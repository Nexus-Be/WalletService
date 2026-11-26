# Add money with Stripe

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Direct checkout](03-direct-checkout.md) | [Sequence diagram](02-stripe-topup.diagram.md)

**Story:** As a wallet owner, I want to add VND to my wallet with Stripe Checkout, resume an open unfinished session, and be protected from duplicate credits or silent stuck payments.

> **Gateway note:** There are no active VNPAY top-up endpoints/handlers (only legacy schema fields/seed rows remain) and no seller/buyer top-up cancel endpoint (unlike direct checkout).

## Start a top-up

1. Send `POST /api/v1/wallets/topups/stripe` (`[RequireRole]`) with JSON `{ "amount": <whole VND 10,000..99,999,999> }` and required header `Idempotency-Key` (1–128 chars `[A-Za-z0-9_-]`). An existing `ACTIVE` VND wallet is required (`404 WALLET_NOT_FOUND` or `403 WALLET_FROZEN`).
2. [TopupStripeCommandHandler](../../Wallet.Application/Features/Topups/Commands/TopupStripe/TopupStripeCommandHandler.cs) calls [StripeTopupPreparation](../../Wallet.Application/Features/Stripe/StripeTopupPreparation.cs) in two database transactions separated by the external Stripe API call:
   - **Phase 1 (DB-first commit):** Calls `LockOrEnsureWalletByUserIdAsync` (reloaded unmodified), checks `Currency == Vnd` and `wallet.EnsureActive()`, computes fingerprint `SHA256("topup\n{ownerId:D}\n{amount:0}\nVND")`, and checks `StripePaymentAttempts` (`IgnoreQueryFilters()`) for `(OwnerId, ClientIdempotencyKey)`:
     - Equivalent replay is allowed **only** when not soft-deleted, `Purpose == Topup`, same fingerprint/amount, `Status == AwaitingPayment`, `BlocksReplacement == true`, valid `CheckoutUrl`, unexpired `SessionExpiresAt`, and parent `WalletTopup` is `Pending` -> returns existing session without calling Stripe again. Conflicting/expired/settled/uncertain matches return `409 PAYMENT_CONFLICT`.
     - Fresh attempt inserts `WalletTopup(Pending)`, `StripePaymentAttempt(CreationPending, BlocksReplacement = true, ProviderIdempotencyKey = "wallet_topup_{guid:N}")`, and `AuditAction.TopupRequest`, then commits.
   - **Stripe session creation (outside DB tx):** Calls `IStripeCheckoutClient.CreateSessionAsync` ([StripeCheckoutClient](../../Wallet.Infrastructure/Payments/StripeCheckoutClient.cs), `Mode = payment`, `Currency = vnd`, `AfterExpiration.Recovery.Enabled = false`, expiry 30–1440m) and validates the session (`cs_test_...`, `!Livemode`, exact VND amount). Stripe 4xx -> `400 STRIPE_REQUEST_REJECTED`; transport/timeout -> `503 PAYMENT_PROVIDER_UNAVAILABLE`.
   - **Phase 2 (Commit correlation):** Locks `WalletTopup` -> `StripePaymentAttempt`, verifies still `CreationPending`/`Pending`/`BlocksReplacement`, records `CheckoutSessionId`, `CheckoutUrl`, `SessionExpiresAt`, transitions attempt to `AwaitingPayment`, and commits.
3. Wallet returns `201 Created` (`TopupStripeResponse`: `transactionId` = top-up ID, `paymentUrl`, `amount`, `currency`, `status: "Pending"`, `createdAt`). Issuing a URL never credits the wallet.

## Confirm payment: webhook, verification & background recovery

