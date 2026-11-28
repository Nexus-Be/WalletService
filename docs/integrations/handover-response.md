# Cross-Service Handoff Response — WalletService

**Branch:** `feature/handover-wallet-hardening`
**Service:** `WalletService` (Port `8008`, `wallet_db:5440`)

---

## 1. NotificationService Integration (`WALLET_NOTIFICATION_HANDOFF.md`)

### Ownership & Source-of-Truth Confirmation
- **Withdrawal events (`POST /api/v1/notifications/withdrawal-events`)**: **WalletService** emits directly after DB transaction commit on `ProcessWithdrawal` transitions into `Approved`, `Rejected`, `Completed`, or `Failed` (`StartProcessing` is internal intermediate state and does not notify; `ResolveFailed` never exposes `VerificationEvidence` in `displayReason`).
- **Auction deposit events (`POST /api/v1/notifications/auction-deposits`)**: **WalletService** owns and emits after DB transaction commit on `CreateHold` (`Held`), `ReleaseHold` (`Released`), `CaptureHold` (`Forfeited` when `Purpose == "FORFEIT"`, `Captured` when `Purpose == "ORDER_PAYMENT"`), and `RefundCapturedHold` (`Refunded`). **AuctionService** must NOT emit duplicate deposit notifications for those same transitions.
- **Order refunds & order settlements (`order-refunds`, `order-settlements`)**: **CommerceService** (order owner) continues to emit these using the confirmed financial transaction returned by WalletService's internal `/refund` and `/settle` endpoints. WalletService does NOT emit `order-refunds` or `order-settlements` to prevent duplicate notifications.

### Caller, Auth & Resilience
- **Interface**: `Wallet.Application/Interfaces/INotificationClient.cs`
- **Typed HTTP Client**: `Wallet.Infrastructure/Services/Notification/NotificationClient.cs` (10 s timeout, camelCase JSON, W3C `traceparent` propagated via `HttpClient`)
- **Configuration**:
  - Base URL: `NOTIFICATION_SERVICE_URL` or `Services:Notification:BaseUrl` (default `http://localhost:8007/`)
  - Optional S2S key: `NOTIFICATION_SERVICE_KEY` or `Services:Notification:ServiceKey` sent as `X-Internal-Service-Key` when configured (omitted when unset)
- **Non-blocking failure handling**: Notification calls execute strictly **after** `transaction.CommitAsync(...)`. Non-2xx responses or connection exceptions are logged as warnings and written to `AuditLogs` with `AuditAction.NotificationDispatchFailed` (`31`) containing endpoint, full payload, HTTP status code, and error text for manual replay—committed financial balances are never rolled back.
- **Replay idempotency**:
  - `sourceEventId` is deterministic: `withdrawal:{withdrawalId:D}:{Status}` and `hold:{holdId:D}:{Status}`.
  - Replaying an idempotent hold operation re-dispatches with persisted business fields (`transactionId`, `confirmedAt`, `amount`, `status`) unchanged alongside the current `traceId`.

### Sample Payloads

#### `POST /api/v1/notifications/withdrawal-events`
```json
{
  "sourceEventId": "withdrawal:4f6a1a32-7c9f-4e64-9a40-7b2f8d4e1122:Completed",
  "withdrawalId": "4f6a1a32-7c9f-4e64-9a40-7b2f8d4e1122",
  "recipientUserId": "9c21d1a4-0e12-43d7-9b1e-1d0b22aa4455",
  "status": "Completed",
  "confirmedAt": "2026-11-19T02:15:30.0000000+00:00",
  "displayReason": null,
  "transactionId": "BANK-TXN-20261119-01",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```
Supported `status` values emitted: `Approved`, `Rejected`, `Completed`, `Failed`.

#### `POST /api/v1/notifications/auction-deposits`
```json
{
  "sourceEventId": "hold:8a3c4211-1d24-45a8-b7d3-90e1c6b12345:Held",
  "transactionId": "8a3c4211-1d24-45a8-b7d3-90e1c6b12345",
  "auctionId": "a1b2c3d4-1111-2222-3333-444455556666",
  "bidderId": "9c21d1a4-0e12-43d7-9b1e-1d0b22aa4455",
  "status": "Held",
  "amount": 500000,
  "currency": "VND",
  "confirmedAt": "2026-11-19T02:16:00.0000000+00:00",
  "traceId": "00-7f9c2f3577b34da6a3ce929d0e0e9999-00f067aa0ba902b7-01"
}
```
Supported `status` values emitted: `Held`, `Released`, `Forfeited`, `Captured`, `Refunded`.

