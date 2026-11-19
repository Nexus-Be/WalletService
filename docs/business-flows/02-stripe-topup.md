# Add money with Stripe

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

**Story:** As a wallet owner, I want to add money and later recover an
unfinished checkout without being charged twice.

## Start a top-up

1. The user sends `POST /api/v1/wallets/topups/stripe` with JSON `amount` and
   `Idempotency-Key`. Identity comes from the request context, not the body.
   The amount is whole VND between 10,000 and 99,999,999; a VND wallet must
   already exist. The key is required and limited to ASCII letters, digits,
   hyphens and underscores, up to 128 characters.
2. [TopupStripeCommandHandler](../../Wallet.Application/Features/Topups/Commands/TopupStripe/TopupStripeCommandHandler.cs)
   calls [StripeTopupPreparation](../../Wallet.Application/Features/Stripe/StripeTopupPreparation.cs).
   It locks the wallet, stores a pending top-up and attempt plus an audit
   entry, then commits. It creates a Stripe Checkout session outside that
   transaction and stores the session URL and correlation under locks.
3. Wallet returns `201` with `transactionId` (the top-up ID), `paymentUrl`,
   `amount`, `currency`, `status: Pending` and `createdAt`. If sent via the
   Gateway, that DTO is normally in its `data` field.
4. An equivalent retry with the same key can return the original open session.
   Conflicting, expired, settled or uncertain attempts return `409
   PAYMENT_CONFLICT` instead of fabricating a new URL. Provider unavailability
   can return `503`.

No wallet credit is made merely because a checkout URL was issued or the
browser returned from Stripe.

## Confirm payment: webhook and recovery

1. Stripe sends the raw body and `Stripe-Signature` to
   `POST /api/v1/wallets/stripe/webhooks`; this anonymous endpoint can be
   reached directly or through configured routing. It is not a user request.
2. [ProcessStripeWebhookCommandHandler](../../Wallet.Application/Features/Stripe/Commands/ProcessStripeWebhook/ProcessStripeWebhookCommandHandler.cs)
   and [StripeWebhookProcessing](../../Wallet.Application/Features/Stripe/StripeWebhookProcessing.cs)
   verify the signature, recognize relevant events, correlate the attempt, and
   retrieve Stripe session/payment intent state. Invalid signatures get `400`;
   retryable processing failures return `5xx`.
3. [StripeSettlement](../../Wallet.Application/Features/Stripe/StripeSettlement.cs)
   verifies provider state and consistency. A confirmed payment credits the
   wallet once, marks the top-up and attempt successful, and records the
   `TopupStripe` transaction, audit and event in one database transaction.
   Duplicate events do not credit again. Inconsistent state is recorded for
   reconciliation rather than silently credited.
4. A valid handled, duplicate or irrelevant webhook gets an empty `200` back
   to Stripe. It is not wrapped by the checked-in Gateway's response wrapper.
5. Independently, the [background worker](../../Wallet.Api/StripeTopupReconciliationWorker.cs)
   periodically invokes [ReconcilePendingTopups](../../Wallet.Application/Features/Stripe/Commands/ReconcilePendingTopups/ReconcilePendingTopupsCommandHandler.cs)
   for eligible pending top-ups with a known session. It checks Stripe and
   uses the same settlement logic. It has no HTTP response to the Gateway;
   errors are logged for a later cycle.

## Check, list or resume

- `GET /api/v1/wallets/topups/{id}` returns the owner's top-up and
  `status`, `canResume` and `sessionExpiresAt`; another owner's or missing
  payment returns `404 PAYMENT_NOT_FOUND`.
- `GET /api/v1/wallets/topups?page=1&pageSize=10` returns the owner's
  newest-first paginated Stripe top-ups.
- `POST /api/v1/wallets/topups/{id}/resume` uses
  [ResumeTopupCommandHandler](../../Wallet.Application/Features/Topups/Commands/ResumeTopup/ResumeTopupCommandHandler.cs)
  to check the stored pending attempt and confirm with Stripe that the same
  session remains open and unpaid. It returns `200` with `transactionId`,
  `paymentUrl`, `sessionExpiresAt`; it does not create another checkout.
  `canResume` is advisory; an unavailable session gives `409 PAYMENT_CONFLICT`.

The contract and full error details are in [Stripe payments](../api/stripe/payments.md)
and [top-up recovery](../api/stripe/topup-recovery.md). Live integration is
not verified by the code trace.
