# WalletService integration contract

> **Audience:** API Gateway, Commerce, and any other service or client that calls WalletService.
> **Service:** `WalletService` — `http://localhost:8008` (port `8008` is fixed).
> **Protocol:** REST over HTTP/JSON. No gRPC, message broker, or events.
> **Status:** Verified against `Wallet.Api/Controllers/Public/` as of 2026-11-10. Routing, gateway authentication, and the consumers' behavior are not verified in this repository.

This is the consumer-facing summary. Per-endpoint detail lives in the linked
documents; where this page and the source disagree, the source is authoritative.

| Topic | Detail document |
|---|---|
| Wallet, top-up, transfer, history, withdrawal request, VNPay | [wallet-payments.md](wallet-payments.md) |
| Direct checkout and its polling endpoint | [checkout/direct.md](checkout/direct.md), [checkout/direct-status.md](checkout/direct-status.md) |
| VNPay IPN callback | [vnpay/ipn.md](vnpay/ipn.md) |
| Withdrawal approval, ledger report, reconciliation report | [admin-financial-operations.md](admin-financial-operations.md) |

## 1. Trust boundary and identity

WalletService does **not** validate JWTs and the internal service-key middleware
is disabled. It trusts identity headers forwarded by the API Gateway:

| Header | Meaning | Used by |
|---|---|---|
| `x-user-id` | Caller UUID (required, non-empty GUID) | All user and admin routes |
| `x-user-roles` | Comma-separated roles, e.g. `seller`, `admin`, `staff` | Role-restricted routes |

Rules for callers:

- The gateway MUST authenticate the caller, overwrite any client-supplied
  `x-user-id`/`x-user-roles`, and keep WalletService on a private network.
- The caller identity is never taken from a request body. Fields such as
  `userId`, `adminUserId`, and `clientIp` are server-filled and ignored in JSON.
- A caller may only act on its own wallet. Missing identity returns `401
  UNAUTHORIZED`; a missing required role returns `403 FORBIDDEN`.
- The service-to-service routes in section 3 have **no identity or role check
  at all**. They MUST NOT be routed from the public internet.

## 2. Conventions

- Success responses are pure DTOs or `PagedData<T>`; the gateway wraps them in
  its own envelope. WalletService adds no `ApiResponse<T>` wrapper.
- Property names are camelCase. Enums are PascalCase strings
  (`"Success"`, `"TopupVnpay"`); integers and upper-case strings are rejected
  as input where the contract is an enum.
- Money is a JSON number with at most two decimal places (`decimal(18,2)`).
  The wallet currency is VND; the response `currency` value is `"Vnd"`.
- Timestamps are ISO-8601 with offset.
- IDs are UUID strings.
- Pagination: `page` (default `1`, minimum `1`) and `pageSize` (default `10`,
  range `1`–`100`), applied in the database. Response:

```json
{
  "items": [],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 10,
    "totalCount": 0,
    "totalPages": 0,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

### Error model

All errors except VNPay IPN acknowledgements use `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errorCode": "VAL_INVALID_INPUT",
  "detail": "Amount must be greater than zero.",
  "errors": { "Amount": ["Amount must be greater than zero."] },
  "traceId": "request-trace-id",
  "timestamp": "2026-11-10T01:00:00Z"
}
```

Branch on `errorCode` (SCREAMING_SNAKE_CASE), not on `detail` text.

| HTTP | `errorCode` | Meaning |
|---:|---|---|
| 400 | `VAL_INVALID_INPUT` | Invalid body, query, or path value |
| 400 | `INSUFFICIENT_BALANCE` | Not enough funds for the operation |
| 400 | `BUSINESS_RULE_VIOLATION` | Rule failure, e.g. withdrawal already processed |
| 400 | `VAL_INVALID_SIGNATURE` | VNPay return signature/merchant/algorithm invalid |
| 401 | `UNAUTHORIZED` | Missing/invalid `x-user-id` |
| 403 | `FORBIDDEN` | Caller lacks the required role |
| 404 | `WALLET_NOT_FOUND`, `NOT_FOUND`, `PAYMENT_NOT_FOUND` | Resource missing |
| 409 | `PAYMENT_ORDER_CONFLICT`, `SETTLEMENT_CONFLICT`, `REFUND_CONFLICT`, `REFUND_EXCEEDS_PAYMENT` | Replay with different details, or invalid refund |
| 500 | `INTERNAL_SERVER_ERROR` | Unexpected failure; outcome of a write is unknown, reconcile before retrying |

## 3. Service-to-service routes (Commerce → Wallet)

These routes are hidden from Swagger. `{orderId}` and `{subOrderId}` are UUIDs
owned by Commerce and used as the idempotency anchor.

| Method | Route | Identity | Purpose |
|---|---|---|---|
| `POST` | `/api/v1/wallets/orders/{orderId}/payment` | `x-user-id` = buyer | Debit the buyer's wallet for an order |
| `POST` | `/api/v1/wallets/sub-orders/{subOrderId}/settle` | none | Credit the seller's wallet for a sub-order payout |
| `POST` | `/api/v1/wallets/orders/{orderId}/refund` | none | Credit the payer's wallet for an order refund |
| `POST` | `/api/v1/wallets/sub-orders/{subOrderId}/refund` | none | Credit the payer's wallet for a sub-order refund |

All four return `200 OK` with the same `WalletOrderTransactionResponse`:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "walletId": "2fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "11111111-1111-1111-1111-111111111111",
  "type": "PurchaseDebit",
  "amount": 100000,
  "currency": "Vnd",
  "balanceAfter": 400000,
  "status": "Success",
  "note": "Payment for order via wallet",
  "createdAt": "2026-11-10T01:00:00+00:00"
}
```

