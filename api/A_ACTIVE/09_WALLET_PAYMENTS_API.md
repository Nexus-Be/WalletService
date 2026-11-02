# 09 — Wallet Payments API (implemented)

> **Service:** `WalletService`  
> **Base URL:** `http://localhost:8008`
> **Protocol:** REST over HTTP/JSON
> **Status:** Routes below are implemented by this repository. Gateway routing and end-to-end service integration are not verified here.

This contract reflects `Wallet.Api/Controllers/Public/WalletsController.cs` and `AdminWalletsController.cs`. Broader ecosystem proposals are kept separately in [`../B_NONE/09_WALLET_PAYMENTS_API.md`](../B_NONE/09_WALLET_PAYMENTS_API.md).

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
| `POST` | `/api/v1/wallets/withdrawals` | User identity required | Currently disabled; returns a business-rule error |
| `POST` | `/api/v1/wallets/vnpay/ipn` | Anonymous callback | Processes VNPay IPN |
| `GET` | `/api/v1/wallets/vnpay/return` | Anonymous callback | Reports VNPay browser-return result |
| `POST` | `/api/v1/wallets/checkout/direct` | `x-user-id` required by handler | Starts external payment-gateway checkout (`200`) |
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

`amount` must be between VND 10,000 and VND 100,000,000. Currency and payment method default to `VND` and `VNPAY`; the bank code and return URL are optional.

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

All query parameters are optional. `page` defaults to `1`; `pageSize` defaults to `10`, and values outside `1..100` are reset to `20`. `fromDate` and `toDate` are optional ISO-8601 timestamps. `type` accepts `ALL`, `DEPOSIT`, `WITHDRAWAL`, `PAYMENT`, `REFUND`, or a `WalletTransactionType` enum name. An invalid type returns a validation error.

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
  "accountNumber": "123456789"
}
```

Although the route and request contract exist, its handler currently rejects every request with `400 BUSINESS_RULE_VIOLATION`. It does not create a withdrawal request or initiate a bank payout. Do not treat this route as an available withdrawal flow.

## VNPay callbacks

### IPN

```http
POST /api/v1/wallets/vnpay/ipn
Content-Type: application/x-www-form-urlencoded

vnp_TxnRef=TOPUP-reference&vnp_Amount=10000000&vnp_ResponseCode=00&vnp_SecureHash=<signature>
```

The endpoint also accepts a JSON object with the same VNPay parameter names. `vnp_Amount` is expressed in VND minor units (the top-up amount multiplied by 100). A valid signature and matching pending top-up are required before the wallet is credited. Duplicate or invalid callbacks return VNPay response codes in the response body:

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

The route validates the signature and reports the result; it does not credit the wallet. A valid callback returns fields `success`, `transactionReference`, `responseCode`, `transactionNumber`, and `message`. An invalid signature returns `400` with `errorCode: "VAL_INVALID_SIGNATURE"`.

## External-gateway checkout

```http
POST /api/v1/wallets/checkout/direct
x-user-id: 11111111-1111-1111-1111-111111111111
Content-Type: application/json

{
  "orderId": "33333333-3333-3333-3333-333333333333",
  "amount": 100000,
  "gateway": "example",
  "bankCode": null,
  "returnUrl": "https://shop.example/checkout/return"
}
```

`orderId` must be a UUID. The service creates a pending `DirectGatewayPayment` transaction and returns a gateway URL:

```json
{
  "transactionId": "transaction-id",
  "orderId": "33333333-3333-3333-3333-333333333333",
  "amount": 100000,
  "paymentUrl": "https://payments.example.com/checkout?...",
  "status": "Pending",
  "createdAt": "2026-11-02T10:00:00+00:00"
}
```

This endpoint initiates an external payment; it does not debit the wallet balance. A repeated matching pending request reuses the payment transaction. The database migration defines `WalletTransaction.ReferenceId` and its type index for the order reference.

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

`action` must be `Approve` or `Reject`. The operation requires an existing pending withdrawal request; approval debits the previously locked funds and rejection releases them. The current user-facing withdrawal route cannot create such a request.

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

Validation and business-rule errors use `application/problem+json` with fields such as `status`, `errorCode`, `detail`, and `traceId`. Common status codes are `400` for invalid input or business rules, `401` when user identity is missing, `403` for missing admin/staff role, and `404` for missing wallets/resources. Exact error codes and details depend on the failing operation. VNPay IPN acknowledgements are provider response objects, not Problem Details.

The service applies EF Core migrations at startup. Review database state and backups before connecting it to a shared database. VNPay and direct gateway routes require separately configured provider credentials and callback URLs. This repository does not include the API Gateway or Commerce service, so their routing, authentication boundary, and end-to-end checkout behavior remain to be verified.
