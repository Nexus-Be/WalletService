# Stripe checkout handover: frontend, gateway and Commerce

> **Status: implemented in WalletService, not yet verified end to end.**
> Stripe initiation, status and webhook routes are wired; VNPay payment routes
> are removed. Gateway forwarding and real Stripe test-mode payments still
> require verification before consumer cutover.

**Commerce cutover blocker:** WalletService records the Stripe payment but
does not notify Commerce or update its order. The Commerce order-payment
integration, order validation and recovery described below are requirements
for the receiving teams, not implemented or verified end-to-end behavior.

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

The existing WalletService endpoint accepts authenticated
`POST /api/v1/wallets/checkout/direct` requests with:

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

This is the WalletService contract, not a recommendation for the frontend to
call it directly. Commerce must first validate the buyer and authoritative
order terms, and the integration must prevent bypassing that validation; see
the Commerce section below.

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

| Browser route | Purpose | Frontend behavior |
|---|---|---|
| `/buyer/payment/result?checkout=return` | Customer returned from Stripe Checkout | Retrieve and display the current payment status. |
| `/buyer/payment/result?checkout=cancel` | Customer left Stripe Checkout | Retrieve and display the current payment status; do not assume the attempt is cancelled or safe to retry. |

Set `Stripe__SuccessUrl` and `Stripe__CancelUrl` in the Render service
environment to the corresponding absolute frontend URLs. The Render
WalletService host is not the frontend origin; do not use it as the return
page host unless a frontend is actually served there.

Before redirecting to Stripe, the frontend must retain the payment kind and
the corresponding `transactionId` for a top-up or `orderId` for a direct
checkout, for example in tab-scoped session storage. On return, use that
context to call the appropriate owner-protected status endpoint described
above. Handle missing context by showing a neutral message and links back to
the wallet and orders, rather than claiming success or failure. A return or
cancellation hint is not authoritative, and delayed payment may remain
pending; display the API status and allow the user to refresh it.

Use a distinct browser tab for each concurrent checkout, or otherwise prevent
one checkout from overwriting another's retained context. Tab-scoped storage
may be unavailable after a browser restart or when a return opens a new tab;
in that case, show the neutral recovery UI instead of guessing an identifier.
Keep authentication tokens and secrets out of the stored payment context.

The existing WalletService APIs for the return page are:

| Payment kind | Authenticated API | Identifier to retain |
|---|---|---|
| Wallet top-up | `GET /api/v1/wallets/topups/{transactionId}` | `transactionId` from top-up creation |
| Direct order | `GET /api/v1/wallets/checkout/direct/{orderId}` | `orderId` from direct checkout |

Call these through the gateway with the original user's authenticated
identity. Show `Pending` as awaiting confirmation, including after a cancel
return. Show `Success`, `Failed`, or `Expired` only when reported by the API;
surface lookup errors and offer a retry. The gateway response envelope and
end-to-end forwarding must be verified before integration.

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
- Payment statuses are PascalCase. Top-ups can return `Pending`, `Success`,
  `Failed` or `Expired`; the direct-payment ledger returns `Pending`,
  `Success` or `Failed`. Retry behavior depends on confirmed provider finality.

## Commerce: end-to-end order payment

### Current boundaries

- Commerce owns the order, buyer, payable total, currency, order lifecycle and
  fulfilment. WalletService owns the direct-payment attempt and payment ledger;
  Stripe owns the provider payment. These are separate records and a
  successful WalletService payment does **not** automatically mark a Commerce
  order paid. WalletService does not currently push a payment notification to
  Commerce.
- `POST /api/v1/wallets/checkout/direct` accepts the authenticated user,
  `orderId` and `amount`; it does not fetch the Commerce order or verify the
  order's owner, payable state or authoritative total. The user must have a
  VND wallet. Do not treat a frontend-supplied amount or order ID as proof of
  the order terms.
- After a verified Stripe webhook and provider reconciliation, WalletService
  updates its own direct-payment order and ledger. Commerce must independently
  reconcile that result before advancing its order. The browser redirect and
  frontend-reported status are not proof of settlement.

### Required direct-Stripe integration

The following is an integration design, not a claim that Commerce routes,
background workers or delivery mechanisms already exist:

1. The buyer starts checkout through the frontend and gateway. Commerce must
   authenticate the buyer, retrieve the order server-side, verify ownership and
   that it is payable, and derive the exact whole-VND amount and currency from
   authoritative order data. Protect or mediate direct WalletService
   initiation so a caller cannot bypass these checks by posting an arbitrary
   order ID or amount. Agree the service-to-service identity and network
   boundary with the gateway and Commerce teams before enabling this flow.
