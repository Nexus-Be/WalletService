# Stripe checkout handover: frontend and gateway

> **Status: implemented in WalletService, not yet verified end to end.**
> Stripe initiation, status and webhook routes are wired; VNPay payment routes
> are removed. Gateway forwarding and real Stripe test-mode payments still
> require verification before consumer cutover.

The approved WalletService contract is described in
[payments.md](payments.md). This handover covers test-mode integration only.
Amounts are whole VND; do not multiply them by 100. The maximum payment is
99,999,999 VND. Account-specific payment method and amount limits remain to
be verified.

## Frontend

### Top-up

1. Send `POST /api/v1/wallets/topups/stripe` with the user's authentication,
   an `Idempotency-Key` header and a JSON body:

   ```http
   Idempotency-Key: topup-12345678
   Content-Type: application/json

   {"amount":100000}
   ```

   The amount must be an integer from 10,000 through 99,999,999. The key must
   be 1-128 ASCII letters, digits, hyphens or underscores. Generate a unique
   key for each new top-up and retain it across retries of that same request.

2. The `201` response contains `transactionId`, `paymentUrl`,
   `amount`, `currency`, `status` and `createdAt`. Redirect the user to
   `paymentUrl`; do not construct a provider URL or mark a payment successful
   from the redirect alone.
3. Poll `GET /api/v1/wallets/topups/{transactionId}` as the same user. The
   response contains `transactionId`, `amount`, `currency`, `status`,
   `createdAt` and `updatedAt`. A different user receives `404`.

### Direct order checkout

Send `POST /api/v1/wallets/checkout/direct` with authentication and:

```json
{"orderId":"11111111-1111-1111-1111-111111111111","amount":100000}
```

The amount must be a positive whole VND value no greater than 99,999,999.
The `200` response contains `transactionId`, `orderId`, `amount`,
`paymentUrl`, `status` and `createdAt`. Redirect to `paymentUrl`, then poll
`GET /api/v1/wallets/checkout/direct/{orderId}`. The status response contains
`transactionId`, `orderId`, `amount`, `currency`, `status`,
`gatewayTransactionNo`, `createdAt` and `updatedAt`. The gateway transaction
number may be null and, when available, is the Stripe PaymentIntent ID.

Do not send VNPay bank codes or return URLs. Success and cancellation URLs
are configured on the server. A browser return or cancellation is not proof
of payment success or failure. Delayed methods can remain `Pending`; use the
owned status endpoint for the authoritative outcome.

### Checkout return page

Adapt the existing frontend route `/buyer/payment/result` for both Stripe
top-ups and direct orders. Configure WalletService with these fixed URLs,
substituting the frontend's actual HTTPS origin:

```text
STRIPE_SUCCESS_URL=https://<frontend-origin>/buyer/payment/result?checkout=return
STRIPE_CANCEL_URL=https://<frontend-origin>/buyer/payment/result?checkout=cancel
```

These are frontend pages, not WalletService API endpoints. The route and query
parameters above are a handover proposal: the existing result page currently
expects an `orderId` query parameter and does not implement this Stripe flow.
Do not configure the URLs until the frontend route is updated and accessible
at the chosen origin. WalletService currently uses the same fixed URLs for
all attempts and does not append a top-up ID or order ID.

Before redirecting to Stripe, the frontend must retain the payment kind and
the corresponding `transactionId` for a top-up or `orderId` for a direct
checkout, for example in tab-scoped session storage. On return, use that
context to call the appropriate owner-protected status endpoint described
above. Handle missing context by showing a neutral message and links back to
the wallet and orders, rather than claiming success or failure. A return or
cancellation hint is not authoritative, and delayed payment may remain
pending; display the API status and allow the user to refresh it.

### Retries and errors

- Retry the same top-up request with the same idempotency key and amount.
  An active matching request reuses its session. A changed amount, unresolved
  creation or a completed/expired attempt may return `409 PAYMENT_CONFLICT`;
  use status and follow the operator recovery process rather than assuming
  that a failed request means no provider session exists.
- Direct checkout is identified by order ID, owner and amount. Reuse an
  active matching request. Do not initiate another payment after cancellation
  or a timeout unless the service confirms a replacement is safe.
- Expected errors include `400 VAL_INVALID_INPUT`, `401 UNAUTHORIZED`,
  `404 PAYMENT_NOT_FOUND` or `WALLET_NOT_FOUND`, `409 PAYMENT_CONFLICT`, and
  `503 PAYMENT_PROVIDER_UNAVAILABLE`. Surface failures to the user; do not
  treat an error or a browser redirect as a successful payment.
- Payment statuses are PascalCase: `Pending`, `Success`, `Failed`, `Expired`.
  The effective retry behavior depends on confirmed provider finality.

## Gateway

- Route the three authenticated payment requests and their status requests to
  WalletService. Preserve the existing authenticated user context and forward
  the top-up `Idempotency-Key` header unchanged.
- Route anonymous `POST /api/v1/wallets/stripe/webhooks` to WalletService.
  Forward the exact raw body bytes and the `Stripe-Signature` header unchanged.
  Do not deserialize, reserialize, compress or otherwise transform the
  signature input. Do not log request bodies, signatures or secrets.
- Preserve the upstream status code. The webhook returns an empty
  `200` for committed, duplicate or irrelevant events, `400` for invalid
  signatures and `5xx` for retryable failures. Do not wrap the webhook
  acknowledgement in a gateway response envelope.
- WalletService returns pure DTOs. Confirm the gateway's actual response
  envelope for authenticated requests before frontend integration; do not
  assume the examples here include a gateway envelope.

## Integration checklist

- [x] WalletService routes are implemented and the old VNPay routes removed.
- [ ] Gateway forwards the idempotency header and raw signed webhook bytes.
- [ ] Gateway permits anonymous webhook delivery and does not envelope its
      acknowledgement.
- [ ] Frontend verifies owner-only status, redirect and delayed payment flows.
- [ ] A Stripe test-mode payment and webhook replay are verified end to end.
- [ ] Gateway and frontend agree on the deployed response envelope.

Live operation remains blocked pending Stripe wallet-funding eligibility,
account/payment-method confirmation and an approved refunds/disputes policy.
