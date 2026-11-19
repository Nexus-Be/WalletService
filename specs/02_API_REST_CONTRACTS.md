# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (WALLET SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-11-19):** ✅ 100% RESTFUL + CLASSIC CQRS — 47 use case (29 Command, 18 Query; `EnsureWalletCommand` do `GetMyWalletQueryHandler` gọi nội bộ). Controller chỉ inject `ISender`. Bãi bỏ hoàn toàn gRPC, Protobuf, RabbitMQ và MassTransit.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Public/`, `Wallet.Application/Features/`).
> **Cập nhật lần cuối:** 2026-11-19.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Khách hàng (`WalletsController` — `/api/v1/wallets`)
* `GET /api/v1/wallets/me` → `GetMyWalletQuery` → `GetMyWalletQueryHandler` (read-only; khi chưa có ví thì gửi `EnsureWalletCommand` → `EnsureWalletCommandHandler` tạo ví 0 đồng + `AuditLog` `WalletCreate`; Swagger hiển thị `x-user-id` header parameter).
* `POST /api/v1/wallets/topups/stripe` → `TopupStripeCommand` → `TopupStripeCommandHandler` (HTTP 201; cần `Idempotency-Key`).
* `GET /api/v1/wallets/topups/{id:guid}` → `GetTopupStatusQuery` → `GetTopupStatusQueryHandler` (chỉ chủ ví).
* `GET /api/v1/wallets/topups` → `ListTopupsQuery` (phân trang, chỉ chủ ví); `POST /api/v1/wallets/topups/{id:guid}/resume` → `ResumeTopupCommand` (không tạo giao dịch mới).
* `POST /api/v1/wallets/holds` → `CreateHoldCommand`; `POST /api/v1/wallets/holds/{holdId:guid}/release` → `ReleaseHoldCommand` (service key và `Idempotency-Key`).
* `GET /api/v1/wallets/holds/{holdId:guid}` và `GET /api/v1/wallets/holds/by-reference/{referenceType}/{referenceId}` → `GetHoldQuery` (service key).
* `POST /api/v1/wallets/holds/{holdId:guid}/capture` → `CaptureHoldCommand`; `POST /api/v1/wallets/holds/{holdId:guid}/refund-captured` → `RefundCapturedHoldCommand` (service key và `Idempotency-Key`).
* `GET /api/v1/wallets/me/holds` → `ListMyHoldsQuery` (phân trang, chỉ chủ ví). Chi tiết: `docs/api/holds/auction-deposits.md`.
* `POST /api/v1/wallets/withdrawals` → `RequestWithdrawalCommand` → `RequestWithdrawalCommandHandler` (HTTP 201 hoặc HTTP 200 khi replay cùng `Idempotency-Key`; hỗ trợ `bankAccountId` hoặc fallback tài khoản mặc định; chặn ví `FROZEN` 403 `WALLET_FROZEN`).
* `GET /api/v1/wallets/withdrawals` → `ListMyWithdrawalsQuery`; `GET /api/v1/wallets/withdrawals/{id:guid}` → `GetMyWithdrawalQuery`; `POST /api/v1/wallets/withdrawals/{id:guid}/cancel` → `CancelWithdrawalCommand` (hủy lệnh `Pending` và nhả `LockedBalance`).
* `GET /api/v1/wallets/bank-accounts` → `ListMyBankAccountsQuery`; `POST /api/v1/wallets/bank-accounts` → `CreateBankAccountCommand`; `PATCH /api/v1/wallets/bank-accounts/{id:guid}/default` → `SetDefaultBankAccountCommand`; `DELETE /api/v1/wallets/bank-accounts/{id:guid}` → `DeleteBankAccountCommand`.
* `POST /api/v1/wallets/transfers` → `TransferCommand` → `TransferCommandHandler` (chặn khi ví gửi hoặc nhận `FROZEN`).
* `GET /api/v1/wallets/transactions` → `ListTransactionsQuery` → `ListTransactionsQueryHandler`; `GET /api/v1/wallets/transactions/{id:guid}` → `GetTransactionByIdQuery`.
* `POST /api/v1/wallets/stripe/webhooks` → `ProcessStripeWebhookCommand` → `ProcessStripeWebhookCommandHandler` (anonymous; xác minh `Stripe-Signature`, body nguyên bản).

### 1.2. Quản trị (`AdminWalletsController` — `/api/v1/admin/wallets`)
* `GET /api/v1/admin/wallets` → `ListAdminWalletsQuery` (phân trang, lọc `status`/`userId`/`search`).
* `GET /api/v1/admin/wallets/{userId:guid}` → `GetMyWalletQuery`.
* `POST /api/v1/admin/wallets/{userId:guid}/freeze` → `FreezeWalletCommand`; `POST /api/v1/admin/wallets/{userId:guid}/unfreeze` → `UnfreezeWalletCommand`.
* `GET /api/v1/admin/wallets/withdrawals` → `ListAdminWithdrawalsQuery`; `GET /api/v1/admin/wallets/withdrawals/{id:guid}/history` → `GetWithdrawalHistoryQuery`.
* `PATCH /api/v1/admin/wallets/withdrawals/{id:guid}` → `ProcessWithdrawalCommand` (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, admin-only `ResolveFailed`; gửi `withdrawal-events` sau commit).
* `POST /api/v1/admin/wallets/{userId:guid}/adjustments` → `AdjustWalletBalanceCommand` (admin-only `EnsureAdmin`).
* `GET /api/v1/admin/wallets/ledger` → `ListWalletLedgerQuery` (admin only); `GET /api/v1/admin/wallets/reconciliation` → `GetWalletReconciliationQuery` (admin only).
* `GET /api/v1/admin/wallets/holds` & `GET /api/v1/admin/wallets/{userId:guid}/holds` → `ListAdminHoldsQuery`.
* `GET /api/v1/admin/wallets/stripe/reconciliation-required` → `ListStripeReconciliationRequiredQuery` (admin only); `POST /api/v1/admin/wallets/stripe/attempts/{attemptId:guid}/resolve` → `ResolveStripeAttemptCommand` (admin only); `POST /api/v1/admin/wallets/stripe/reconcile-pending` → `ReconcilePendingTopupsCommand` (admin only).

