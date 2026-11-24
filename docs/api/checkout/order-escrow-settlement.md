# WalletService — Order Payment, Settlement, and Refund

> **Feature / Use Case:** Wallet payment escrow for auction orders  
> **Status:** IN_PROGRESS (Implementation complete; PostgreSQL verification pending)  
> **Created:** 2026-11-17  
> **Scope:** WalletService only. No route, request, response, status-code, or
> authentication contract changes.

## 1. Routing and authorization

These are existing routes. No endpoint or CQRS use case is added.

| Method and route | Current caller identity / gate | Success |
|---|---|---|
| `POST /api/v1/wallets/orders/{orderId:guid}/payment` | Authenticated buyer; controller obtains buyer ID from trusted server identity | `200 OK` |
| `POST /api/v1/wallets/sub-orders/{subOrderId:guid}/settle` | Existing Commerce caller; `LegacyCommerceCredits` release gate | `200 OK` |
| `POST /api/v1/wallets/orders/{orderId:guid}/refund` | Existing Commerce caller; `OrderRefund` release gate (`ReleaseFeatures:OrderRefundEnabled`, off by default everywhere) | `200 OK` |
| `POST /api/v1/wallets/sub-orders/{subOrderId:guid}/refund` | Child refund; `OrderRefund` release gate; **requires `parentOrderId`** (`400 PARENT_ORDER_REQUIRED` otherwise); the answer carries `replayed` | `200 OK` |
| `POST /api/v1/wallets/holds/{holdId:guid}/capture` | Auction service key; `AuctionOrderCapture` release gate for `ORDER_PAYMENT` | `200 OK` |
| `POST /api/v1/wallets/holds/{holdId:guid}/refund-captured` | Auction service key and idempotency key | `200 OK` |

The order payment, settlement, and refund actions remain hidden from Swagger as
they are today. The order and same-ID auction settlement/refund operations use
the current order ID; ordinary child operations whose ID cannot be mapped to a
parent payment retain their existing compatibility behavior.

The payment deducted for these order operations is the buyer's digital Wallet
balance. Physical cash/COD collection and reconciliation are outside this
Wallet contract.

## 2. Inputs and validation

| Operation | Route/body input | Validation and identity |
|---|---|---|
| Wallet payment | `orderId` in route; body `{ "amount": 100000 }` | Non-empty order UUID; amount greater than zero with at most two fractional digits. Buyer ID is populated from the authenticated server identity, not the body. |
| Seller settlement | `subOrderId` in route; body `{ "sellerId": "<uuid>", "amount": 90000 }` | Non-empty sub-order UUID; valid seller UUID; amount greater than zero with at most two fractional digits. |
| Order refund | `orderId` in route; body `{ "userId": "<uuid>", "amount": 100000, "reason": "..." }` | Non-empty order UUID and user UUID; amount greater than zero with at most two fractional digits; optional reason is at most 500 characters. The existing body contract is unchanged. |
| Captured-deposit refund | `holdId` in route; body includes matching `orderId` and reason | Existing service-key, idempotency, hold-state, and full-original-deposit rules remain unchanged. |

Server-populated fields on the payment command (`OrderId`, `UserId`) remain
ignored in JSON. Route identifiers are authoritative for settlement and refund.

## 3. Response contract

The three order operations return the existing pure
`WalletOrderTransactionResponse` with `200 OK`; the service does not wrap it in
`ApiResponse<T>`.

```json
{
  "id": "transaction-id",
  "walletId": "wallet-id",
  "userId": "owner-user-id",
  "type": "PurchaseDebit",
  "amount": 100000,
  "currency": "Vnd",
  "balanceAfter": 900000,
  "status": "Success",
  "note": "Payment for order via wallet",
  "createdAt": "2026-11-17T14:30:00+00:00"
}
```

Current enum values are persisted as integers and emitted as PascalCase strings:

