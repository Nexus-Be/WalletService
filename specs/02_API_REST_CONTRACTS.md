# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (WALLET SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-11-10):** ✅ 100% RESTFUL + CLASSIC CQRS — 17 handlers (11 Command, 6 Query; `EnsureWalletCommand` do `GetMyWalletQueryHandler` gọi nội bộ). Controller chỉ inject `ISender`. Bãi bỏ hoàn toàn gRPC, Protobuf, RabbitMQ và MassTransit.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa.
> **Nguồn sự thật:** code (`Wallet.Api/Controllers/Public/`, `Wallet.Application/Features/`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Khách hàng (`WalletsController` — `/api/v1/wallets`)
* `GET /api/v1/wallets/me` → `GetMyWalletQuery` → `GetMyWalletQueryHandler` (read-only; khi chưa có ví thì gửi `EnsureWalletCommand` → `EnsureWalletCommandHandler` tạo ví 0 đồng + `AuditLog` `WalletCreate`; Swagger hiển thị `x-user-id` header parameter).
* `POST /api/v1/wallets/topups/vnpay` → `TopupVnPayCommand` → `TopupVnPayCommandHandler` (HTTP 201).
* `POST /api/v1/wallets/withdrawals` → `RequestWithdrawalCommand` → `RequestWithdrawalCommandHandler` (HTTP 201; tạo yêu cầu Pending và khóa số dư khả dụng; không khởi tạo chuyển khoản ngân hàng).
* `POST /api/v1/wallets/transfers` → `TransferCommand` → `TransferCommandHandler`.
* `GET /api/v1/wallets/transactions` → `ListTransactionsQuery` → `ListTransactionsQueryHandler`.
* `GET /api/v1/wallets/vnpay/ipn` (VNPay's method; `POST` form/JSON also accepted) → `ProcessVnPayIpnCommand` → `ProcessVnPayIpnCommandHandler`. Trả JSON `{"RspCode","Message"}` theo chuẩn VNPay (`00` `Confirm Success`, `02` `Order already confirmed`, `97` `Invalid signature`…); mỗi callback được ghi vào `VnpayIpnLogs`. Chi tiết: `docs/api/vnpay/ipn.md`.
* `GET /api/v1/wallets/vnpay/return` → `ProcessVnPayReturnQuery` → `ProcessVnPayReturnQueryHandler`.

### 1.2. Quản trị (`AdminWalletsController` — `/api/v1/admin/wallets`)
* `PATCH /api/v1/admin/wallets/withdrawals/{id:guid}` → `ProcessWithdrawalCommand` → `ProcessWithdrawalCommandHandler`.
* `POST /api/v1/admin/wallets/{walletId:guid}/adjustments` → `AdjustWalletBalanceCommand` → `AdjustWalletBalanceCommandHandler`.
* `GET /api/v1/admin/wallets/ledger` → `ListWalletLedgerQuery` → `ListWalletLedgerQueryHandler` (admin only; filtered, database-paginated transaction report).
* `GET /api/v1/admin/wallets/reconciliation` → `GetWalletReconciliationQuery` → `GetWalletReconciliationQueryHandler` (admin only; read-only wallet-balance snapshot report).

Withdrawal approval requires `referenceCode` (non-blank, maximum 100 characters) and records the manual bank-transfer reference in `GatewayReferenceId`; the transfer must already have occurred. Withdrawal rejection does not require the reference and releases locked funds. See `docs/api/admin-financial-operations.md`.

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
