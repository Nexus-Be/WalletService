# Pay an order directly with Stripe

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Stripe top-up](02-stripe-topup.md) | [Sequence diagram](03-direct-checkout.diagram.md)

**Story:** As a buyer, I want to pay an order directly via Stripe without debiting my wallet balance, resume an unfinished session, cancel a pending checkout, or let internal workflows expire stale unpaid sessions.

## Start direct checkout

1. Send `POST /api/v1/wallets/checkout/direct` (`[RequireRole]`) with `{ "orderId", "amount" }`. `orderId` is a non-empty UUID, `amount` is whole VND `1`–`99,999,999`, and an existing `ACTIVE` VND wallet is required (`404 WALLET_NOT_FOUND` or `403 WALLET_FROZEN`). No `Idempotency-Key` header is used; idempotency is keyed by `orderId`.
2. [CheckoutDirectCommandHandler](../../Wallet.Application/Features/Checkout/Commands/CheckoutDirect/CheckoutDirectCommandHandler.cs) calls [StripeDirectPreparation](../../Wallet.Application/Features/Stripe/StripeDirectPreparation.cs):
   - **Phase 1 Tx:** Acquires `LockOrderPaymentAsync(orderId)` -> (if `DirectPaymentOrder` exists) `LockDirectPaymentOrderByIdAsync` -> `LockOrEnsureWalletByUserIdAsync(ownerId)`.
   - Verifies active VND wallet and **mutual exclusion**: rejects with `409 PAYMENT_CONFLICT` if any non-Failed/non-Cancelled `PurchaseDebit` exists for `orderId`.
   - Computes fingerprint `SHA256("direct\n{ownerId:D}\n{orderId:D}\n{amount:0}\nVND")`.
   - If an attempt has `BlocksReplacement == true`: replays the existing session URL only if `order.Pending`, `ledger.Pending`, `attempt.AwaitingPayment`, and session is unexpired with matching owner/amount; otherwise `409 PAYMENT_CONFLICT`.
   - If a prior attempt finished cleanly (`Failed`/`Expired`, non-blocking, no `ReconciliationRequired`, order/ledger `Failed`/`Expired`/`Cancelled`), resets order/ledger to `Pending` and stages a fresh `CreationPending` attempt (`ProviderIdempotencyKey = "wallet_direct_{guid:N}"`). Otherwise creates `DirectPaymentOrder(Pending)`, `WalletTransaction(DirectGatewayPayment, Pending, BalanceBefore = BalanceAfter = wallet.Balance)`, `StripePaymentAttempt(CreationPending)`, and `AuditAction.DirectPaymentRequest`.
   - **External Stripe call (outside Tx):** Creates and validates Stripe Checkout Session (fixed `30` min expiry, `Recovery.Enabled = false`).
   - **Phase 2 Tx:** Locks `DirectPaymentOrder` -> `StripePaymentAttempt`, transitions attempt to `AwaitingPayment` with session correlation & URL.
3. Returns `200` (`transactionId`, `orderId`, `amount`, `paymentUrl`, `status: "Pending"`, `createdAt`). Wallet `Balance` and `LockedBalance` remain unchanged.

## Webhook confirmation & reconciliation

1. Stripe posts signed events to `POST /api/v1/wallets/stripe/webhooks` (`[AllowAnonymous]`).
2. [StripeSettlement.ApplyAsync](../../Wallet.Application/Features/Stripe/StripeSettlement.cs) locks `DirectPaymentOrder` -> `StripePaymentAttempt` -> `WalletAccount`:
   - Re-checks that no active `PurchaseDebit` exists for `orderId` (if a wallet payment won a concurrent race before settlement, escalates attempt to `ReconciliationRequired` instead of settling twice).
   - Marks `DirectPaymentOrder = TopupStatus.Success`, `DirectGatewayPayment` transaction `= TransactionStatus.Success` (keeping `BalanceBefore == BalanceAfter` unchanged), `StripePaymentAttempt = Successful`, and logs `AuditAction.DirectPaymentSuccess`. Because `WalletAccount.Balance` does not change, `DirectGatewayPayment` is excluded from Shadow Ledger posting (`WalletDbContext` and invariant `I12`).