| Enum value | Database integer | API string |
|---|---:|---|
| `WalletTransactionType.PurchaseDebit` | `1` | `"PurchaseDebit"` |
| `WalletTransactionType.EscrowRelease` | `4` | `"EscrowRelease"` |
| `WalletTransactionType.RefundCredit` | `2` | `"RefundCredit"` |
| `TransactionStatus.Success` | `1` | `"Success"` |

## 4. Error matrix

Errors use the existing Wallet `application/problem+json` format.

| HTTP status | Error code | Message / condition |
|---:|---|---|
| `400` | `VAL_INVALID_INPUT` | Request validation failed. |
| `400` | `INSUFFICIENT_BALANCE` | Buyer Wallet balance cannot fund the payment. |
| `401` | `UNAUTHORIZED` | Payment caller identity is missing or invalid; hold service key is missing or invalid. |
| `404` | `WALLET_NOT_FOUND` | Required buyer or seller Wallet does not exist. |
| `404` | `PAYMENT_NOT_FOUND` | No completed Wallet payment exists for the order refund. |
| `409` | `PAYMENT_ORDER_CONFLICT` | The order already has a different or completed payment. |
| `409` | `SETTLEMENT_CONFLICT` | The sub-order was already settled with different details, or the order payment was resolved without recoverable escrow. |
| `409` | `REFUND_CONFLICT` | The order was already refunded with different details, the refund user does not match the payer, or the order payment was resolved without recoverable escrow. |
| `409` | `REFUND_EXCEEDS_PAYMENT` | Refund amount exceeds the original order payment. |
| `409` | `ORDER_ESCROW_INSUFFICIENT` | Remaining wallet-payment escrow cannot fund the requested seller payout or refund. Captured deposits are not used to cover the difference. The existing `ESCROW_INSUFFICIENT` remains reserved for captured-deposit refunds. |
| `409` | `ORDER_ESCROW_UNAVAILABLE` | Existing payment history is ambiguous or cannot be safely matched to an escrow record. |
| `503` | `FEATURE_DISABLED` | Existing settlement/refund or `ORDER_PAYMENT` capture release gate is disabled. |
| `500` | `INTERNAL_CONFIGURATION_ERROR` | Existing release-gate configuration is invalid. |

## 5. Pre-code implementation and verification plan

1. Keep the existing `WalletsController` routes and request/response records;
   add no controller, endpoint, or handler.
2. Persist one `OrderEscrow` per order payment. Create it atomically with the
   buyer debit, wallet transaction, and audit record.
3. Serialize payment, same-ID seller settlement, and order refund by the
   existing order advisory lock. Seller payout consumes only remaining
   wallet-payment escrow and rejects insufficient escrow even if a captured
   deposit exists.
4. Order refund consumes only remaining wallet-payment escrow. One partial
   seller payout and one refund may both succeed if each is covered by the
   remaining escrow; exact retries are idempotent, and overdrawn or changed
   retries do not move funds. Keep regular marketplace child-operation behavior
   unchanged where the parent payment ID is absent.
5. Do not modify captured-deposit capture/refund handlers. Seller settlement
   and order refund leave deposit escrow untouched; the existing hold operation
   can still refund its full original amount.
6. Add append-only audit actions only when an existing action is not
   semantically correct. Use `AddAuditLog` in the same database transaction as
   each financial mutation.
7. Verify PostgreSQL integration scenarios, unchanged API contracts, the
   architecture verifier, Release build, full test suite, and GitNexus changes.

### Pre-code checklist

- [x] Existing payment, settlement, refund, hold, and audit source reviewed.
- [x] Current routes, payloads, response DTO, validation, errors, and enum
      values recorded.
- [x] Wallet-only scope and cash-on-delivery exclusion clarified.
- [x] Revised feature plan approved.
- [x] Implementation and automated test coverage complete; PostgreSQL tests are authored but not executed without a disposable database.
- [x] Architecture verifier, Release build, and full test suite passed;
      PostgreSQL tests were skipped and remain pending under T014.
