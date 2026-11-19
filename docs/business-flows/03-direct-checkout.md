# Pay an order directly with Stripe

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

**Story:** As a buyer, I want to pay an order through Stripe rather than spend
my wallet balance.

## Start checkout

1. The authenticated buyer sends `POST /api/v1/wallets/checkout/direct`
   with `orderId` and `amount`. The order ID is a nonempty UUID, amount is
   whole VND from 1 through 99,999,999, and an existing VND wallet is required.
2. [CheckoutDirectCommandHandler](../../Wallet.Application/Features/Checkout/Commands/CheckoutDirect/CheckoutDirectCommandHandler.cs)
   invokes [StripeDirectPreparation](../../Wallet.Application/Features/Stripe/StripeDirectPreparation.cs).
   It locks the order and wallet, prevents a conflicting wallet-funded order
   payment, creates a pending direct-order record, pending
   `DirectGatewayPayment` ledger entry and payment attempt, and commits.
3. The provider creates a Stripe session outside the initial database
   transaction; Wallet stores the session correlation and URL. A matching
   active request can reuse the session. Changed owner/amount, an unresolved
   attempt or a conflicting payment yields `409 PAYMENT_CONFLICT`.
4. Wallet returns `200` with `transactionId`, `orderId`, `amount`,
   `paymentUrl`, `status: Pending` and `createdAt`, optionally wrapped by the
   Gateway. Wallet balance is unchanged.

## Payment confirmation and status

1. Stripe posts a signed webhook to `/api/v1/wallets/stripe/webhooks`.
   The shared verification and provider-state checks are described in
   [Stripe top-up](02-stripe-topup.md#confirm-payment-webhook-and-recovery).
   For a confirmed direct payment, [StripeSettlement](../../Wallet.Application/Features/Stripe/StripeSettlement.cs)
   marks the order, attempt and existing direct-payment ledger entry
   successful and records audit/event data; it does not deposit or deduct
   wallet balance. The webhook response is an empty `200` when acknowledged.
2. The buyer polls `GET /api/v1/wallets/checkout/direct/{orderId}`.
   [GetDirectPaymentStatusQueryHandler](../../Wallet.Application/Features/Checkout/Queries/GetDirectPaymentStatus/GetDirectPaymentStatusQueryHandler.cs)
   reads the ledger and provider reference. The owner can read their own
   payment; admin/staff can read any. Unknown or non-owned payment returns
   `404 PAYMENT_NOT_FOUND`.
3. Wallet returns `200` with `transactionId`, `orderId`, `amount`, `currency`,
   `status`, `gatewayTransactionNo`, `createdAt`, `updatedAt`. The Stripe
   PaymentIntent ID may be absent until bound.

Redirecting back from Stripe is not proof of payment. The periodic worker in
this repository selects **top-ups**, not direct-order attempts; do not assume
it automatically recovers direct payments. See the [direct contract](../api/checkout/direct.md)
and [status contract](../api/checkout/direct-status.md).