3. [ReconcilePendingTopupsCommandHandler](../../Wallet.Application/Features/Stripe/Commands/ReconcilePendingTopups/ReconcilePendingTopupsCommandHandler.cs) (run every **1 minute** by [StripeTopupReconciliationWorker](../../Wallet.Api/StripeTopupReconciliationWorker.cs) and on admin demand) **does poll pending `DirectOrder` attempts** (`AwaitingPayment`, older than 1 minute, up to 30 per pass) alongside top-ups, and promotes `CreationPending` attempts older than 10 minutes to `ReconciliationRequired`.

## Check status, resume, buyer cancel & internal expire

All direct lifecycle mutations share [StripeDirectLifecycle.CancelOrExpireAsync](../../Wallet.Application/Features/Stripe/StripeDirectLifecycle.cs):

| Operation | Route & Caller | Behavior & Guarantees |
|---|---|---|
| Poll status | `GET /api/v1/wallets/checkout/direct/{orderId}` ([GetDirectPaymentStatusQueryHandler](../../Wallet.Application/Features/Checkout/Queries/GetDirectPaymentStatus/GetDirectPaymentStatusQueryHandler.cs)) | Owner reads own payment; `admin`/`staff` may read any (`AllowAnyUser = true`). Missing/non-owned -> `404 PAYMENT_NOT_FOUND`. Populates `gatewayTransactionNo` from `PaymentIntentId` (or legacy `VnpTransactionNo`). |
| Resume open session | `POST /api/v1/wallets/checkout/direct/{orderId:guid}/resume` ([ResumeDirectPaymentCommandHandler](../../Wallet.Application/Features/Checkout/Commands/ResumeDirectPayment/ResumeDirectPaymentCommandHandler.cs)) | Owner only; enforces `wallet.EnsureActive()`, fetches live Stripe session (`open`, `unpaid`, null intent, matching amount/metadata) -> returns `200` with existing URL or `409 PAYMENT_CONFLICT`. |
| Buyer cancel | `POST /api/v1/wallets/checkout/direct/{orderId:guid}/cancel` ([CancelDirectPaymentCommandHandler](../../Wallet.Application/Features/Checkout/Commands/CancelDirectPayment/CancelDirectPaymentCommandHandler.cs)) | Owner only. Expires open Stripe session (confirming unpaid & unrecoverable), locks `OrderId -> DirectPaymentOrder -> Attempt`, sets `Attempt = Expired` (`BlocksReplacement = false`), `Order = Failed`, `DirectGatewayPayment` tx `= Cancelled`, logs `AuditAction.DirectPaymentCancel`. Idempotent if already `Failed`/`Expired` and non-blocking. |
| Internal S2S expire | `POST ~/api/v1/internal/wallets/checkout/direct/{orderId:guid}/expire` ([ExpireDirectPaymentCommandHandler](../../Wallet.Application/Features/Checkout/Commands/ExpireDirectPayment/ExpireDirectPaymentCommandHandler.cs)) | `[RequireInternalServiceKey]`, any internal order expiry (`isInternalExpire = true`, no user ownership header required). Same live Stripe expiry verification; sets `Attempt = Expired`, `Order = Failed`, `DirectGatewayPayment` tx `= Failed`, logs `AuditAction.DirectPaymentExpire`. |

Cancellation or expiry rejects completed payments with `409 PAYMENT_ALREADY_COMPLETED` and rejects uncertain/pending-creation states with `409 PAYMENT_CANCELLATION_PENDING` rather than risk double-charging or cancelling a paid session. See [direct contract](../api/checkout/direct.md) and [direct status contract](../api/checkout/direct-status.md).
