# WalletService Payments API (implemented)

> **Service:** `WalletService`  
> **Base URL:** `http://localhost:8008`
> **Protocol:** REST over HTTP/JSON
> **Status:** Routes below are implemented by this repository. Gateway routing and end-to-end service integration are not verified here.

This contract reflects `Wallet.Api/Controllers/Public/WalletsController.cs` and `AdminWalletsController.cs`. It documents only behavior implemented in this repository.

**Initial release boundary:** Outside Development, new withdrawal requests,
auction `ORDER_PAYMENT` capture and legacy Commerce settlement/order-refund/
sub-order-refund are disabled by default. Requests receive `503
FEATURE_DISABLED` after applicable authorization; invalid gate configuration
returns `500 INTERNAL_CONFIGURATION_ERROR`. The boolean settings
`ReleaseFeatures:WithdrawalsEnabled`, `ReleaseFeatures:AuctionOrderCaptureEnabled`
and `ReleaseFeatures:LegacyCommerceCreditsEnabled` can explicitly enable or
disable each group. Existing withdrawal resolution, hold release, forfeiture
and captured-deposit refund are not disabled by these gates. Enabling legacy
Commerce credits is unsafe without verified caller authorization and a
funding-backed settlement/refund contract.

## Important deployment note

The API host currently does not configure JWT authentication. `UserContextMiddleware` and controller extensions resolve user identity from claims or the forwarded `x-user-id` header, and admin/staff/seller roles from claims or `x-user-roles`. While legacy `InternalServiceAuthMiddleware` remains disabled, all `~/api/v1/internal/wallets/...` routes and the four legacy Commerce order routes (`orders/{id}/payment`, `sub-orders/{id}/settle`, `orders/{id}/refund`, `sub-orders/{id}/refund`) require `[RequireInternalServiceKey]` (`X-Internal-Service-Key`), and Auction hold routes require `[RequireAuctionServiceKey]`. Do not expose WalletService directly to untrusted clients: the trusted gateway must authenticate requests, overwrite forwarded identity headers, block `/api/v1/internal/*` from public ingress, and protect port `8008`. See also [Cross-service handover](../integrations/handover-response.md).

For authenticated user routes, include:

```http
x-user-id: 11111111-1111-1111-1111-111111111111
```

Admin routes require an authorized `x-user-roles` value (`admin` or `staff`, with high-privilege actions restricted to `admin` only as noted below). The API trusts forwarded headers; these examples do not imply that the service validates the caller's bearer token itself. Stripe webhooks are anonymous and require a verified `Stripe-Signature`.

## Route summary

