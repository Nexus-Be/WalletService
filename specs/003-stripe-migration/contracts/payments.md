# Stripe payment contracts

These are implemented test-mode contracts for WalletService's pure DTO boundary.
All business-user calls require the existing authenticated user context.
Stripe amounts use whole VND and cannot exceed 99,999,999 VND, the
eight-digit PaymentIntent limit. Top-ups additionally require at least
10,000 VND; account and payment-method minimums still require verification.

| Route | Request | Success | Behavior |
|---|---|---|---|
| `POST /api/v1/wallets/topups/stripe` | whole-VND `amount`; `Idempotency-Key` header | `201` with `transactionId`, `paymentUrl`, `amount`, `currency`, `status`, `createdAt` | Persist an attempt and return its hosted URL; identical retry reuses it, conflicting inputs return `409`. |
| `GET /api/v1/wallets/topups/{id}` | ID | `200` with owned top-up status, amount, currency and timestamps | Non-owner and missing ID return `404`. |
| `POST /api/v1/wallets/checkout/direct` | `orderId`, whole-VND `amount` | `200` with existing direct-payment response shape and Stripe `paymentUrl` | No `bankCode` or caller-controlled return URL; existing active attempt reuses session. A new attempt requires terminal provider confirmation. |
| `GET /api/v1/wallets/checkout/direct/{orderId}` | order ID | `200` existing status response | Owner or existing privileged caller; `gatewayTransactionNo` carries the PaymentIntent ID when available. |
| `POST /api/v1/wallets/stripe/webhooks` | raw body and `Stripe-Signature` | `200` after committed handling or valid duplicate | Anonymous, signature required; invalid signature `400`, processing failure `5xx`; response is not gateway-enveloped. |

Validation errors use `VAL_INVALID_INPUT` and existing ProblemDetails format.
Missing user identity returns `401`; missing wallet returns `404`; changed
idempotency input or an unresolved earlier attempt returns `409`. Provider
outages return `503 PAYMENT_PROVIDER_UNAVAILABLE`. Exact replay rules, payloads
and error matrix are in the service API document. Gateway and real Stripe
test-mode integration still require verification.
Success and cancel URLs are server-configured absolute HTTPS URLs. The frontend
polls owned status after redirect, never treating
navigation as successful payment.

The top-up `transactionId` is the wallet top-up ID and is the `{id}` used by
the status endpoint. The direct payment `transactionId` retains its existing
ledger transaction meaning. These identifiers are distinct.
