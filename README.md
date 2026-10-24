# WALLET SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — 5/5 rpc gRPC đã implement (`Wallet.Api/Services/WalletGrpcService.cs:26,46,66,94,122`) và 5/5 handler CQRS thật (`Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:22`, `.../Withdraw/WithdrawCommand.cs:24`, `.../Transfer/TransferCommand.cs:25`, `.../Queries/GetWallet/GetWalletQuery.cs:12`, `.../Queries/ListTransactions/ListTransactionsQuery.cs:11`), OpenTelemetry đã bật (`Wallet.Api/Extensions/ObservabilityExtensions.cs:8`). Chưa có: **0 test project**, **24 chuỗi thông báo lỗi tiếng Việt** trong ngữ cảnh lỗi, **không có authentication** (tin header `x-user-id` không kiểm chứng), **6/8 bảng không có đường ghi nào** và consumer `StockReservedConsumer` **chưa từng chạy** vì không publisher nào phát `IStockReservedEvent`.
> **Nguồn sự thật:** code (`Wallet.Api/Program.cs:47`, `Wallet.Api/Services/WalletGrpcService.cs:26-160`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs:14-21`, `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs`).
> **Cập nhật lần cuối:** 2026-10-24.

## (CẨM NANG CẤU TRÚC THƯ MỤC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)

> **Thông tin dịch vụ**:
> - **Service Port**: `8008` chỉ đúng khi chạy bằng container (`Dockerfile:3-4`, `docker-compose.yml:37`). Chạy `dotnet run` bằng `launchSettings.json` lại bind `http://localhost:5096` / `https://localhost:7218` (`Wallet.Api/Properties/launchSettings.json:17,27`) — **lệch chuẩn** ❌.
> - **Database**: PostgreSQL (`wallet_db`), Port máy host: `5440` (`docker-compose.yml:19`).
> - **Nhiệm vụ chính**: Quản lý số dư ví, ghi nhật ký biến động số dư, chuyển tiền nội bộ, thanh toán đơn trong Checkout Saga.
> - **Nhiệm vụ CHƯA triển khai**: Nạp tiền qua VNPay (không có URL thanh toán, không có IPN handler), rút tiền qua ngân hàng (không tạo `WithdrawalRequest`), ghi `AuditLog`.

---

## 1. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN

```
src/Services/Wallet/
│
├── Wallet.Domain/                                     # 1. TẦNG DOMAIN (POCO thuần + 1 package MediatR.Contracts 2.0.1)
│   ├── Base/                                          # BaseEntity, AuditableEntity
│   ├── Entities/                                      # WalletAccount, WalletTransaction, WalletTopup, DirectPaymentOrder,
│   │                                                  # WithdrawalRequest, BankAccount, VnpayIpnLog, AuditLog
│   ├── Enums/                                         # CurrencyCode, WalletTransactionType, TransactionStatus, TopupStatus,
│   │                                                  # WithdrawalStatus, IpnProcessingStatus, AuditAction
│   ├── Events/                                        # WalletEvents.cs (3 interface, không có class implementation)
│   └── Exceptions/                                    # DomainException, InsufficientBalanceException
│
├── Wallet.Application/                                # 2. TẦNG APPLICATION (USE CASES & BUSINESS LOGIC)
│   ├── Common/                                        # Behaviors (ValidationBehavior), Interfaces (IWalletDbContext)
│   ├── Consumers/                                     # MassTransit Consumer (StockReservedConsumer)
│   ├── DTOs/                                          # Data Transfer Objects
│   ├── Features/                                      # Commands & Queries (Deposit, Withdraw, Transfer, GetWallet, ListTransactions)
│   └── ApplicationDependencyInjection.cs              # Đăng ký AddApplicationServices()
│
├── Wallet.Infrastructure/                             # 3. TẦNG INFRASTRUCTURE (CÔNG NGHỆ & HẠ TẦNG CỤ THỂ)
│   ├── Persistence/                                   # Quản lý CSDL PostgreSQL & EF Core 8
│   │   ├── WalletDbContext.cs                         # Kế thừa DbContext, triển khai IWalletDbContext
│   │   ├── Configurations/                            # 8 Fluent API Configurations (*Configuration.cs)
│   │   └── Seeds/                                     # WalletSeedData.cs (5 ví + 1 tài khoản NH + 1 giao dịch mẫu)
│   ├── Migrations/                                    # 20261024103202_InitialWalletSchema.cs (11 bảng)
│   └── InfrastructureDependencyInjection.cs           # Đăng ký AddInfrastructureServices(configuration)
│
└── Wallet.Api/                                        # 4. TẦNG API / HOST (ENTRY POINT & MIDDLEWARE)
    ├── Middleware/                                    # GlobalExceptionHandlerMiddleware.cs (chỉ áp cho pipeline HTTP)
    ├── Services/                                      # WalletGrpcService.cs (5 rpc)
    ├── Contracts/                                     # shop.proto (bản sao byte-identical với bản của Log service)
    └── Program.cs                                     # Khởi động Kestrel HTTP/1 & HTTP/2, gọi DI App & Infra
```

> **Lưu ý tên entity**: KHÔNG có entity nào tên `Wallet`. Thực thể ví là `WalletAccount` (`Wallet.Domain/Entities/WalletAccount.cs`). Enum loại giao dịch là `WalletTransactionType` (không phải `TransactionType`).

---

## 2. NGUYÊN TẮC BẤT BIẾN TẠI WALLET SERVICE

| # | Nguyên tắc | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---:|:---|
| 1 | **Không để số dư âm**: mọi thao tác trừ tiền kiểm tra `Balance >= Amount`, thiếu tiền ném `InsufficientBalanceException` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:20-48` (Deposit/Withdraw/Deduct) |
| 2 | **Ghi nhật ký đồng thời**: mỗi lần đổi số dư tạo một `WalletTransaction` cùng transaction DB | ⚠️ | Có tạo transaction: `Wallet.Application/Features/Commands/Deposit/DepositCommand.cs:53-66`; nhưng KHÔNG dùng `BeginTransaction` tường minh — chỉ dựa vào một lần `SaveChangesAsync` |
| 3 | **Global middleware chuẩn hóa lỗi**: `GlobalExceptionHandlerMiddleware` trả `application/problem+json` + errorCode SCREAMING_SNAKE | ⚠️ | `Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs:35-73`; đăng ký `Wallet.Api/Program.cs:35`. **Chỉ áp cho HTTP** (`/`, `/health`), KHÔNG áp cho lời gọi gRPC ⇒ lỗi gRPC trả về thô |
| 4 | **Thông báo lỗi 100% English, sentence case** (Nguyên tắc 5 của `AGENTS.md`) | ❌ | 24 chuỗi tiếng Việt: `Wallet.Api/Services/WalletGrpcService.cs:31,51,61,71,89,99,117`; `Wallet.Domain/Entities/WalletAccount.cs:23,32,44`; `Wallet.Application/Consumers/StockReservedConsumer.cs:32,49,93`; `Wallet.Domain/Exceptions/DomainException.cs:25,31`; `DepositCommand.cs:17,18`; `WithdrawCommand.cs:18,19,20`; `TransferCommand.cs:18,19,20,21` |

---

## 3. CÁCH CHẠY VÀ KIỂM THỬ ĐỘC LẬP SERVICE

```bash
# 1. Bật database của Wallet Service (Port 5440):
cd src/Services/Wallet
docker compose up -d wallet-db

# 2. Migration: repo ĐÃ có sẵn migration InitialWalletSchema, chỉ cần áp dụng
#    (`dotnet-ef` KHÔNG được vendor trong repo — dùng dotnet ef toàn cục/cục bộ)
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api

# 3. Chạy service (lưu ý: launchSettings bind 5096/7218, KHÔNG phải 8008):
dotnet run --project Wallet.Api

# 3'. Muốn đúng port 8008 thì chạy qua container:
docker compose up -d wallet-service
```

> `appsettings.json` khai báo connection string trỏ `localhost:5440` (`Wallet.Api/appsettings.json:10`, `docker-compose.yml:19`) và có chứa mật khẩu thật — xem mục 4.

---

## 4. TRẠNG THÁI TRIỂN KHAI THEO TÍNH NĂNG

| Tính năng | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|
| gRPC server `WalletApi` (5 rpc) | ✅ | `Wallet.Api/Services/WalletGrpcService.cs:26,46,66,94,122`; `Wallet.Api/Program.cs:47` |
| CQRS/MediatR + FluentValidation pipeline | ✅ | `Wallet.Application/ApplicationDependencyInjection.cs:17-21`; `Wallet.Application/Common/Behaviors/ValidationBehavior.cs:6,30` |
| OpenTelemetry (traces + OTLP exporter) | ✅ | `Wallet.Api/Extensions/ObservabilityExtensions.cs:8-25`; `Wallet.Api/Program.cs:12` |
| Serilog | ❌ | `grep -r "Serilog"` trên toàn repo = 0 kết quả |
| Authentication / Authorization | ❌ | không có `AddAuthentication`/`UseAuthentication`/`JwtBearer` trong repo; `WalletGrpcService.cs:28,48,68,96,124` tin header `x-user-id` |
| Repository pattern | ✅ (đúng luật: KHÔNG dùng) | Application dùng trực tiếp `IWalletDbContext` — `Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6` |
| EF Core + migration | ✅ | `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs` (11 bảng) |
| MassTransit + Outbox | ⚠️ | Cấu hình `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:34-40`; nhưng không có retry/DLQ, consumer chưa từng nhận message |
| Nạp tiền VNPay (tạo URL + IPN) | ❌ | không có endpoint/handler nào dùng `WalletTopups`/`VnpayIpnLogs` (grep tên DbSet ngoài DbContext = 0) |
| Rút tiền qua ngân hàng | ❌ | `WithdrawCommandHandler` chỉ trừ số dư, `bankAccount` chỉ nhét vào `Note` (`WithdrawCommand.cs:55`), không tạo `WithdrawalRequest` |
| Ghi `AuditLog` | ❌ | grep `AuditLogs` ngoài `WalletDbContext.cs:21`/`IWalletDbContext`/migration = 0 |
| Kiểm thử tự động | ❌ | 0 test project, 0 `[Fact]`, không có `Wallet.Tests`; CI `.github/workflows/ci-cd.yml` chỉ build/push Docker image |
| Cấu hình port local khớp chuẩn `8008` | ❌ | `Wallet.Api/Properties/launchSettings.json:17,27` = `5096`/`7218` |
| Bí mật không nằm trong repo | ❌ | `Wallet.Api/appsettings.json` được **track thật** và chứa mật khẩu DB/RabbitMQ (`:10`,`:16`) |