### 3.1 Pay an order

```http
POST /api/v1/wallets/orders/{orderId}/payment
x-user-id: <buyer-uuid>
Content-Type: application/json

{ "amount": 100000 }
```

- `amount` > 0, at most two decimals. Creates a `PurchaseDebit`.
- Idempotent on `orderId`: replaying the same buyer and amount returns the
  original transaction with no second debit.
- `409 PAYMENT_ORDER_CONFLICT`: the order already has a different buyer/amount
  or a direct-gateway (VNPay) payment.
- `400 INSUFFICIENT_BALANCE`, `404 WALLET_NOT_FOUND`.

### 3.2 Settle a seller sub-order

```http
POST /api/v1/wallets/sub-orders/{subOrderId}/settle

{ "sellerId": "<seller-uuid>", "amount": 90000 }
```

- Creates an `EscrowRelease` credit on the seller's wallet.
- Idempotent on `subOrderId`; a replay with a different seller or amount returns
  `409 SETTLEMENT_CONFLICT`.
- WalletService does not hold buyer funds in escrow. Commerce decides when a
  payout is released and for what amount.

### 3.3 Refund

```http
POST /api/v1/wallets/orders/{orderId}/refund
POST /api/v1/wallets/sub-orders/{subOrderId}/refund

{ "userId": "<buyer-uuid>", "amount": 100000, "reason": "Order cancelled" }
```

- `reason` is optional, at most 500 characters. Creates a `RefundCredit`.
- Order refund requires a prior successful wallet `PurchaseDebit` for that order
  and the same `userId`, else `404 PAYMENT_NOT_FOUND` or `409 REFUND_CONFLICT`;
  the amount may not exceed the payment (`409 REFUND_EXCEEDS_PAYMENT`).
- Sub-order refund skips the payment lookup; Commerce owns the amount.
- Idempotent per `orderId`/`subOrderId`: a replay with the same amount returns
  the original refund; a different amount returns `409 REFUND_CONFLICT`.
- Wallet-paid orders only. VNPay direct payments are not refunded here.

## 4. Gateway-facing routes (user and admin)

| Method | Route | Access | Notes |
|---|---|---|---|
| `GET` | `/api/v1/wallets/me` | User | Creates a zero-balance VND wallet if absent |
| `POST` | `/api/v1/wallets/topups/vnpay` | User | `201`; returns `paymentUrl`, balance credited only after a signed IPN |
| `POST` | `/api/v1/wallets/transfers` | User | `200`; wallet-to-wallet, VND 1,000–100,000,000 |
| `GET` | `/api/v1/wallets/transactions` | User | Paginated own history |
| `POST` | `/api/v1/wallets/withdrawals` | `seller` (admin/staff also pass) | `201`; reserves funds, no bank payout, no idempotency key |
| `POST` | `/api/v1/wallets/checkout/direct` | User | Starts a VNPay payment; does not debit the wallet |
| `GET` | `/api/v1/wallets/checkout/direct/{orderId}` | Owner, or admin/staff | Poll the settled VNPay result |
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` | admin/staff | Approve needs `referenceCode`; reject releases funds |
| `POST` | `/api/v1/admin/wallets/{walletId}/adjustments` | admin/staff | Manual credit/debit with reason; audited |
| `GET` | `/api/v1/admin/wallets/ledger` | admin only | Filtered, paginated transaction report |
| `GET` | `/api/v1/admin/wallets/reconciliation` | admin only | Balance vs latest successful snapshot |

Provider and operational routes:

| Method | Route | Caller | Notes |
|---|---|---|---|
| `GET`/`POST` | `/api/v1/wallets/vnpay/ipn` | VNPay | Anonymous, HMAC-SHA512 signed; replies `{"RspCode","Message"}` with HTTP 200 |
| `GET` | `/api/v1/wallets/vnpay/return` | Browser | Informational; never credits the wallet |
| `GET` | `/health` | Orchestrator | `{"status":"Healthy","service":"wallet-service"}` |

### Withdrawal approval (new in Phase 2)

```http
PATCH /api/v1/admin/wallets/withdrawals/{id}
x-user-id: <admin-uuid>
x-user-roles: admin

