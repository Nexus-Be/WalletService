# WalletService — Stripe payment contracts

> **Status:** Implemented for test mode; real Stripe and gateway integration
> have not yet been verified.
> **Date:** 2026-11-12

## 1. Routing and authorization

| Method and route | Access | Use case |
|---|---|---|
| `POST /api/v1/wallets/topups/stripe` | Authenticated wallet owner | Create/reuse Stripe Checkout top-up |
| `GET /api/v1/wallets/topups/{id}` | Authenticated top-up owner | Read top-up status |
| `POST /api/v1/wallets/checkout/direct` | Authenticated wallet owner | Create/reuse direct order checkout |
| `GET /api/v1/wallets/checkout/direct/{orderId}` | Existing owner/privileged rules | Read direct order payment status |
| `POST /api/v1/wallets/stripe/webhooks` | Anonymous, verified Stripe signature | Process provider events |

All actions stay in `WalletsController` and dispatch MediatR requests. The
webhook receives the unmodified raw body and `Stripe-Signature` header.
The VNPay initiation, return and IPN routes have been removed.

## 2. Input and validation

Top-up body: `{ "amount": 100000 }`, with `Idempotency-Key` header. The
amount must be whole VND, between 10,000 and 99,999,999 VND, and within
account/payment-method limits. Direct checkout
body: `{ "orderId": "<UUID>", "amount": 100000 }`; its amount must be positive
whole VND, at most 99,999,999 VND, and within account/payment-method limits.
Stripe documents an eight-digit PaymentIntent amount limit; VND is
zero-decimal. The previous VNPay top-up maximum of 100,000,000 VND therefore
cannot be used for a single Stripe payment.
See [Stripe PaymentIntent creation](https://docs.stripe.com/api/payment_intents/create).
The server chooses success and cancellation URLs. Identity is server-populated
and cannot be selected through the request body.

## 3. Response contracts

Top-up initiation returns HTTP `201` and a pure DTO containing
`transactionId`, `paymentUrl`, `amount`, `currency`, `status` and `createdAt`.
`transactionId` identifies the top-up and is used as `{id}` for its status
lookup. The status DTO contains `transactionId`, `amount`, `currency`,
`status`, `createdAt` and `updatedAt`.

Direct checkout returns HTTP `200` with existing fields `transactionId`,
`orderId`, `amount`, `paymentUrl`, `status` and `createdAt`; `transactionId`
is the existing direct-payment ledger transaction ID. Direct status returns
`transactionId`, `orderId`, `amount`, `currency`, `status`,
`gatewayTransactionNo`, `createdAt` and `updatedAt`; the nullable gateway
number contains the Stripe PaymentIntent ID when bound. Timestamps are UTC
ISO 8601 strings and status is a PascalCase string.

The top-up idempotency key is required, 1–128 ASCII letters, digits,
hyphens or underscores, and scoped to the authenticated owner and endpoint;
bind it to a canonical amount and currency.
An identical retry after session creation returns `201` with the original
response and without creating another session. A changed input under the same
key returns `409 PAYMENT_CONFLICT`. A concurrent or uncertain creation must
not return a fabricated URL: return `409 PAYMENT_CONFLICT` while reconciliation
is pending, or `503 PAYMENT_PROVIDER_UNAVAILABLE` for a provider outage. A
retry after settlement or expiration returns `409 PAYMENT_CONFLICT`; the client
uses the owned status endpoint and a fresh key for a new top-up. No special
replay response header is required.

For direct checkout, the order ID and immutable owner/amount fingerprint
identify the business operation. A matching request returns `200` and the
same active session; changed inputs or an unresolved earlier attempt return
`409 PAYMENT_CONFLICT`. A replacement is allowed only after Stripe confirms
the prior attempt can no longer succeed. No request may create another charge
merely because a session URL expired or a network call timed out.

The webhook returns an empty `200` for valid committed, duplicate or irrelevant
events, `400` for an invalid signature, and `5xx` for a retryable processing
failure. Gateway webhook responses must not be wrapped.

## 4. Error matrix and enums

| HTTP | Code | Condition |
|---|---|---|
| 400 | `VAL_INVALID_INPUT` | Invalid amount, identifier or required field |
| 400 | `VAL_INVALID_SIGNATURE` | Invalid or missing Stripe signature |
| 401 | `UNAUTHORIZED` | Missing authenticated identity |
| 404 | `WALLET_NOT_FOUND` | Wallet does not exist |
| 404 | `PAYMENT_NOT_FOUND` | Missing or non-owned payment |
| 409 | `PAYMENT_CONFLICT` | Idempotency mismatch or unresolved previous attempt |
| 413 | `WEBHOOK_PAYLOAD_TOO_LARGE` | Webhook body exceeds 1 MiB |
| 503 | `PAYMENT_PROVIDER_UNAVAILABLE` | Provider request cannot be completed |

The implemented top-up status route uses a GUID route constraint: a malformed
GUID does not match the route and returns `404`; an empty GUID reaches
validation and returns `400 VAL_INVALID_INPUT`.

Error messages must use English sentence case. Missing or malformed idempotency
keys return `400 VAL_INVALID_INPUT`; invalid webhook signatures return `400`
without disclosing signature details. Persistent verified-event integrity
mismatches are durably recorded and alerted without financial effects, then
acknowledged with `200`; if the record cannot be committed, return `5xx`.
Transient database/provider failures return `5xx` so delivery is retried.
Existing `TopupStatus` values are
`Pending=0`, `Success=1`, `Failed=2`, `Expired=3`; API strings are PascalCase.
New attempt and ledger enum values must not reinterpret existing integers.
The existing ledger value `TopupVnpay=0` remains historical; the distinct
Stripe ledger value is `TopupStripe=13`. The transaction-history `DEPOSIT`
filter includes both.

## 5. Execution and verification

1. Authenticate and persist a pending attempt with stable idempotency key.
2. Create the Stripe session outside database locks and persist correlation.
3. Verify signed webhooks; validate amount, currency, mode and references.
4. Lock the attempt and relevant wallet/order; apply success once and write
   event, transaction and audit state atomically.
5. Test delayed payment, replay, out-of-order events, ambiguous creation,
   owner isolation, retries and failure recovery before enabling cutover.

See the feature plan and contracts in `specs/003-stripe-migration/`. Live
launch is blocked pending Stripe wallet-funding eligibility and an approved
refund/dispute policy.
