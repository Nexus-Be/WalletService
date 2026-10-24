# WALLET SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE
## (CẨM NANG CẤU TRÚC THƯ MỤC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)

> **Thông tin dịch vụ**:
> - **Service Port**: `8008` (HTTP/1.1 REST & HTTP/2 gRPC)
> - **Database**: PostgreSQL (`wallet_db`), Port máy host: `5440`
> - **Nhiệm vụ chính**: Quản lý ví điện tử, số dư, nạp tiền (Topup VNPay), rút tiền (Withdrawal), chuyển tiền nội bộ, thanh toán đơn hàng Checkout.

---

## 1. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN

```
src/Services/Wallet/
│
├── Wallet.Domain/                                     # 1. TẦNG DOMAIN (POCO 100%, ZERO DEPENDENCY)
│   ├── Base/                                          # BaseEntity, AuditableEntity
│   ├── Entities/                                      # Wallet, WalletAccount, WalletTransaction, BankAccount...
│   ├── Enums/                                         # CurrencyCode, TransactionType, TransactionStatus...
│   └── Exceptions/                                    # DomainException, InsufficientBalanceException...
│
├── Wallet.Application/                                # 2. TẦNG APPLICATION (USE CASES & BUSINESS LOGIC)
│   ├── Common/                                        # Behaviors, Interfaces (IWalletDbContext)
│   ├── Consumers/                                     # MassTransit Event Consumers (StockReservedConsumer)
│   ├── DTOs/                                          # Data Transfer Objects
│   ├── Features/                                      # Commands & Queries (Deposit, Withdraw, Transfer...)
│   └── ApplicationDependencyInjection.cs              # Đăng ký AddApplicationServices()
│
├── Wallet.Infrastructure/                             # 3. TẦNG INFRASTRUCTURE (CÔNG NGHỆ & HẠ TẦNG CỤ THỂ)
│   ├── Persistence/                                   # Quản lý CSDL PostgreSQL & EF Core 8
│   │   ├── WalletDbContext.cs                         # Triển khai IWalletDbContext
│   │   ├── Configurations/                            # 8 Fluent API Configurations (*Configuration.cs)
│   │   └── Seeds/                                     # Dữ liệu hạt giống tài khoản mẫu
│   └── InfrastructureDependencyInjection.cs           # Đăng ký AddInfrastructureServices(configuration)
│
└── Wallet.Api/                                        # 4. TẦNG API / HOST (ENTRY POINT & MIDDLEWARE)
    ├── Middleware/                                    # MIDDLEWARE TOÀN CỤC CỦA SERVICE
    │   └── GlobalExceptionHandlerMiddleware.cs        # Bắt mọi lỗi ngoại lệ, format RFC 9110 ProblemDetails
    ├── Services/                                      # gRPC Server Service (WalletGrpcService.cs)
    ├── Contracts/                                     # shop.proto (Hợp đồng gRPC)
    └── Program.cs                                     # Khởi động Kestrel HTTP/1 & HTTP/2, gọi DI App & Infra
```

---

## 2. NGUYÊN TẮC BẤT BIẾN TẠI WALLET SERVICE

1. **TUYỆT ĐỐI KHÔNG ĐỂ SỐ DƯ ÂM (NEGATIVE BALANCE)**: Mọi thao tác rút, chuyển hoặc trừ tiền thanh toán bắt buộc kiểm tra `Balance >= Amount`. Thiếu tiền ném `InsufficientBalanceException`.
2. **TRANSACTIONAL INTEGRITY**: Lưu lịch sử giao dịch `WalletTransaction` đồng thời khi thay đổi số dư ví.
3. **GLOBAL MIDDLEWARE**: `GlobalExceptionHandlerMiddleware` bắt mọi ngoại lệ và chuẩn hóa ProblemDetails.

---

## 3. CÁCH CHẠY VÀ KIỂM THỬ ĐỘC LẬP SERVICE

```bash
# 1. Bật database của Wallet Service (Port 5440):
cd src/Services/Wallet
docker compose up -d wallet-db

# 2. Tạo & Cập nhật migration:
./.tools/dotnet-ef migrations add InitialWalletSchema --project Wallet.Infrastructure --startup-project Wallet.Api
./.tools/dotnet-ef database update --project Wallet.Infrastructure --startup-project Wallet.Api

# 3. Chạy service:
dotnet run --project Wallet.Api
```
