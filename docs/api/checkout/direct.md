# Wallet Service — direct Stripe checkout

`POST /api/v1/wallets/checkout/direct` creates a Stripe Checkout session for
an authenticated wallet owner without changing wallet balance.

```json
{
  "orderId": "01000000-0000-0000-0000-000000000001",
  "amount": 850000
}
```

The order ID must be a non-empty UUID and the amount must be a whole VND value
between 1 and 99,999,999. The server configures the return URLs. A successful
response has HTTP `200` and contains `transactionId`, `orderId`, `amount`,
`paymentUrl`, `status` and `createdAt`.

A matching active request reuses the Checkout session. Changed owner or amount,
an unresolved attempt, or an incompatible payment returns `409
PAYMENT_CONFLICT`. A replacement is allowed only after provider-confirmed
finality. Poll [direct payment status](direct-status.md) after redirect;
navigation or cancellation alone does not establish payment status.

The anonymous, signed `POST /api/v1/wallets/stripe/webhooks` endpoint verifies
provider state before settling. More details, including provider errors and
test-mode gates, are in the [Stripe contract](../stripe/payments.md).