Withdrawal completion requires `transferConfirmed = true` and a unique `transferReference` (max 100 chars). `Approve` takes an optional `transferReference` and debits locked funds; `Complete` sets `CompletedAt`; `Fail`/`ResolveFailed` reverses the debit (`ResolveFailed` is admin-only).

The ledger accepts `page` (default 1), `pageSize` (default 10, maximum 100), optional `walletId`, `type`, `status`, `fromDate`, and `toDate`. Date filters are inclusive ISO 8601 instants with offsets, normalized to UTC. The response is the standard `PagedData<T>` shape and omits notes and bank details. Results are ordered by `CreatedAt DESC`, then transaction ID DESC.

Reconciliation accepts `page`, `pageSize`, and optional `walletId`. It compares each current wallet balance with the latest successful transaction `BalanceAfter`, ordered by creation time and transaction ID. Results are `Balanced`, `Discrepancy`, or `InsufficientHistory`; this is not double-entry accounting or external bank reconciliation.

### 1.3. Thanh toán trực tiếp & Internal S2S (`WalletsController`)
* `POST /api/v1/wallets/checkout/direct` → `CheckoutDirectCommand` (idempotent theo `OrderId`).
* `POST /api/v1/wallets/checkout/direct/{orderId:guid}/resume` → `ResumeDirectPaymentCommand`; `POST /api/v1/wallets/checkout/direct/{orderId:guid}/cancel` → `CancelDirectPaymentCommand`.
* `GET /api/v1/wallets/checkout/direct/{orderId}` → `GetDirectPaymentStatusQuery` (`[RequireRole]`; chỉ chủ đơn, admin/staff đọc được mọi đơn; 404 `PAYMENT_NOT_FOUND`). Trả `status` (`Pending`/`Success`/`Failed`) và `gatewayTransactionNo`. Chi tiết: `docs/api/checkout/direct-status.md`.
* Internal S2S routes (`~/api/v1/internal/wallets/...`, `[RequireInternalServiceKey]`): `GET {userId:guid}`, `POST freeze-purchase`, `POST unfreeze-purchase`, `POST orders/{orderId:guid}/payment`, `POST sub-orders/{subOrderId:guid}/settle`, `POST orders/{orderId:guid}/refund`, `POST sub-orders/{subOrderId:guid}/refund`, `POST checkout/direct/{orderId:guid}/expire`, `POST orders/{orderId:guid}/adjust-payment`, `POST orders/{orderId:guid}/escrow/cancel`. Legacy public routes (`orders/{id}/payment`, `sub-orders/{id}/settle`, `orders/{id}/refund`, `sub-orders/{id}/refund`) cũng gắn `[RequireInternalServiceKey]` đến khi Commerce chuyển URL.
* Internal Auction hold mirrors (`~/api/v1/internal/wallets/holds/...`, `[RequireAuctionServiceKey]`): mirror 6 action service của `holds`.

---

## 2. PHONG BÌ DỮ LIỆU VÀ MÃ LỖI CHUẨN

* Service trả payload DTO trực tiếp; API Gateway bọc thành `ApiResponse<T>` hoặc `ApiResponse<PagedData<T>>`.
* Mã lỗi chuẩn `SCREAMING_SNAKE_CASE`: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `BUSINESS_RULE_VIOLATION`, `VAL_INVALID_INPUT`, `VAL_INVALID_SIGNATURE`, `UNAUTHORIZED`, `FORBIDDEN`.
* Enum/status trả ra API luôn PascalCase: `TransactionStatus.ToString()`, `WithdrawalStatus.ToString()`, `TopupStatus.ToString()`, và `WalletAccount.Status` được chuẩn hóa qua `StatusTextExtensions.ToApiStatus()` (`"ACTIVE"` → `"Active"`). Cấm `ToUpperInvariant()` trong response.
* Input enum (`action`, `adjustmentType`) parse qua `EnumExtensions.TryToEnum<T>()`, từ chối số nguyên thô và giá trị không định nghĩa.
* Thông báo lỗi 100% tiếng Anh, sentence case.
* Lỗi model-binding và MediatR validation dùng chung `application/problem+json` với `type`, `title`, `status`, `errorCode`, `detail`, `errors`, `traceId`, `timestamp`; `errors` nhóm thông báo tiếng Anh theo tên trường.
* Trường do server điền (`userId`, `adminUserId`, `clientIp`) không nhận từ JSON body; controller tự bơm từ header `x-user-id` hoặc connection.

---

## 3. LUỒNG CQRS

```
Controller (ISender)
   ↓
ValidationBehavior<TRequest,TResponse>
   ↓
IRequestHandler<TCommand|TQuery, TResponse>
   ↓
IWalletDbContext / IStripeCheckoutClient
```

* Query tuyệt đối read-only (`AsNoTracking`). Mọi thay đổi trạng thái đi qua Command handler; `GetMyWalletQueryHandler` chỉ điều phối `EnsureWalletCommand` khi ví chưa tồn tại.
* Command đụng số dư/audit bọc `BeginTransactionAsync`; Stripe settlement, hold và duyệt rút tiền dùng khóa dòng PostgreSQL `FOR UPDATE`.
