# WalletService Payments API (implemented)

> **Service:** `WalletService`  
> **Base URL:** `http://localhost:8008`
> **Protocol:** REST over HTTP/JSON
> **Status:** Routes below are implemented by this repository. Gateway routing and end-to-end service integration are not verified here.

This contract reflects `Wallet.Api/Controllers/Public/WalletsController.cs` and `AdminWalletsController.cs`. It documents only behavior implemented in this repository.

## Important deployment note

The API host currently does not configure JWT authentication. Controllers resolve user identity from claims or the forwarded `x-user-id` header, and admin roles from claims or `x-user-roles`. The internal-service authentication middleware is disabled. Do not expose these routes directly to untrusted clients: the trusted gateway must authenticate requests, overwrite forwarded identity headers, and protect the service network.

For authenticated user routes, include:

```http
x-user-id: 11111111-1111-1111-1111-111111111111
```

Admin routes also require an authorized `x-user-roles` value (`admin` or `staff`). The API accepts forwarded headers; these examples do not imply that the service validates the caller's bearer token itself. Stripe webhooks are anonymous and require a verified signature.

## Route summary

| Method | Route | Access | Result |
|---|---|---|---|
| `GET` | `/api/v1/wallets/me` | User identity required | Current wallet; creates a default wallet if absent |
| `POST` | `/api/v1/wallets/topups/stripe` | User identity required | Creates pending Stripe top-up (`201`) |
| `GET` | `/api/v1/wallets/topups/{id}` | Top-up owner | Reads top-up status (`200`) |
| `POST` | `/api/v1/wallets/transfers` | User identity required | Transfers between wallets (`200`) |
| `GET` | `/api/v1/wallets/transactions` | User identity required | Paginated transaction history (`200`) |
| `POST` | `/api/v1/wallets/withdrawals` | Seller role and user identity required; shared filter also permits admin/staff | Creates a pending withdrawal request and reserves available funds (`201`) |
| `POST` | `/api/v1/wallets/stripe/webhooks` | Anonymous, signature required | Processes Stripe webhooks (`200` when acknowledged) |
| `POST` | `/api/v1/wallets/checkout/direct` | User identity required | Starts direct checkout through Stripe (`200`) |
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` | `admin` or `staff` | Processes an existing pending withdrawal |
| `POST` | `/api/v1/admin/wallets/{userId}/adjustments` | `admin` or `staff` | Adjusts wallet balance by owner user ID |
| `GET` | `/health` | Anonymous | Service health response |

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
  "currency": "Vnd",
  "status": "ACTIVE",
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

This creates a pending top-up. The balance is credited only after a verified Stripe webhook and provider-state reconciliation. Poll the owned top-up status route after redirect.

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

All query parameters are optional. `page` defaults to `1`; `pageSize` defaults to `10`. `page` must be at least `1`, and `pageSize` must be between `1` and `100`; out-of-range values return `400 VAL_INVALID_INPUT`. `fromDate` and `toDate` are optional ISO-8601 timestamps, and `fromDate` must not be later than `toDate`. `type` accepts `ALL`, `DEPOSIT`, `WITHDRAWAL`, `PAYMENT`, `REFUND`, or a defined `WalletTransactionType` name. `DEPOSIT` includes historical `TopupVnpay` and `TopupStripe` transactions. An invalid type returns a validation error.

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
Content-Type: application/json

{
  "amount": 100000,
  "currency": "VND",
  "bankCode": "BANK",
  "accountNumber": "123456789",
  "accountHolderName": "NGUYEN VAN A"
}
```

`amount` must be between VND 50,000 and VND 500,000,000 and fit the persisted `decimal(18,2)` precision. `currency` defaults to `VND` and no other currency is accepted. `bankCode`, `accountNumber`, and `accountHolderName` are required, with maximum lengths of 150, 100, and 255 characters respectively. The request reserves the amount from the wallet's available balance; it does not debit the wallet balance or initiate a bank payout. The current fee is `0`.

**`201 Created`**

```json
{
  "withdrawalId": "withdrawal-id",
  "amount": 100000,
  "fee": 0,
  "status": "Pending",
  "requestedAt": "2026-11-03T08:00:00+00:00"
}
```

The route requires the `seller` role; the shared role filter also permits `admin` and `staff`. This service does not independently verify seller verification status. A bank payout, payout failure/retry handling, and reconciliation are not implemented.

The route returns `400 INSUFFICIENT_BALANCE` when available funds are insufficient, `400 VAL_INVALID_INPUT` for invalid request fields, `401 UNAUTHORIZED` when user identity is missing, `403 FORBIDDEN` when the caller lacks an allowed role, and `404 WALLET_NOT_FOUND` when the user has no wallet.

The route has no idempotency key. A repeated request can create another pending withdrawal and reserve additional funds; clients should not blindly retry when the result is unknown.

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
  "action": "Approve",
  "adminNote": "Reviewed"
}
```

`action` must be `Approve` or `Reject`. The operation requires an existing pending withdrawal request; approval debits the previously locked funds and rejection releases them. This records the staff decision but does not initiate or confirm an external bank transfer.

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

Common status codes are `400` for invalid input or business rules, `401` when user identity is missing, `403` for missing admin/staff role, and `404` for missing wallets/resources. Exact business error codes and details depend on the failing operation.

The service applies EF Core migrations at startup. Review database state and backups before connecting it to a shared database. Stripe test credentials and HTTPS return URLs must be configured; gateway and real Stripe integration remain to be verified.