2. Commerce or a trusted orchestration layer initiates WalletService
   `POST /api/v1/wallets/checkout/direct` with that order ID and amount,
   preserving the authenticated buyer identity. Return the resulting
   `paymentUrl` to the frontend, which retains the order ID and redirects the
   buyer to Stripe. Agree the actual Commerce-facing route and response
   envelope with Commerce; none is specified or implemented here.
3. Stripe sends a signed webhook to WalletService. WalletService verifies it,
   retrieves provider state and reconciles the payment to its own ledger.
   The frontend polls the WalletService direct-payment status endpoint for
   display; it must not mark the Commerce order paid.
4. Commerce must query
   `GET /api/v1/wallets/checkout/direct/{orderId}` server-to-server using
   an agreed authorized identity. WalletService permits the payer or an
   `admin`/`staff` identity to read that payment. Verify the returned
   `orderId`, `amount`, currency and `status` against the stored order and
   expected buyer/payment context before applying a paid transition. The
   status response does not itself include the buyer ID. `transactionId`
   identifies the WalletService ledger transaction; `gatewayTransactionNo`
   can be null and is the Stripe PaymentIntent ID when available.
5. Only a verified matching `Success` should authorize an idempotent Commerce
   paid transition and subsequent fulfilment. Keep `Pending` pending; `Failed`
   or a conflicting/unknown result must not be interpreted as a paid order.
   Serialize competing payment and order-state changes, including cancellation,
   so late success, concurrent wallet payment, duplicate polls and retries
   cannot cause double fulfilment or silently discard a charge.
6. Implement durable, bounded retry/reconciliation independent of the browser,
   including recovery after service restarts and provider/WalletService
   outages. Persist the correlation and transition outcome. Escalate
   mismatches, late payments on cancelled orders, timeouts and unresolved
   attempts to an operator; do not assume failure or initiate a replacement
   payment while the earlier attempt may still succeed.

WalletService currently exposes ledger statuses `Pending`, `Success` and
`Failed` for the direct status endpoint; a provider-expired direct attempt is
represented by a failed ledger status. Commerce must not infer that a new
attempt is safe solely from a browser cancel or timeout. Settlement and
Commerce order transitions are not a distributed transaction; the Commerce
recovery mechanism and ownership of manual reconciliation must be agreed.

### Wallet-balance order payments

For payment from an existing wallet balance, the WalletService endpoint called
by Commerce is `POST /api/v1/wallets/orders/{orderId}/payment` with the buyer
identity and body `{"amount":100000}`. It debits the buyer's wallet and
returns a wallet transaction; the handler checks balance, serializes the order
payment against competing Stripe attempts and reuses an equivalent existing
wallet debit. A different or unresolved payment can return
`409 PAYMENT_ORDER_CONFLICT`. This endpoint does not update the Commerce
order: Commerce must apply its own idempotent order transition and recover
after a response timeout without assuming the debit failed. The endpoint is
hidden from Swagger, and its identity/network boundary and Commerce-side
integration must be verified before use.

This wallet-balance debit is a separate payment path from direct Stripe
Checkout. Do not call it to confirm a successful direct Stripe payment: that
would attempt a second payment. Refunds, disputes and reversals require the
separately approved operational policy and are not implemented by the
proposed Commerce reconciliation flow above.

### Cross-service acceptance tests

Run against a disposable test order, Stripe test mode and isolated test
wallets; capture order ID and WalletService transaction ID, but never log
secrets or signed webhook bodies.

- Verify Commerce rejects another buyer's order, altered amount/currency,
  non-payable order and attempts to bypass authoritative validation.
- Complete a direct Stripe checkout and verify webhook delivery, one
  WalletService success, one matching Commerce paid transition and one
  fulfilment. Refresh/replay the webhook and reconciliation; nothing repeats.
- Exercise delayed, failed and expired payments, browser cancellation and a
  closed browser. Commerce must not mark the order paid without a verified
  matching WalletService success, and reconciliation must continue without the
  browser.
- Simulate WalletService/Commerce outages, a lost response after commit,
  mismatched amount, concurrent wallet/direct attempts and a late success
  after cancellation. Verify idempotent recovery or explicit operator
  escalation, with no silent double charge or fulfilment.

## Gateway

- The Render WalletService webhook target is
  `https://walletservice-r8fp.onrender.com/api/v1/wallets/stripe/webhooks`.
  This is a Stripe-to-service webhook, not a browser return page or an
  authenticated frontend API.
- Route the authenticated payment initiation and status requests to
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
- [ ] Commerce validates authoritative order terms and buyer identity before
      direct checkout; the bypass path is addressed.
- [ ] Commerce reconciles WalletService settlement independently of the
      browser and applies order/fulfilment transitions once.
- [ ] Cross-service retries, outages, payment conflicts and late-success
      recovery pass the acceptance tests above.

Live operation remains blocked pending Stripe wallet-funding eligibility,
account/payment-method confirmation and an approved refunds/disputes policy.
