# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (WALLET SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-10-31):** ✅ 100% RESTFUL + CLASSIC CQRS — 10 endpoint, 11 use case (8 Command, 3 Query; `EnsureWalletCommand` do `GetMyWalletQueryHandler` gọi nội bộ). Controller chỉ inject `ISender`. Bãi bỏ hoàn toàn gRPC, Protobuf, RabbitMQ và MassTransit.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Public/`, `Wallet.Api/Controllers/Internal/`, `Wallet.Application/Features/`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Khách hàng (`WalletsController` — `/api/v1/wallets`)
* `GET /api/v1/wallets/me` → `GetMyWalletQuery` → `GetMyWalletQueryHandler` (read-only; khi chưa có ví thì gửi `EnsureWalletCommand` → `EnsureWalletCommandHandler` tạo ví 0 đồng + `AuditLog` `WalletCreate`).
* `POST /api/v1/wallets/topups/vnpay` → `TopupVnPayCommand` → `TopupVnPayCommandHandler` (HTTP 201).
* `POST /api/v1/wallets/withdrawals` → `RequestWithdrawalCommand` → `RequestWithdrawalCommandHandler` (hiện luôn trả `BUSINESS_RULE_VIOLATION`).
* `POST /api/v1/wallets/transfers` → `TransferCommand` → `TransferCommandHandler`.
* `GET /api/v1/wallets/transactions` → `ListTransactionsQuery` → `ListTransactionsQueryHandler`.
* `POST /api/v1/wallets/vnpay/ipn` → `ProcessVnPayIpnCommand` → `ProcessVnPayIpnCommandHandler`.
* `GET /api/v1/wallets/vnpay/return` → `ProcessVnPayReturnQuery` → `ProcessVnPayReturnQueryHandler`.

### 1.2. Quản trị (`AdminWalletsController` — `/api/v1/admin/wallets`)
* `PATCH /api/v1/admin/wallets/withdrawals/{id:guid}` → `ProcessWithdrawalCommand` → `ProcessWithdrawalCommandHandler`.
* `POST /api/v1/admin/wallets/{walletId:guid}/adjustments` → `AdjustWalletBalanceCommand` → `AdjustWalletBalanceCommandHandler`.

### 1.3. Nội bộ (`InternalWalletsController` — `/api/v1/internal/wallets`)
* `POST /api/v1/internal/wallets/checkout/direct` → `CheckoutDirectCommand` → `CheckoutDirectCommandHandler` (idempotent theo `OrderId`).

---

## 2. PHONG BÌ DỮ LIỆU VÀ MÃ LỖI CHUẨN

* Service trả payload DTO trực tiếp; API Gateway bọc thành `ApiResponse<T>` hoặc `ApiResponse<PagedData<T>>`.
* Mã lỗi chuẩn `SCREAMING_SNAKE_CASE`: `WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `BUSINESS_RULE_VIOLATION`, `VAL_INVALID_INPUT`, `VAL_INVALID_ARGUMENT`, `VAL_INVALID_USER_ID`, `VAL_INVALID_ORDER_ID`, `VAL_INVALID_ACTION`, `VAL_INVALID_ADJUSTMENT_TYPE`, `UNAUTHORIZED`, `FORBIDDEN`.
* Enum/status trả ra API luôn PascalCase: `TransactionStatus.ToString()`, `WithdrawalStatus.ToString()`, `TopupStatus.ToString()`, và `WalletAccount.Status` được chuẩn hóa qua `StatusTextExtensions.ToApiStatus()` (`"ACTIVE"` → `"Active"`). Cấm `ToUpperInvariant()` trong response.
* Input enum (`action`, `adjustmentType`) parse qua `EnumExtensions.TryToEnum<T>()`, từ chối số nguyên thô và giá trị không định nghĩa.
* Thông báo lỗi 100% tiếng Anh, sentence case.
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
IWalletDbContext / IVnPayService / IDirectPaymentGateway
```

* Query tuyệt đối read-only (`AsNoTracking`). Mọi thay đổi trạng thái đi qua Command handler; `GetMyWalletQueryHandler` chỉ điều phối `EnsureWalletCommand` khi ví chưa tồn tại.
* Command đụng số dư/audit bọc `BeginTransactionAsync`; IPN và duyệt rút tiền khóa dòng bằng `LockTopupByReferenceAsync` / `LockWithdrawalRequestAsync` (PostgreSQL `FOR UPDATE`).