---

## 2. CommerceService Migration Checklist (`X-Internal-Service-Key` Required)

All internal endpoints require header `X-Internal-Service-Key` matching `INTERNAL_SERVICE_SECRET` / `InternalSecurity:ServiceKey`.

> **Immediate breaking security fix**: The 4 legacy routes under `/api/v1/wallets/...` (`orders/{orderId}/payment`, `sub-orders/{subOrderId}/settle`, `orders/{orderId}/refund`, `sub-orders/{subOrderId}/refund`) now enforce `[RequireInternalServiceKey]` and will be removed once CommerceService migrates to the `/api/v1/internal/wallets/...` routes below.

| Operation | Method & Target Route | Request / Notes |
|---|---|---|
| Lookup wallet | `GET /api/v1/internal/wallets/{userId}` | Replaces calling `GET /api/v1/wallets/me` with spoofed `x-user-id`; returns `availableBalance`, `lockedBalance`, `status`. |
| Pay order | `POST /api/v1/internal/wallets/orders/{orderId}/payment` | JSON `{ "buyerId": "<uuid>", "amount": 100000 }` |
| Settle sub-order | `POST /api/v1/internal/wallets/sub-orders/{subOrderId}/settle` | JSON `{ "sellerId": "<uuid>", "amount": 100000, "parentOrderId": "<uuid?>" }` (`parentOrderId` links multiple sub-order payouts against the parent `OrderEscrow`) |
| Refund order | `POST /api/v1/internal/wallets/orders/{orderId}/refund` | JSON `{ "userId": "<uuid>", "amount": 100000, "reason": "..." }` |
| Refund sub-order | `POST /api/v1/internal/wallets/sub-orders/{subOrderId}/refund` | JSON `{ "userId": "<uuid>", "amount": 100000, "reason": "...", "parentOrderId": "<uuid?>" }` (`parentOrderId` validates buyer ownership and decrements parent `OrderEscrow`) |
| Expire direct checkout | `POST /api/v1/internal/wallets/checkout/direct/{orderId}/expire` | Marks unpaid Stripe direct payment expired on order timeout. |
| Adjust order payment | `POST /api/v1/internal/wallets/orders/{orderId}/adjust-payment` | Header `Idempotency-Key`; JSON `{ "buyerId": "<uuid>", "deltaAmount": 15000, "reason": "..." }`. Positive `deltaAmount` debits buyer; negative refunds buyer from escrow. |
| Cancel order escrow | `POST /api/v1/internal/wallets/orders/{orderId}/escrow/cancel` | Header `Idempotency-Key`; JSON `{ "reason": "...", "expectedBuyerId": "<uuid?>", "expectedAmount": 100000 }`. Strictly all-or-nothing (`RemainingAmount == OriginalAmount`); credits funding wallet (`EscrowCancellationRefund`), marks escrow `Cancelled`, and writes `OrderEscrowCancel` audit log. |

---

## 3. AuctionService Migration Checklist

- Mirror internal routes are live under `/api/v1/internal/wallets/holds/...` using `[RequireAuctionServiceKey]` (`InternalSecurity:AuctionServiceKey` with fallback to `InternalSecurity:ServiceKey`).
- Legacy `/api/v1/wallets/holds/...` routes remain active until AuctionService and CommerceService (`GetAuctionDepositHoldAsync`) switch URLs.
- `ReleaseHold` accepts `"BUYER_WITHDRAWN"`, `"AUCTION_SETTLED"`, `"SECOND_CHANCE_EXPIRED"`, `"LATE_AUTHORIZATION"`, `"RECONCILED_CLOSED_AUCTION"`, `"AMOUNT_MISMATCH"`, and any valid `SCREAMING_SNAKE_CASE` reason code (`1..128` chars) in addition to existing release reasons.
- `CreateHold` replay returns live `Status`.

---

## 4. Gateway Routing Verification

- Existing Gateway catch-all routes (`/api/v1/wallets/{**catch-all}` and `/api/v1/admin/wallets/{**catch-all}`) cover all newly added public seller/buyer/admin endpoints.
- `/api/v1/internal/*` MUST remain unexposed in Gateway routing.