1. **Webhook (`POST /api/v1/wallets/stripe/webhooks`, `[AllowAnonymous]`):**
   - [StripeWebhookBodyReader](../../Wallet.Api/Common/StripeWebhookBodyReader.cs) caps body size at `1 MiB` (`413 WEBHOOK_PAYLOAD_TOO_LARGE`).
   - [ProcessStripeWebhookCommandHandler](../../Wallet.Application/Features/Stripe/Commands/ProcessStripeWebhook/ProcessStripeWebhookCommandHandler.cs) and [StripeWebhookProcessing](../../Wallet.Application/Features/Stripe/StripeWebhookProcessing.cs) verify `Stripe-Signature` (`400 VAL_INVALID_SIGNATURE` or `400 VAL_UNSUPPORTED_WEBHOOK_VERSION`) and reject live-mode events (`!livemode`).
   - Audited-ignored events (`payment_intent.payment_failed`, `charge.refunded`, `charge.dispute.created`, `charge.dispute.closed`) are recorded once in `StripeWebhookEvents` (`Outcome = "AuditedIgnored"`) and acked `200`.
   - Handled events (`checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `checkout.session.expired`) **always query Stripe's API directly** (`GetSessionAsync` + `GetPaymentIntentAsync`) and evaluate `StripeReconciliationPolicy`:
     - `ConfirmedPaid`: Complete/paid session + succeeded PaymentIntent + exact VND amount/metadata.
     - `CannotSucceed`: Expired/complete & unpaid with `Recovery.Enabled == false` (and `canceled` PaymentIntent if one exists).
     - `IntegrityMismatch` -> escalates attempt to `ReconciliationRequired` (`AuditAction.StripeAttemptResolve` / manual admin resolution).
     - `Pending` -> webhook handler throws (`500`) so Stripe retries later.
2. **Atomic settlement ([StripeSettlement](../../Wallet.Application/Features/Stripe/StripeSettlement.cs)):**
   - Lock order: Parent (`LockTopupByIdAsync`) -> `LockStripePaymentAttemptAsync` -> `LockWalletByIdAsync`. Dedupes by `StripeWebhookEvents.ProviderEventId`.
   - On `ConfirmedPaid`: credits wallet (`TopupStripe` transaction, does **not** reject frozen wallets so confirmed customer money is never lost), marks `WalletTopup` `Success`, marks `StripePaymentAttempt` `Successful`, logs `AuditAction.TopupSuccess`. (With Shadow Ledger enabled, emits `EXTERNAL_GATEWAY` queue row automatically.)
   - On `CannotSucceed`: marks `BlocksReplacement = false`, updates statuses to `Expired`/`Failed`, logs `AuditAction.TopupFail`.
   - Returns empty `200` to Stripe for handled/duplicate/ignored events.
3. **Background reconciliation ([StripeTopupReconciliationWorker](../../Wallet.Api/StripeTopupReconciliationWorker.cs)):** Runs every **1 minute** via `ReconcilePendingTopupsCommandHandler` (also triggerable manually by admins).
   - Promotes up to 30 stuck `CreationPending` attempts older than `10 minutes` (null `CheckoutSessionId`) to `ReconciliationRequired` (logged `Critical`).
   - Polls up to 30 `Topup` and 30 `DirectOrder` attempts in `AwaitingPayment` (`CheckoutSessionId != null`, older than `1 minute`) with deterministic event ID `"reconcile_" + SHA256(...)`, invoking `StripeSettlement.ApplyAsync`.

## Check, list, or resume

- `GET /api/v1/wallets/topups?page=1&pageSize=10&status=...` ([ListTopupsQueryHandler](../../Wallet.Application/Features/Topups/Queries/ListTopups/ListTopupsQueryHandler.cs)): newest-first paginated Stripe top-ups owned by the user (`400 VAL_INVALID_STATUS` on bad status).
- `GET /api/v1/wallets/topups/{id:guid}` ([GetTopupStatusQueryHandler](../../Wallet.Application/Features/Topups/Queries/GetTopupStatus/GetTopupStatusQueryHandler.cs)): owner-scoped status (`404 PAYMENT_NOT_FOUND` if missing or owned by another user) including advisory `canResume` (`Pending` + `AwaitingPayment` + unexpired `SessionExpiresAt` + non-empty URL).
- `POST /api/v1/wallets/topups/{id:guid}/resume` ([ResumeTopupCommandHandler](../../Wallet.Application/Features/Topups/Commands/ResumeTopup/ResumeTopupCommandHandler.cs)): enforces `wallet.EnsureActive()` (`403 WALLET_FROZEN`), verifies live with Stripe (`GetSessionAsync`) that the stored session is still `open` and `unpaid` with matching metadata/amount (`409 PAYMENT_CONFLICT` otherwise), and returns `200 { transactionId, paymentUrl, sessionExpiresAt }` without creating a second session.

See [Stripe payments API](../api/stripe/payments.md) and [Stripe top-up recovery](../api/stripe/topup-recovery.md) for full request/response schemas and error codes.