{ "action": "Approve", "referenceCode": "BANK-TRANSFER-2026-001" }
```

`referenceCode` (non-blank, at most 100 characters) is required only for
`Approve` and confirms staff already made the manual bank transfer. The
response adds a nullable `referenceCode`. Route, statuses, and other fields are
unchanged, so older clients that omit it now receive `400 VAL_INVALID_INPUT`
on approval.

### Administrator reports (new in Phase 2)

`GET /api/v1/admin/wallets/ledger` accepts `page`, `pageSize`, `walletId`,
`type`, `status`, `fromDate`, `toDate` and returns
`PagedData<AdminWalletLedgerEntry>` ordered newest first. Entries expose no
notes or bank data.

`GET /api/v1/admin/wallets/reconciliation` accepts `page`, `pageSize`,
`walletId` and returns, per wallet, `currentBalance`,
`latestSuccessfulBalance`, `difference`, `successfulTransactionCount`,
`latestTransactionAt`, and `result` (`Balanced`, `Discrepancy`, or
`InsufficientHistory`). It compares stored balance with the latest transaction
snapshot only; it is not double-entry accounting or bank reconciliation.

## 5. Idempotency and retry matrix

WalletService has no `Idempotency-Key` header. Idempotency is by business key:

| Operation | Key | Safe to retry on timeout? |
|---|---|---|
| Pay order | `orderId` | Yes, same buyer and amount |
| Settle sub-order | `subOrderId` | Yes, same seller and amount |
| Refund order / sub-order | `orderId` / `subOrderId` | Yes, same amount |
| Direct checkout | `orderId` + same parameters while pending | Yes while pending; terminal state returns `409` |
| VNPay IPN | VNPay transaction reference | Yes; duplicates return `02` |
| Withdrawal decision | Withdrawal `id` | Yes; a second decision returns `400 BUSINESS_RULE_VIOLATION` with no new effect |
| Top-up, transfer, **withdrawal request**, balance adjustment | none | **No.** A retry can create a second record; read history or wallet state first |

After a `5xx` or timeout on a write without a business key, query
`GET /api/v1/wallets/transactions` (or the wallet) before retrying.

## 6. Reference enums

| `WalletTransactionType` | Value |
|---|---:|
| `TopupVnpay` | 0 |
| `PurchaseDebit` | 1 |
| `RefundCredit` | 2 |
| `EscrowCredit` | 3 |
| `EscrowRelease` | 4 |
| `WithdrawalDebit` | 5 |
| `WithdrawalReversal` | 6 |
| `PlatformFeeCredit` | 7 |
| `AdjustmentCredit` | 8 |
| `AdjustmentDebit` | 9 |
| `DirectGatewayPayment` | 10 |
| `TransferIn` | 11 |
| `TransferOut` | 12 |

`TransactionStatus`: `Pending` (0), `Success` (1), `Failed` (2), `Cancelled` (3).
`WithdrawalStatus` values returned today are `Pending`, `Approved`, and
`Rejected`; the enum also defines `Processing`, `Completed`, and `Failed`,
which the service does not currently produce.

## 7. Integration flows

**Wallet-paid order:** Commerce calls `POST .../orders/{orderId}/payment` for the
buyer → on `200`, mark the order paid. On cancel, call `.../refund`. After
fulfilment, call `.../sub-orders/{subOrderId}/settle` per seller.

**VNPay-paid order:** gateway calls `POST .../checkout/direct` and redirects the
buyer to `paymentUrl` → VNPay calls the signed IPN → Commerce polls
`GET .../checkout/direct/{orderId}` until `status` is `Success` or `Failed`.
Never treat the browser return as proof of payment.

**Seller withdrawal:** seller `POST .../withdrawals` (funds reserved, status
`Pending`) → staff makes the manual bank transfer → staff `PATCH` with
`Approve` and `referenceCode`, or `Reject` to release funds.

## 8. Not provided

- Escrow holding, purchase freeze/unfreeze, and auction deposit/refund/deduct/
  forfeit endpoints (deferred pending an approved internal-authentication
  contract).
- Automated bank payouts, payout retries, or external bank reconciliation.
- Events, webhooks, or message-broker notifications.
- A general `Idempotency-Key` contract.
- Authentication of service-to-service callers.

## 9. Verification

Routes and DTOs were checked against the controllers and CQRS handlers in this
repository, and Swagger for the public routes. Run
`./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release`,
and `dotnet test Wallet.sln -c Release` after any change to this contract.
