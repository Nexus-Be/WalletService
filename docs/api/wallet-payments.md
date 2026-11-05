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

Admin routes also require an authorized `x-user-roles` value (`admin` or `staff`). The API accepts forwarded headers; these examples do not imply that the service validates the caller's bearer token itself. VNPay callbacks are intentionally callable without user identity.

## Route summary

| Method | Route | Access | Result |
|---|---|---|---|
| `GET` | `/api/v1/wallets/me` | User identity required | Current wallet; creates a default wallet if absent |
| `POST` | `/api/v1/wallets/topups/vnpay` | User identity required | Creates pending VNPay top-up (`201`) |
| `POST` | `/api/v1/wallets/transfers` | User identity required | Transfers between wallets (`200`) |
| `GET` | `/api/v1/wallets/transactions` | User identity required | Paginated transaction history (`200`) |
| `POST` | `/api/v1/wallets/withdrawals` | Seller role and user identity required; shared filter also permits admin/staff | Creates a pending withdrawal request and reserves available funds (`201`) |
| `POST` | `/api/v1/wallets/vnpay/ipn` | Anonymous callback | Processes VNPay IPN |
| `GET` | `/api/v1/wallets/vnpay/return` | Anonymous callback | Reports VNPay browser-return result |
| `POST` | `/api/v1/wallets/checkout/direct` | `x-user-id` required by handler | Starts direct checkout through VNPay (`200`) |
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` | `admin` or `staff` | Processes an existing pending withdrawal |
| `POST` | `/api/v1/admin/wallets/{walletId}/adjustments` | `admin` or `staff` | Adjusts wallet balance |
| `GET` | `/health` | Anonymous | Service health response |

## User wallet routes

### Get the current user's wallet

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

### Create a VNPay top-up

```http
POST /api/v1/wallets/topups/vnpay
x-user-id: 11111111-1111-1111-1111-111111111111
Content-Type: application/json

{
  "amount": 100000,
  "currency": "VND",
  "paymentMethod": "VNPAY",
  "bankCode": null,
  "returnUrl": "https://shop.example/checkout/return"
}
```

`amount` must be between VND 10,000 and VND 100,000,000 and fit the persisted `decimal(18,2)` precision. Currency and payment method default to `VND` and `VNPAY`; when provided, only `VND` and `VNPAY` are accepted. The bank code and return URL are optional.

**`201 Created`**

```json
{
  "transactionId": "transaction-id",
  "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
  "amount": 100000,
  "currency": "Vnd",
  "status": "Pending",
  "createdAt": "2026-11-02T10:00:00+00:00"
}
```

This creates a pending top-up. The balance is credited only after the service validates a successful VNPay IPN.

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

All query parameters are optional. `page` defaults to `1`; `pageSize` defaults to `10`. `page` must be at least `1`, and `pageSize` must be between `1` and `100`; out-of-range values return `400 VAL_INVALID_INPUT`. `fromDate` and `toDate` are optional ISO-8601 timestamps, and `fromDate` must not be later than `toDate`. `type` accepts `ALL`, `DEPOSIT`, `WITHDRAWAL`, `PAYMENT`, `REFUND`, or a defined `WalletTransactionType` name. An invalid type returns a validation error.

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

## VNPay callbacks

### IPN

```http
POST /api/v1/wallets/vnpay/ipn
Content-Type: application/x-www-form-urlencoded

vnp_TmnCode=<configured-merchant>&vnp_TxnRef=TOPUP-reference&vnp_Amount=10000000&vnp_CurrCode=VND&vnp_ResponseCode=00&vnp_SecureHashType=HmacSHA512&vnp_SecureHash=<signature>
```

The endpoint also accepts a JSON object with the same VNPay parameter names. `vnp_Amount` must be an unsigned integer in VND minor units (the pending top-up or direct-order amount multiplied by 100), and `vnp_CurrCode` must be `VND`. The signature must be a valid HMAC-SHA512 using the configured hash secret; `vnp_SecureHashType` must be `HmacSHA512`, and `vnp_TmnCode` must match the configured merchant code. A matching pending top-up is required before the wallet is credited; a matching pending direct order transitions its payment state without changing the wallet balance.

The payment row is locked while its state and associated effects are committed atomically. A duplicate notification returns response code `02` without another credit or settlement; an invalid signature/merchant/algorithm returns `97`, a missing order returns `01`, an invalid amount/currency returns `04`, and malformed or incomplete input returns `99`. The endpoint acknowledges with HTTP `200` and a VNPay response object:

```json
{
  "responseCode": "00",
  "message": "Success"
}
```

### Browser return

```http
GET /api/v1/wallets/vnpay/return?<vnpay-query-parameters>
```

The route uses the same configured HMAC-SHA512, hash-type, and merchant-code validation and reports the result; it does not credit the wallet. A valid callback returns fields `success`, `transactionReference`, `responseCode`, `transactionNumber`, and `message`. An invalid signature, merchant code, or signature algorithm returns `400` with `errorCode: "VAL_INVALID_SIGNATURE"`.

## VNPay direct checkout

```http
POST /api/v1/wallets/checkout/direct
x-user-id: 11111111-1111-1111-1111-111111111111
Content-Type: application/json

{
  "orderId": "33333333-3333-3333-3333-333333333333",
  "amount": 100000,
  "bankCode": null,
  "returnUrl": "https://shop.example/checkout/return"
}
```

`orderId` must be a non-empty UUID. The wallet must use VND, the amount must be a positive whole-VND value within the persisted `decimal(18,2)` precision, and `returnUrl` must be absolute HTTPS. The service creates a pending `DirectPaymentOrder` and `DirectGatewayPayment` transaction, then returns a signed VNPay URL:

```json
{
  "transactionId": "transaction-id",
  "orderId": "33333333-3333-3333-3333-333333333333",
  "amount": 100000,
  "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
  "status": "Pending",
  "createdAt": "2026-11-02T10:00:00+00:00"
}
```

This endpoint initiates a VNPay payment; it does not debit the wallet balance. A matching retry while pending reuses the same VNPay reference; changed parameters, a different owner, or a terminal order returns `409 PAYMENT_ORDER_CONFLICT`. The signed IPN route settles direct orders as well as wallet top-ups; the browser return is informational only.

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
POST /api/v1/admin/wallets/{walletId}/adjustments
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: admin
Content-Type: application/json

{
  "amount": 50000,
  "adjustmentType": "Credit",
  "reason": "Support case"
}
```

`adjustmentType` must be `Credit` or `Debit`; `amount` must be positive and `reason` is required. The result includes `adjustmentId`, `walletId`, `amount`, `balanceAfter`, and `createdAt`. The operation records an audit log.

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

Common status codes are `400` for invalid input or business rules, `401` when user identity is missing, `403` for missing admin/staff role, and `404` for missing wallets/resources. Exact business error codes and details depend on the failing operation. VNPay IPN acknowledgements are provider response objects, not Problem Details.

The service applies EF Core migrations at startup. Review database state and backups before connecting it to a shared database. VNPay routes require configured merchant credentials and callback URLs. This repository does not include the API Gateway or Commerce service, so their routing, authentication boundary, and end-to-end checkout behavior remain to be verified.
