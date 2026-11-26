# Stripe Wallet Top-Up — Sequence Diagram

[Detailed walkthrough](02-stripe-topup.md) | [Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

Two-phase DB-first Stripe Checkout top-up lifecycle: phase 1 reserves an idempotent attempt, Stripe session creation runs outside any DB lock, phase 2 commits the checkout URL, and confirmed funds arrive via signed webhook or 1-minute background polling.

```mermaid
sequenceDiagram
    actor Buyer as Wallet Owner
    participant API as WalletsController / Stripe Handlers
    participant DB as PostgreSQL (Wallets / Topups / Attempts / Webhooks)
    participant Stripe as Stripe Checkout API
    participant Worker as StripeTopupReconciliationWorker (1m)

    Note over Buyer,Worker: == 1. Phase 1 - DB-First Topup Preparation ==
    Buyer->>API: POST /api/v1/wallets/topups/stripe (Idempotency-Key, amount 10k..99.99M VND)
    API->>DB: BeginTx -> LockOrEnsureWallet -> verify Active VND & SHA-256 fingerprint
    DB->>DB: Query StripePaymentAttempts (IgnoreQueryFilters) by (OwnerId, IdempotencyKey)
    alt Valid AwaitingPayment Replay (unexpired, BlocksReplacement = true, Topup Pending)
        DB-->>API: Existing CheckoutUrl & Topup
        API-->>Buyer: 201 Existing TopupStripeResponse (no duplicate Stripe call)
    else Conflicting / Expired / Settled Match
        DB-->>API: Fingerprint or state mismatch
        API-->>Buyer: 409 PAYMENT_CONFLICT
    else Fresh Attempt
        DB->>DB: Insert WalletTopup(Pending) + StripePaymentAttempt(CreationPending) + Audit(TopupRequest)
        DB-->>API: Commit Phase 1

        Note over Buyer,Worker: == 2. Phase 2 - Stripe Session Creation (Outside DB Tx) ==
        API->>Stripe: CreateSessionAsync (payment, vnd, Recovery=false, idempotencyKey=wallet_topup_...)
        alt Stripe Unavailable or 4xx Error
            Stripe-->>API: Error / Timeout
            API-->>Buyer: 400 STRIPE_REQUEST_REJECTED or 503 PAYMENT_PROVIDER_UNAVAILABLE
        else Valid cs_test Session (!Livemode, Exact VND)
            Stripe-->>API: SessionId, CheckoutUrl, ExpiresAt
            API->>DB: BeginTx -> Lock Topup -> Attempt -> set AwaitingPayment & URL -> commit
            DB-->>API: Updated attempt
            API-->>Buyer: 201 TopupStripeResponse (transactionId, paymentUrl, status Pending)
        end
    end

    opt Unfinished Session Resume
        Buyer->>API: POST /api/v1/wallets/topups/{id}/resume
        API->>Stripe: GetSessionAsync (verify Active wallet, open, unpaid, exact metadata/amount)
        Stripe-->>API: Live session open & unpaid
        API-->>Buyer: 200 Existing paymentUrl + sessionExpiresAt (or 409 PAYMENT_CONFLICT)
    end

    Note over Buyer,Worker: == 3. User Completes Payment & Signed Webhook ==
    Buyer->>Stripe: Complete payment on Stripe Checkout
    Stripe->>API: POST /api/v1/wallets/stripe/webhooks (Stripe-Signature)
    API->>API: Cap body <= 1 MiB, verify HMAC signature & reject Livemode=true
    alt Audited-Ignored Event (payment_intent.payment_failed / refund / dispute)
        API->>DB: Record StripeWebhookEvents (AuditedIgnored)
        API-->>Stripe: 200 OK
    else Handled Checkout Session Event
        API->>Stripe: GetSessionAsync + GetPaymentIntentAsync (never trust webhook body alone)
        Stripe-->>API: Live Stripe session & PaymentIntent status
        API->>API: StripeReconciliationPolicy evaluates outcome

        Note over Buyer,Worker: == 4. Atomic Settlement & Conflict Outcomes ==
        alt Policy = Pending
            API-->>Stripe: 500 Throw so Stripe retries later
        else Policy Resolved (ConfirmedPaid / CannotSucceed / IntegrityMismatch)
            API->>DB: BeginTx -> LockTopup -> LockAttempt -> LockWallet -> check StripeWebhookEvents dedupe
            alt Already Recorded or Terminal
                DB-->>API: Duplicate event
                API-->>Stripe: 200 OK (idempotent no-op)
            else ConfirmedPaid (credits even frozen wallets)
                DB->>DB: wallet.Deposit + insert TopupStripe(Success) + Topup(Success) + Attempt(Successful)
                DB-->>API: Commit (+ Shadow Ledger EXTERNAL_GATEWAY row if enabled)
                API-->>Stripe: 200 OK
            else CannotSucceed
                DB->>DB: Attempt=Expired/Failed (BlocksReplacement=false) + Topup=Failed + Audit(TopupFail)
                DB-->>API: Commit
                API-->>Stripe: 200 OK
            else IntegrityMismatch (Amount/Currency/Metadata mismatch)
                DB->>DB: Escalate Attempt to ReconciliationRequired + Audit(StripeAttemptResolve)
                DB-->>API: Commit
                API-->>Stripe: 200 OK (requires Admin ResolveStripeAttempt)
            end
        end
    end

    Note over Buyer,Worker: == 5. Async Background Recovery (1-Minute Worker Loop) ==
    loop Every 1 Minute (if Stripe config valid)
        Worker->>DB: Fetch <= 30 CreationPending (>10m) & <= 30 AwaitingPayment (>1m) Topup/Direct attempts
        Worker->>DB: Promote >10m null-session CreationPending to ReconciliationRequired
        Worker->>Stripe: Poll live session for stale AwaitingPayment rows
        Stripe-->>Worker: Live status
        Worker->>DB: Invoke StripeSettlement.ApplyAsync with deterministic reconcile_SHA256 eventId
    end
```
