# Direct Stripe Order Checkout — Sequence Diagram

[Detailed walkthrough](03-direct-checkout.md) | [Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

Direct order payment via Stripe without debiting wallet balance, enforcing strict mutual exclusion with wallet `PurchaseDebit` and supporting status polling, resume, buyer cancel, internal expiration, and 1-minute background recovery.

```mermaid
sequenceDiagram
    actor Buyer as Buyer
    actor Internal as Commerce / Staff Caller
    participant API as WalletsController / Direct Checkout Handlers
    participant DB as PostgreSQL (DirectOrders / Attempts / Wallets)
    participant Stripe as Stripe Checkout API
    participant Worker as StripeTopupReconciliationWorker (1m)

    Note over Buyer,Worker: == 1. Start Direct Checkout & Mutual Exclusion ==
    Buyer->>API: POST /api/v1/wallets/checkout/direct (orderId, amount 1..99.99M VND)
    API->>DB: BeginTx -> LockOrderPaymentAsync(orderId) -> LockDirectOrder -> LockOrEnsureWallet
    DB->>DB: Check wallet Active VND & check active PurchaseDebit / blocking attempts for orderId
    alt Active PurchaseDebit or Conflicting Attempt Exists
        DB-->>API: Conflict detected
        API-->>Buyer: 409 PAYMENT_CONFLICT
    else Active AwaitingPayment Direct Session Replay (unexpired, matching fingerprint)
        DB-->>API: Existing session URL
        API-->>Buyer: 200 Existing checkout URL (no duplicate Stripe call)
    else Fresh or Resettable (Failed/Expired) Direct Order
        DB->>DB: Upsert DirectPaymentOrder(Pending) + DirectGatewayPayment(Pending, Before=After) + Attempt(CreationPending)
        DB-->>API: Commit Phase 1
        API->>Stripe: CreateSessionAsync (vnd, Recovery=false, idempotencyKey=wallet_direct_...)
        Stripe-->>API: SessionId, CheckoutUrl, ExpiresAt
        API->>DB: BeginTx -> Lock DirectOrder -> Attempt -> set AwaitingPayment & URL -> commit
        API-->>Buyer: 200 DirectCheckoutResponse (transactionId, paymentUrl, status Pending)
    end

    Note over Buyer,Worker: == 2. User Completes Payment & Webhook Settlement ==
    Buyer->>Stripe: Complete payment on Stripe Checkout
    Stripe->>API: POST /api/v1/wallets/stripe/webhooks (Stripe-Signature)
    API->>Stripe: GetSessionAsync + GetPaymentIntentAsync
    Stripe-->>API: Verified Stripe payment state
    API->>DB: BeginTx -> Lock DirectPaymentOrder -> Attempt -> Wallet -> re-check active PurchaseDebit
    alt Wallet PurchaseDebit Won Concurrent Race
        DB->>DB: Escalate Attempt to ReconciliationRequired (do not double-settle)
        API-->>Stripe: 200 OK
    else ConfirmedPaid & No Wallet Conflict
        DB->>DB: DirectOrder=Success, DirectGatewayPayment tx=Success (Balance unchanged), Attempt=Successful
        DB-->>API: Commit (excluded from Shadow Ledger because Balance delta is 0)
        API-->>Stripe: 200 OK
    else CannotSucceed
        DB->>DB: DirectOrder=Failed, Attempt=Expired/Failed (BlocksReplacement=false), DirectTx=Failed
        DB-->>API: Commit
        API-->>Stripe: 200 OK
    end

    Note over Buyer,Worker: == 3. Status Polling, Resume, Buyer Cancel & Internal Expire ==
    Internal->>API: GET /api/v1/wallets/checkout/direct/{orderId} (Owner or Admin/Staff)
    API->>DB: Load DirectPaymentOrder + PaymentIntentId / GatewayTransactionNo
    API-->>Internal: 200 Status (or 404 PAYMENT_NOT_FOUND)

    Buyer->>API: POST /api/v1/wallets/checkout/direct/{orderId}/resume
    API->>Stripe: Verify wallet Active + Stripe session open & unpaid
    API-->>Buyer: 200 paymentUrl (or 409 PAYMENT_CONFLICT)

    alt Buyer Cancel Pending Checkout
        Buyer->>API: POST /api/v1/wallets/checkout/direct/{orderId}/cancel
    else Internal Order Expiration
        Internal->>API: POST ~/api/v1/internal/wallets/checkout/direct/{orderId}/expire (RequireInternalServiceKey)
    end
    API->>Stripe: Expire open Stripe session (verify unpaid & unrecoverable)
    alt Already Completed or CreationPending Uncertain
        API-->>Buyer: 409 PAYMENT_ALREADY_COMPLETED or 409 PAYMENT_CANCELLATION_PENDING
    else Safely Expired
        API->>DB: Lock OrderId -> DirectOrder -> Attempt -> set Attempt=Expired, Order=Failed, Tx=Cancelled/Failed
        API-->>Buyer: 200 Cancelled / Expired
    end

    Note over Buyer,Worker: == 4. Async Background Reconciliation (1-Minute Worker) ==
    loop Every 1 Minute
        Worker->>DB: Query stale AwaitingPayment DirectOrder attempts (>1m)
        Worker->>Stripe: Fetch live Stripe session
        Stripe-->>Worker: Session + PaymentIntent status
        Worker->>DB: Apply StripeSettlement (reconcile_SHA256) or promote >10m null CreationPending
    end
```
