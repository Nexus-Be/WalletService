# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (WALLET SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-10-31):** ✅ 100% RESTFUL + CLASSIC CQRS — 15 use case (11 Command, 4 Query; `EnsureWalletCommand` do `GetMyWalletQueryHandler` gọi nội bộ). Controller chỉ inject `ISender`. Bãi bỏ hoàn toàn gRPC, Protobuf, RabbitMQ và MassTransit.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Public/`, `Wallet.Application/Features/`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Khách hàng (`WalletsController` — `/api/v1/wallets`)
* `GET /api/v1/wallets/me` → `GetMyWalletQuery` → `GetMyWalletQueryHandler` (read-only; khi chưa có ví thì gửi `EnsureWalletCommand` → `EnsureWalletCommandHandler` tạo ví 0 đồng + `AuditLog` `WalletCreate`; Swagger hiển thị `x-user-id` header parameter).
* `POST /api/v1/wallets/topups/stripe` → `TopupStripeCommand` → `TopupStripeCommandHandler` (HTTP 201; cần `Idempotency-Key`).
* `GET /api/v1/wallets/topups/{id:guid}` → `GetTopupStatusQuery` → `GetTopupStatusQueryHandler` (chỉ chủ ví).
* `POST /api/v1/wallets/withdrawals` → `RequestWithdrawalCommand` → `RequestWithdrawalCommandHandler` (HTTP 201; tạo yêu cầu Pending và khóa số dư khả dụng; không khởi tạo chuyển khoản ngân hàng).
* `POST /api/v1/wallets/transfers` → `TransferCommand` → `TransferCommandHandler`.
* `GET /api/v1/wallets/transactions` → `ListTransactionsQuery` → `ListTransactionsQueryHandler`.
* `POST /api/v1/wallets/stripe/webhooks` → `ProcessStripeWebhookCommand` → `ProcessStripeWebhookCommandHandler` (anonymous; xác minh `Stripe-Signature`, body nguyên bản).

### 1.2. Quản trị (`AdminWalletsController` — `/api/v1/admin/wallets`)
* `PATCH /api/v1/admin/wallets/withdrawals/{id:guid}` → `ProcessWithdrawalCommand` → `ProcessWithdrawalCommandHandler`.
* `POST /api/v1/admin/wallets/{userId:guid}/adjustments` → `AdjustWalletBalanceCommand` → `AdjustWalletBalanceCommandHandler` (target identified by wallet owner's user ID).

### 1.3. Thanh toán trực tiếp (`WalletsController` — `/api/v1/wallets/checkout/direct`)
* `POST /api/v1/wallets/checkout/direct` → `CheckoutDirectCommand` → `CheckoutDirectCommandHandler` (idempotent theo `OrderId`). Internal API tạm khóa nên route dùng tiền tố công khai.
* `GET /api/v1/wallets/checkout/direct/{orderId}` → `GetDirectPaymentStatusQuery` → `GetDirectPaymentStatusQueryHandler` (`[RequireRole]`; chỉ chủ đơn, admin/staff đọc được mọi đơn; 404 `PAYMENT_NOT_FOUND`). Trả `status` (`Pending`/`Success`/`Failed`) và `gatewayTransactionNo`. Chi tiết: `docs/api/checkout/direct-status.md`.

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
IWalletDbContext / IVnPayService
```

* Query tuyệt đối read-only (`AsNoTracking`). Mọi thay đổi trạng thái đi qua Command handler; `GetMyWalletQueryHandler` chỉ điều phối `EnsureWalletCommand` khi ví chưa tồn tại.
* Command đụng số dư/audit bọc `BeginTransactionAsync`; IPN và duyệt rút tiền khóa dòng bằng `LockTopupByReferenceAsync` / `LockWithdrawalRequestAsync` (PostgreSQL `FOR UPDATE`).
