# 09 — WALLET PAYMENTS API (IMPLEMENTED)

> **Service:** `WalletService`  
> **Local base URL:** `http://localhost:8008`  
> **Protocol:** REST over HTTP/JSON; each request returns its result before the caller continues.  
> **Authentication:** JWT bearer token, except VNPay callback routes.  
> **Status:** Routes in this document are implemented in this repository. Gateway proxy exposure is not verified here.

This document describes the WalletService routes that are present in code. The broader ecosystem proposal is available in [`../B_NONE/09_WALLET_PAYMENTS_API.md`](../B_NONE/09_WALLET_PAYMENTS_API.md); it includes routes and capabilities that are not all implemented by this service.

For Gateway, UserService, Commerce, and future Auction integrations, see the [Synchronous Service Integration Guide](../../specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md).

## Table of contents

1. [Authentication and errors](#1-authentication-and-errors)
2. [Wallet endpoints](#2-wallet-endpoints)
3. [VNPay callbacks](#3-vnpay-callbacks)
4. [Internal synchronous order payment](#4-internal-synchronous-order-payment)
5. [Notable limitations](#5-notable-limitations)

## 1. Authentication and errors

Send the caller's access token on authenticated routes:

```http
Authorization: Bearer <access-token>
```

The user ID is read from the configured JWT user-ID claim (default: `sub`); it is not accepted from public request bodies. Missing or unusable identity returns `401 Unauthorized`.

Business errors use Problem Details with an `errorCode` extension. Current mappings include `WALLET_NOT_FOUND` (`404`), `INSUFFICIENT_BALANCE` (`409`), `ORDER_ALREADY_PAID` (`409`), and `VAL_INVALID_INPUT` (`400`). FluentValidation failures return `400` validation-problem responses.

## 2. Wallet endpoints

### 2.1 Get the current user's wallet

```http
GET /api/v1/wallets/me
Authorization: Bearer <access-token>
```

Returns the caller's wallet. If none exists, the service creates a VND wallet with zero balance.

**`200 OK`**

```json
{
  "id": "wallet-id",
  "userId": "user-id",
  "balance": 0,
  "currency": "Vnd",
  "status": "ACTIVE",
  "updatedAt": "2026-11-01T10:00:00+00:00"
}
```

### 2.2 Create a VNPay top-up

```http
POST /api/v1/wallets/topups/vnpay
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "amount": 100000,
  "currency": "VND"
}
```

`currency` is optional and defaults to `VND`. The amount must be a positive whole VND amount no greater than `999,999,999`.

**`201 Created`**

```json
{
  "topupId": "topup-id",
  "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
  "amount": 100000,
  "currency": "Vnd",
  "status": "Pending",
  "createdAt": "2026-11-01T10:00:00+00:00"
}
```

The balance is credited only after a valid VNPay IPN confirms successful payment.

### 2.3 Transfer wallet funds

```http
POST /api/v1/wallets/transfers
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "recipientUserId": "11111111-1111-1111-1111-111111111111",
  "amount": 25000,
  "note": "Transfer"
}
```

**`201 Created`** returns the sender's transaction:

```json
{
  "id": "transaction-id",
  "walletId": "wallet-id",
  "userId": "sender-user-id",
  "type": "AdjustmentDebit",
  "amount": 25000,
  "currency": "Vnd",
  "balanceAfter": 75000,
  "status": "Success",
  "note": "Transfer",
  "createdAt": "2026-11-01T10:00:00+00:00"
}
```

The operation also records a credit transaction for the recipient. A transfer to the caller's own user ID is rejected.

### 2.4 List the current user's transactions

```http
GET /api/v1/wallets/transactions?page=1&pageSize=10&type=TopupVnpay
Authorization: Bearer <access-token>
```

All query parameters are optional. `page` defaults to `1`; `pageSize` defaults to `10` and is capped at `100`. An unrecognized `type` is ignored.

**`200 OK`**

```json
{
  "items": [
    {
      "id": "transaction-id",
      "walletId": "wallet-id",
      "userId": "user-id",
      "type": "TopupVnpay",
      "amount": 100000,
      "currency": "Vnd",
      "balanceAfter": 100000,
      "status": "Success",
      "note": "VNPay top-up transaction-reference",
      "createdAt": "2026-11-01T10:00:00+00:00"
    }
  ],
  "total": 1
}
```

## 3. VNPay callbacks

VNPay callback routes are anonymous and receive VNPay parameters as query parameters. Both singular and plural path aliases are mapped.

### 3.1 IPN — confirm top-up

```http
GET /api/v1/wallet/vnpay/ipn?<vnpay-parameters>
```

Alias: `GET /api/v1/wallets/vnpay/ipn?<vnpay-parameters>`

The service validates the signature and amount, credits a successful top-up once, and returns VNPay's acknowledgement:

```json
{
  "rspCode": "00",
  "message": "Confirm Success"
}
```

Invalid signatures, missing orders, invalid amounts, and duplicate callbacks return the corresponding VNPay response code and message.

### 3.2 Return — report payment result to the browser

```http
GET /api/v1/wallet/vnpay/return?<vnpay-parameters>
```

Alias: `GET /api/v1/wallets/vnpay/return?<vnpay-parameters>`

This route validates the signature and returns a small result object. It does not itself credit the wallet; the IPN performs confirmation.

```json
{
  "validSignature": true,
  "transactionRef": "transaction-reference",
  "responseCode": "00"
}
```

## 4. Internal synchronous order payment

Commerce calls this authenticated service-to-service endpoint and waits for the response before marking the order paid:

```http
POST /api/v1/internal/wallet/orders/{orderId}/payment
Authorization: Bearer <buyer's access-token>
Content-Type: application/json

{
  "amount": 100000
}
```

**`200 OK`** returns the resulting wallet transaction. Repeating the same order ID, buyer, and amount returns the original payment. Reusing the order ID with a different buyer or amount returns `409 ORDER_ALREADY_PAID`.

This endpoint is excluded from Swagger's public API description. The database schema must have `WalletTransaction.ReferenceId` available for order idempotency.

## 5. Notable limitations

- The withdrawal route exists, but it only debits wallet balance and records a transaction; it does **not** submit a payout to a bank. Track it in [`../B_NONE/README.md`](../B_NONE/README.md).
- The API Gateway and Commerce services are not part of this repository. Gateway routing and end-to-end checkout integration are therefore not verified here.
- VNPay top-up requires valid merchant configuration and a reviewed PostgreSQL schema.
