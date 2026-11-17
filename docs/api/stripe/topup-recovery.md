# Stripe top-up history, resume and reconciliation

**Status:** Implemented and covered by automated tests; live Stripe and
gateway acceptance remains pending.

## Contract

All buyer routes use the existing trusted user identity and return pure DTOs.
Amounts are whole VND and top-up statuses are `Pending` (0), `Success` (1),
`Failed` (2) and `Expired` (3). Existing ProblemDetails errors remain in use.

| Route | Request | Response |
|---|---|---|
| `GET /api/v1/wallets/topups` | `page` (default 1), `pageSize` (default 10, maximum 100) | `200` with owned, newest-first `PagedData` entries containing `transactionId`, `amount`, `currency`, `status`, `createdAt`, `updatedAt`, `canResume`, `sessionExpiresAt`. |
| `GET /api/v1/wallets/topups/{id}` | Owned top-up ID | Existing `200` DTO with additive `canResume`, `sessionExpiresAt`; missing or non-owned is `404 PAYMENT_NOT_FOUND`. |
| `POST /api/v1/wallets/topups/{id}/resume` | Owned top-up ID | `200` with `transactionId`, `paymentUrl`, `sessionExpiresAt` only if provider confirms the same session is open and unpaid; no new session or payment. |

Malformed input returns `400 VAL_INVALID_INPUT`; missing identity returns `401
UNAUTHORIZED`; unavailable or non-resumable sessions return `409
PAYMENT_CONFLICT`; provider failure returns `503
PAYMENT_PROVIDER_UNAVAILABLE`. Lists are scoped to the current wallet owner.
`canResume` is an advisory local snapshot; resume checks the provider.

## Processing and compatibility

Top-up success and cancel URLs carry `kind=topup` and the top-up
`transactionId`; any configured parameters with those names are replaced.
Direct-order return URLs remain unchanged. Redirects do not
settle funds. Verified Stripe webhooks and periodic reconciliation use the
same transactional settlement logic, check provider identity, amount,
currency and references, and never credit twice. Ambiguous session creation
without a stored session ID remains an explicit operational reconciliation
case; the worker does not blindly create a replacement.
On a reconciliation failure, the worker logs the error and retries on a later
cycle with a fresh context. Attempts are rotated by last check time so one
persistently failing attempt does not indefinitely block other candidates.

Production return URLs must use HTTPS. Development may use
`http://localhost:3000`. Test-mode Stripe and gateway webhook forwarding
require end-to-end verification.
