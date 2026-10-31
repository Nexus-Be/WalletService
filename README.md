# WALLET SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE

> **Trạng thái triển khai (2026-10-31):** ✅ CLASSIC CQRS — 11 use case (8 Command, 3 Query) trong `Wallet.Application/Features/`, 3 thin controller inject `ISender`, 53 test pass. Không còn gRPC, MassTransit, consumer hay Outbox/Inbox.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa. Cấm tạo `Wallet.Application/Services/{Feature}/`, cấm tách controller theo tài nguyên, cấm gỡ MediatR.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Application/Features/`, `Wallet.Api/Controllers/`, `Wallet.Tests/`).
> **Cập nhật lần cuối:** 2026-10-31.

> **Thông tin dịch vụ**
> - **Port**: `8008` (HTTP REST).
> - **Database**: PostgreSQL `wallet_db`, host port `5440`.
> - **Nhiệm vụ**: số dư ví, nạp VNPay, chuyển tiền nội bộ, thanh toán đơn trực tiếp, duyệt rút tiền, audit log.
> - **Ngoại lệ kiến trúc**: Wallet là service duy nhất dùng MediatR/CQRS trong hệ thống Nexus.

---

## 1. PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC

```
src/Services/Wallet/
├── Wallet.Domain/                      # Entity, enum, domain exception (zero-dependency)
│   ├── Base/                           # BaseEntity, AuditableEntity, ISoftDelete
│   ├── Entities/                       # WalletAccount, WalletTransaction, WalletTopup,
│   │                                   # WithdrawalRequest, BankAccount, VnpayIpnLog, AuditLog
│   ├── Enums/                          # CurrencyCode, WalletTransactionType, TransactionStatus,
│   │                                   # TopupStatus, WithdrawalStatus, AuditAction
│   └── Exceptions/                     # DomainException và các exception nghiệp vụ
│
├── Wallet.Application/                 # CQRS use cases
│   ├── Common/
│   │   ├── Behaviors/                  # ValidationBehavior (DataAnnotations)
│   │   ├── Exceptions/                 # RequestValidationException (VAL_INVALID_INPUT)
│   │   └── Extensions/                 # AddAuditLog, EnumExtensions
│   ├── Features/
│   │   ├── Wallets/Queries/GetMyWallet/
│   │   ├── Wallets/Queries/ListTransactions/
│   │   ├── Topups/Commands/TopupVnPay/
│   │   ├── Withdrawals/Commands/RequestWithdrawal/
│   │   ├── Transfers/Commands/Transfer/
│   │   ├── VnPay/Commands/ProcessVnPayIpn/
│   │   ├── VnPay/Queries/ProcessVnPayReturn/
│   │   ├── Checkout/Commands/CheckoutDirect/
│   │   └── AdminWallets/Commands/{ProcessWithdrawal,AdjustWalletBalance}/
│   ├── Interfaces/                     # IWalletDbContext, IVnPayService, IDirectPaymentGateway
│   └── DependencyInjection.cs          # AddMediatR + AddOpenBehavior
│
├── Wallet.Infrastructure/              # EF Core, PostgreSQL, VNPay/DirectPayment gateways
│   ├── Persistence/                    # WalletDbContext, Configurations, Seeds
│   └── DependencyInjection.cs
│
├── Wallet.Api/                         # Host REST API
│   ├── Controllers/Public/             # WalletsController, AdminWalletsController
│   ├── Controllers/Internal/           # InternalWalletsController
│   ├── Extensions/                     # HttpContext, Observability, ServiceDefaults
│   ├── Filters/                        # RequireRoleAttribute
│   ├── Middleware/                     # GlobalExceptionHandler, InternalServiceAuth
│   └── Program.cs
│
└── scripts/verify-wallet-architecture.sh   # Chặn agent phá kiến trúc CQRS
```

> **Tên entity**: ví là `WalletAccount` (không có entity `Wallet`); loại giao dịch là `WalletTransactionType`.

---

## 2. NGUYÊN TẮC BẤT BIẾN

| # | Nguyên tắc | Bằng chứng |
|:--|:---|:---|
| 1 | Không cho số dư âm: mọi thao tác trừ tiền kiểm tra `AvailableBalance >= Amount` | `Wallet.Domain/Entities/WalletAccount.cs` |
| 2 | Mỗi lần đổi số dư tạo một `WalletTransaction` cùng transaction DB | `Features/Transfers/Commands/Transfer/TransferCommandHandler.cs` |
| 3 | Mỗi endpoint là một Command/Query + Handler, không có service facade | `Wallet.Application/Features/**` |
| 4 | Controller chỉ inject `ISender`, không query DB | `Wallet.Api/Controllers/Public/WalletsController.cs` |
| 5 | Thao tác nhạy cảm ghi `AuditLog` qua `AddAuditLog(...)` | `Wallet.Application/Common/Extensions/AuditLogExtensions.cs` |
| 6 | Lỗi trả `application/problem+json` với mã `SCREAMING_SNAKE_CASE` | `Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs` |

---

## 3. CÁCH CHẠY VÀ KIỂM THỬ

```bash
cd src/Services/Wallet

# 0. Kiểm tra kiến trúc trước khi sửa
./scripts/verify-wallet-architecture.sh

# 1. Bật database (PostgreSQL, host port 5440)
docker compose up -d wallet-db

# 2. Build và test
dotnet build Wallet.sln -c Release
dotnet test  Wallet.sln -c Release

# 3. Chạy service ở cổng 8008
dotnet run --project Wallet.Api

# 4. Kiểm tra
curl http://localhost:8008/health
```

`appsettings.json` và biến môi trường cung cấp `ConnectionStrings:DefaultConnection` hoặc `WALLET_CONNECTION_STRING`. Không commit secret.

---

## 4. TRẠNG THÁI TÍNH NĂNG

| Tính năng | Trạng thái | Use case |
|:---|:---:|:---|
| Xem ví và số dư | ✅ | `GetMyWalletQuery` |
| Lịch sử giao dịch có phân trang | ✅ | `ListTransactionsQuery` |
| Nạp tiền VNPay | ✅ | `TopupVnPayCommand` |
| IPN VNPay | ✅ | `ProcessVnPayIpnCommand` |
| Return callback VNPay | ✅ | `ProcessVnPayReturnQuery` |
| Chuyển tiền nội bộ | ✅ | `TransferCommand` |
| Thanh toán đơn trực tiếp (internal) | ✅ | `CheckoutDirectCommand` |
| Duyệt / từ chối rút tiền | ✅ | `ProcessWithdrawalCommand` |
| Điều chỉnh số dư thủ công | ✅ | `AdjustWalletBalanceCommand` |
| Tạo yêu cầu rút tiền cho người dùng | ⚠️ Vô hiệu | `RequestWithdrawalCommand` luôn trả `BUSINESS_RULE_VIOLATION` |
| Quản lý tài khoản ngân hàng | ⚠️ Chỉ có entity | chưa có API |
| gRPC / MassTransit / Saga / Outbox | 🛑 Đã xóa | không còn trong code |