| Method | Route | Access | Result |
|---|---|---|---|
| `GET` | `/api/v1/wallets/me` | User identity required | Current wallet; creates a default wallet if absent |
| `POST` | `/api/v1/wallets/topups/stripe` | User identity required | Creates pending Stripe top-up (`201`) |
| `GET` | `/api/v1/wallets/topups` | Top-up owner | Paginated top-up history (optional `status`) |
| `GET` | `/api/v1/wallets/topups/{id}` | Top-up owner | Reads top-up status (`200`) |
| `POST` | `/api/v1/wallets/topups/{id}/resume` | Top-up owner | Resumes the existing open unpaid Stripe session |
| `POST` | `/api/v1/wallets/holds` | Auction service key | Reserves available wallet balance (`201`/`200`) |
| `POST` | `/api/v1/wallets/holds/{holdId}/release` | Auction service key | Releases a hold |
| `POST` | `/api/v1/wallets/holds/{holdId}/capture` | Auction service key (`AuctionOrderCapture` gate) | Captures a hold to recipients or deposit escrow |
| `POST` | `/api/v1/wallets/holds/{holdId}/refund-captured` | Auction service key | Refunds a captured order deposit |
| `GET` | `/api/v1/wallets/holds/{holdId}` | Auction service key | Reads a hold by ID |
| `GET` | `/api/v1/wallets/holds/by-reference/{referenceType}/{referenceId}` | Auction service key | Reads a hold by reference |
| `GET` | `/api/v1/wallets/me/holds` | User identity required | Paginated owned holds (optional `status`) |
| `POST` | `/api/v1/wallets/transfers` | User identity required | Transfers between wallets (`200`; `WALLET_FROZEN` guarded) |
| `GET` | `/api/v1/wallets/transactions` | User identity required | Paginated transaction history (`200`) |
| `GET` | `/api/v1/wallets/transactions/{id}` | Transaction owner | Single wallet transaction details (`200`) |
| `POST` | `/api/v1/wallets/withdrawals` | `seller` (`admin`/`staff` also permitted by `[RequireRole]`); `Withdrawals` gate | Idempotent (`Idempotency-Key`) pending withdrawal reservation (`201` create / `200` replay) |
| `GET` | `/api/v1/wallets/withdrawals` | `seller` (and `admin`/`staff`) | Paginated owned withdrawal requests (optional `status`) |
| `GET` | `/api/v1/wallets/withdrawals/{id}` | `seller` (and `admin`/`staff`) | Owned withdrawal request detail (`200`) |
| `POST` | `/api/v1/wallets/withdrawals/{id}/cancel` | `seller` (and `admin`/`staff`) | Cancels an owned `Pending` withdrawal and releases funds (`200`) |
| `GET` | `/api/v1/wallets/bank-accounts` | `seller` (and `admin`/`staff`) | Lists owned saved bank accounts (`200`) |
| `POST` | `/api/v1/wallets/bank-accounts` | `seller` (and `admin`/`staff`) | Creates a saved bank account (`201`) |
| `PATCH` | `/api/v1/wallets/bank-accounts/{id}/default` | `seller` (and `admin`/`staff`) | Marks a saved bank account as default (`200`) |
| `DELETE` | `/api/v1/wallets/bank-accounts/{id}` | `seller` (and `admin`/`staff`) | Soft-deletes a saved bank account (`204`) |
| `POST` | `/api/v1/wallets/stripe/webhooks` | Anonymous, signature required | Processes Stripe webhooks (`200` when acknowledged) |
| `POST` | `/api/v1/wallets/checkout/direct` | User identity required | Starts direct checkout through Stripe (`200`) |
| `POST` | `/api/v1/wallets/checkout/direct/{orderId}/resume` | Order owner | Resumes existing open Stripe Checkout session (`200`) |
| `POST` | `/api/v1/wallets/checkout/direct/{orderId}/cancel` | Order owner | Cancels an unpaid pending Stripe Checkout session (`200`) |
| `GET` | `/api/v1/wallets/checkout/direct/{orderId}` | Order owner or `admin`/`staff` | Reads direct payment status |
| `POST` | `/api/v1/wallets/orders/{orderId}/payment` | `X-Internal-Service-Key` + forwarded buyer identity (`IgnoreApi = true`) | Legacy route: debits buyer `AvailableBalance` (`WALLET_FROZEN` guarded) and records escrow (`200`) |
| `POST` | `/api/v1/wallets/sub-orders/{subOrderId}/settle` | `X-Internal-Service-Key` + `LegacyCommerceCredits` gate (`IgnoreApi = true`) | Legacy route: credits seller from matching wallet-payment escrow (`200`) |
| `POST` | `/api/v1/wallets/orders/{orderId}/refund` | `X-Internal-Service-Key` + `LegacyCommerceCredits` gate (`IgnoreApi = true`) | Legacy route: refunds remaining wallet-payment escrow (`200`) |
| `POST` | `/api/v1/wallets/sub-orders/{subOrderId}/refund` | `X-Internal-Service-Key` + `LegacyCommerceCredits` gate (`IgnoreApi = true`) | Legacy route: child refund without original `PurchaseDebit` lookup (`200`) |
| `GET` | `/api/v1/admin/wallets` | `admin` or `staff` | Paginated wallet list (`status`, `userId`, `search`) |
| `GET` | `/api/v1/admin/wallets/{userId}` | `admin` or `staff` | Inspects a user's wallet (`200`) |
| `POST` | `/api/v1/admin/wallets/{userId}/freeze` | `admin` or `staff` | Freezes a user's wallet (`200`) |
| `POST` | `/api/v1/admin/wallets/{userId}/unfreeze` | `admin` or `staff` | Unfreezes a user's wallet (`200`) |
| `GET` | `/api/v1/admin/wallets/withdrawals` | `admin` or `staff` | Paginated withdrawal queue (optional `status`) |
| `GET` | `/api/v1/admin/wallets/withdrawals/{id}/history` | `admin` or `staff` | Audit history of a withdrawal request (`200`) |
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` | `admin` or `staff` (`ResolveFailed` requires `admin` only) | Processes withdrawal workflow transitions |
| `POST` | `/api/v1/admin/wallets/{userId}/adjustments` | `admin` only (`403` for `staff`) | Manually adjusts wallet balance (`200`) |
| `GET` | `/api/v1/admin/wallets/ledger` | `admin` only | Reads a paginated, filtered transaction report |
| `GET` | `/api/v1/admin/wallets/reconciliation` | `admin` only | Compares wallet balances with latest successful transaction snapshots |
| `GET` | `/api/v1/admin/wallets/holds` | `admin` or `staff` | Paginated auction holds across wallets |
| `GET` | `/api/v1/admin/wallets/{userId}/holds` | `admin` or `staff` | Paginated auction holds for a user wallet |
| `GET` | `/api/v1/admin/wallets/stripe/reconciliation-required` | `admin` only | Lists Stripe payment attempts requiring manual reconciliation |
| `POST` | `/api/v1/admin/wallets/stripe/attempts/{attemptId}/resolve` | `admin` only | Manually resolves a `ReconciliationRequired` Stripe attempt to `Failed` or `Expired` |
| `POST` | `/api/v1/admin/wallets/stripe/reconcile-pending` | `admin` only | Triggers manual reconciliation of pending Stripe top-ups and direct payments |
| `GET` | `/health` | Anonymous | Unconditional liveness response (does not check database readiness) |

### Internal S2S routes (`~/api/v1/internal/wallets/*`, `IgnoreApi = true`)

Hosted on `WalletsController` and excluded from Swagger and public gateway ingress (all require `X-Internal-Service-Key` via `[RequireInternalServiceKey]`, or `[RequireAuctionServiceKey]` for `holds/*`):

- `GET ~/api/v1/internal/wallets/{userId}` — internal wallet balance/available/freeze lookup.
- `POST ~/api/v1/internal/wallets/freeze-purchase` and `POST ~/api/v1/internal/wallets/unfreeze-purchase` — purchase-dispute wallet freeze/unfreeze.
- `POST ~/api/v1/internal/wallets/orders/{orderId}/payment` — internal buyer wallet debit (`BuyerId`, `Amount` in body; does not rely on `x-user-id` header).
- `POST ~/api/v1/internal/wallets/orders/{orderId}/adjust-payment` — idempotent (`Idempotency-Key`) post-order delta debit or refund against escrow.
- `POST ~/api/v1/internal/wallets/orders/{orderId}/escrow/cancel` — idempotent (`Idempotency-Key`) strict all-or-nothing cancellation of an untouched wallet-payment `OrderEscrow`, returning the full balance to `OrderEscrow.WalletId` (`EscrowCancellationRefund`) and recording an `OrderEscrowCancel` audit entry (see [cancel order escrow](checkout/cancel-order-escrow.md)).
- `POST ~/api/v1/internal/wallets/checkout/direct/{orderId}/expire` — expires unpaid direct Stripe order payment on Commerce order expiry.
- `POST ~/api/v1/internal/wallets/sub-orders/{subOrderId}/settle`, `orders/{orderId}/refund`, `sub-orders/{subOrderId}/refund` (`ReleaseFeature.LegacyCommerceCredits` gated).
- `~/api/v1/internal/wallets/holds/*` — internal mirror routes for Auction `CreateHold`, `ReleaseHold`, `CaptureHold`, `RefundCapturedHold`, `GetHold`, and `GetHoldByReference`.

Full payload and header details for Commerce, Auction, and Notification integrations are in [Cross-service handover](../integrations/handover-response.md).

## Order payment escrow

The existing Commerce order routes remain unchanged. The payment route debits
the authenticated buyer's digital Wallet balance and records that amount as
wallet-payment escrow for the order ID. Settlement credits only the
caller-supplied amount covered by that remaining escrow; captured auction
deposits never fund the payout. If wallet-payment escrow is insufficient, the
operation returns `409 ORDER_ESCROW_INSUFFICIENT`.

An order may have one seller payout and one refund. A partial payout may be
followed by a refund of the unspent wallet-payment remainder. Each operation is
limited to the then-remaining escrow, exact retries return the original
transaction, and changed retries or overdraws do not move funds. Order refunds
leave captured-deposit escrow untouched, so the existing hold operation can
still refund the full captured deposit.

The `sub-orders/{subOrderId}` routes accept an optional `parentOrderId` field so
multiple sub-order settlements and sub-order refunds lock and decrement the
parent `OrderEscrow`. When `parentOrderId` is omitted and no matching payment
exists for `subOrderId`, the legacy compatibility fallback remains active;
these routes remain hidden from Swagger, and the existing release gates remain
unchanged.

## User wallet routes

### Get the current user's wallet

In Swagger, use the `x-user-id` header parameter to test the forwarded user
identity. The API still resolves the authenticated identity from claims or the
trusted gateway header; the header is not a way to select another user's
wallet.

```http
GET /api/v1/wallets/me
x-user-id: 11111111-1111-1111-1111-111111111111
```

If the user does not yet have a wallet, the service creates a VND wallet with zero balance.

```json
{
  "walletId": "wallet-id",
  "userId": "11111111-1111-1111-1111-111111111111",
  "balance": 0,
  "availableBalance": 0,
  "lockedBalance": 0,
  "heldBalance": 0,
  "currency": "Vnd",
  "status": "Active",
  "updatedAt": "2026-11-02T10:00:00+00:00"
}
```

### Create a Stripe top-up

```http
POST /api/v1/wallets/topups/stripe
x-user-id: 11111111-1111-1111-1111-111111111111
Idempotency-Key: topup-12345678
Content-Type: application/json

{
  "amount": 100000
}
```

`amount` must be whole VND between 10,000 and 99,999,999. Retain the same idempotency key and amount for retries.

**`201 Created`**

```json
{
  "transactionId": "transaction-id",
  "paymentUrl": "https://checkout.stripe.com/...",
  "amount": 100000,
  "currency": "Vnd",
  "status": "Pending",
  "createdAt": "2026-11-02T10:00:00+00:00"
}
```

This creates a pending top-up. A verified Stripe webhook or background
provider-state reconciliation can settle it. Poll the owned top-up status
route after redirect; see [top-up recovery](stripe/topup-recovery.md).

### Transfer funds

```http
POST /api/v1/wallets/transfers
x-user-id: 11111111-1111-1111-1111-111111111111
Content-Type: application/json

{
  "recipientUserId": "22222222-2222-2222-2222-222222222222",
  "amount": 25000,
  "note": "Transfer"
}
```

The amount must be between VND 1,000 and VND 100,000,000. The sender is taken from the authenticated/forwarded identity, not from the request body. The service rejects self-transfers, missing wallets, currency mismatches, and insufficient available balance. It records both sides of the transfer atomically.

**`200 OK`**

```json
{
  "transactionId": "transaction-id",
  "amount": 25000,
  "currency": "Vnd",
  "status": "Success",
  "createdAt": "2026-11-02T10:00:00+00:00"
}
```

### List transactions

```http
GET /api/v1/wallets/transactions?page=1&pageSize=10&type=DEPOSIT&fromDate=2026-11-01T00:00:00Z&toDate=2026-11-02T23:59:59Z
x-user-id: 11111111-1111-1111-1111-111111111111
```

All query parameters are optional. `page` defaults to `1`; `pageSize` defaults to `10`. `page` must be at least `1`, and `pageSize` must be between `1` and `100`; out-of-range values return `400 VAL_INVALID_INPUT`. `fromDate` and `toDate` are optional ISO-8601 timestamps, and `fromDate` must not be later than `toDate`. `type` accepts `ALL`, `DEPOSIT`, `WITHDRAWAL`, `PAYMENT`, `REFUND`, or a defined `WalletTransactionType` name. `DEPOSIT` includes historical `TopupVnpay` and `TopupStripe` transactions; `REFUND` includes `AuctionDepositRefund`. An invalid type returns a validation error.

**`200 OK`**

```json
{
  "items": [
    {
      "id": "transaction-id",
      "walletId": "wallet-id",
      "userId": "11111111-1111-1111-1111-111111111111",
      "type": "TopupVnpay",
      "amount": 100000,
      "currency": "Vnd",
      "balanceAfter": 100000,
      "status": "Success",
      "note": "VNPay topup TOPUP-reference",
      "createdAt": "2026-11-02T10:00:00+00:00"
    }
  ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 10,
    "totalCount": 1,
    "totalPages": 1,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

### Request a withdrawal

```http
POST /api/v1/wallets/withdrawals
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: seller
Idempotency-Key: withdraw-2026-11-19-001
Content-Type: application/json

{
  "amount": 100000,
  "currency": "VND",
  "bankCode": "BANK",
  "accountNumber": "123456789",
  "accountHolderName": "NGUYEN VAN A"
}
```

`amount` must be between VND 50,000 and VND 500,000,000 and fit the persisted `decimal(18,2)` precision. `currency` defaults to `VND` and no other currency is accepted. The `Idempotency-Key` header is required (`1` to `100` ASCII letters, digits, hyphens, or underscores). Callers may provide an owned `bankAccountId`, omit both `bankAccountId` and manual bank fields to fall back to their default saved bank account, or pass all three manual fields (`bankCode`, `accountNumber`, `accountHolderName`, max lengths 150, 100, and 255). The request reserves the amount from the wallet's available balance; it does not debit the wallet balance or initiate a bank payout. The current fee is `0`.

**`201 Created`** (new request; an identical `Idempotency-Key` replay with matching fingerprint returns **`200 OK`**)

```json
{
  "withdrawalId": "withdrawal-id",
  "amount": 100000,
  "fee": 0,
  "status": "Pending",
  "requestedAt": "2026-11-03T08:00:00+00:00"
}
```

The route requires the `seller` role; the shared role filter also permits `admin` and `staff`. This service does not independently verify seller verification status or execute external bank transfers. Owners can list/read their withdrawals (`GET /api/v1/wallets/withdrawals`, `GET /api/v1/wallets/withdrawals/{id}`) and cancel `Pending` requests (`POST /api/v1/wallets/withdrawals/{id}/cancel`), or manage saved bank accounts under `/api/v1/wallets/bank-accounts`.

The route returns `400 INSUFFICIENT_BALANCE` when available funds are insufficient, `400 VAL_INVALID_INPUT` for invalid request fields or missing `Idempotency-Key`, `401 UNAUTHORIZED` when user identity is missing, `403 FORBIDDEN` when the caller lacks an allowed role, `403 WALLET_FROZEN` when the wallet is frozen, `404 WALLET_NOT_FOUND` (or missing bank account), and `409` when `Idempotency-Key` is reused with a mismatched request payload.

## Stripe webhook and direct checkout

`POST /api/v1/wallets/stripe/webhooks` accepts the unmodified raw body and
`Stripe-Signature`. A committed, duplicate or irrelevant event returns an empty
`200`; invalid signatures return `400` and retryable failures return `5xx`.
See [Stripe payment contracts](stripe/payments.md).

```http
POST /api/v1/wallets/checkout/direct
x-user-id: 11111111-1111-1111-1111-111111111111
Content-Type: application/json

{
  "orderId": "33333333-3333-3333-3333-333333333333",
  "amount": 100000
}
```

`orderId` must be a non-empty UUID. The wallet must use VND and the amount must be a whole VND value from 1 to 99,999,999. The service creates a pending direct payment and returns a Stripe Checkout URL:

```json
{
  "transactionId": "transaction-id",
  "orderId": "33333333-3333-3333-3333-333333333333",
  "amount": 100000,
  "paymentUrl": "https://checkout.stripe.com/...",
  "status": "Pending",
  "createdAt": "2026-11-02T10:00:00+00:00"
}
```

This endpoint does not debit the wallet balance. A matching retry reuses an active session; changed inputs or an unresolved attempt return `409 PAYMENT_CONFLICT`. A replacement requires provider-confirmed finality.

## Admin routes

The controller requires the forwarded role `admin` or `staff`, in addition to a valid user identity.

### Process an existing withdrawal

```http
PATCH /api/v1/admin/wallets/withdrawals/{withdrawalId}
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: admin
Content-Type: application/json

{
  "action": "Complete",
  "adminNote": "Verified manual bank transfer",
  "transferConfirmed": true,
  "transferReference": "BANK-TRANSFER-2026-001"
}
```

`action` supports the 6-step withdrawal workflow: `Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, and `ResolveFailed` (`ResolveFailed` requires `admin` only and returns `403` for `staff`).
- `Approve` (`Pending -> Approved`): consumes locked funds, records `WithdrawalDebit`, and optionally saves a unique `transferReference`.
- `Reject` (`Pending -> Rejected`): releases locked funds back to available balance.
- `StartProcessing` (`Approved -> Processing`): marks an approved withdrawal as processing while external bank transfer handling occurs.
- `Complete` (`Approved` or `Processing -> Completed`): requires `transferConfirmed: true` and a unique non-blank `transferReference` (max 100 characters) confirming the external bank transfer; does not initiate an automated bank payout.
- `Fail` (`Approved -> Failed`): requires `adminNote` (3–500 characters) and credits a `WithdrawalReversal` back to `wallet.Balance`.
- `ResolveFailed` (`Processing -> Failed`): requires `admin` role (`403` for `staff`), `bankVerifiedNotTransferred: true`, `verificationEvidence` (3–500 characters), and `adminNote` (3–500 characters) to reverse a stuck processing withdrawal (`WithdrawalReversal`) after verifying funds were not sent externally.

State transitions dispatch best-effort post-commit `withdrawal-events` REST notifications via `INotificationClient`; dispatch failures are logged in audit logs (`NotificationDispatchFailed`) without rolling back the withdrawal transition. Withdrawal transaction notes mask bank account numbers.

### Read the administrator ledger

```http
GET /api/v1/admin/wallets/ledger?page=1&pageSize=10&type=WithdrawalDebit&status=Success&fromDate=2026-11-01T00:00:00Z&toDate=2026-11-12T23:59:59Z
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: admin
```

Optional filters are `walletId`, `type`, `status`, `fromDate`, and `toDate`. Pages default to 1 and page size to 10 (maximum 100). Date bounds are inclusive ISO 8601 instants with an explicit offset and are normalized to UTC. The response is a bare `PagedData<AdminWalletLedgerEntry>` with transaction ID, wallet ID, type, amount, currency, balance before/after, status, reference ID, and timestamp. Notes and bank data are excluded. Rows are ordered by creation time descending, then transaction ID descending.

### Reconcile wallet balances

```http
GET /api/v1/admin/wallets/reconciliation?page=1&pageSize=10
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: admin
```

An optional `walletId` narrows the report. The response is a bare `PagedData<WalletReconciliationResult>` containing current balance, latest successful transaction balance and timestamp, successful transaction count, difference, currency, and a `Balanced`, `Discrepancy`, or `InsufficientHistory` result. A wallet without a successful transaction snapshot is always `InsufficientHistory`, including a zero balance. This is a stored-balance snapshot check, not double-entry or external bank reconciliation.

### Adjust a wallet balance

```http
POST /api/v1/admin/wallets/{userId}/adjustments
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: admin
Content-Type: application/json

{
  "amount": 50000,
  "adjustmentType": "Credit",
  "reason": "Support case"
}
```

`userId` is the wallet owner's user ID, not the WalletService wallet ID. `adjustmentType` must be `Credit` or `Debit`; `amount` must be positive and `reason` is required. The result includes `adjustmentId`, `walletId`, `amount`, `balanceAfter`, and `createdAt`. The operation records an audit log.

## Errors and operational notes

Validation and business-rule errors use `application/problem+json`. Invalid request and model-binding responses contain `type`, `title`, `status`, `errorCode`, `detail`, `errors`, `traceId`, and `timestamp`; `errorCode` is `VAL_INVALID_INPUT`, and `errors` maps field names to arrays of messages.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errorCode": "VAL_INVALID_INPUT",
  "detail": "Amount must be greater than zero.",
  "errors": {
    "Amount": ["Amount must be greater than zero."]
  },
  "traceId": "request-trace-id",
  "timestamp": "2026-11-03T08:00:00Z"
}
```

Common status codes are `400` for invalid input or business rules, `401` when user identity or `X-Internal-Service-Key` is missing/invalid, `403` for missing `admin`/`staff` role (`admin`-only on balance adjustments, `ResolveFailed`, ledger, reconciliation, and Stripe resolution) or `WALLET_FROZEN`, and `404` for missing wallets/resources. Exact business error codes and details depend on the failing operation.

The service attempts EF Core migrations at startup (`Program.cs`) and logs warnings rather than failing startup. Review database state, test migration `20261119020448_HandoverWalletHardening`, and create backups before connecting it to a shared database. `StripeSettings.EnsureValid` checks test-mode Stripe credentials and HTTPS return URLs (`localhost:3000` allowed in Development) at startup; gateway, live-mode Stripe, NotificationService, Commerce, and Auction staging acceptance remain to be verified. See [Cross-service handover](../integrations/handover-response.md) and [Production readiness](../operations/production-readiness.md).
