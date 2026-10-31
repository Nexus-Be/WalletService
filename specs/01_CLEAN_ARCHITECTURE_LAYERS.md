# 01 — PHÂN TẦNG CLEAN ARCHITECTURE (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-31):** ✅ ĐÃ CHUẨN HÓA — 4 tầng Clean Architecture, **classic CQRS qua MediatR** (`Wallet.Application/Features/{Area}/Commands|Queries/{UseCase}/`). Wallet là service duy nhất được dùng MediatR trong toàn hệ thống. Giao tiếp đồng bộ 100% RESTful qua `WalletsController`, `AdminWalletsController`, `InternalWalletsController`.
> **ĐÓNG BĂNG:** chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa. Cấm tạo `Services/{Feature}/`, cấm tách controller theo tài nguyên, cấm gỡ MediatR.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Api/Controllers/Public/WalletsController.cs`, `Wallet.Application/Features/Wallets/Queries/GetMyWallet/GetMyWalletQueryHandler.cs`).
> **Cập nhật lần cuối:** 2026-10-31.

---

## 1. PHÂN TẦNG VÀ CHIỀU PHỤ THUỘC (DEPENDENCY RULE)

```
[ Wallet.Api ] ─────────────┐
                            │
                            ▼
             [ Wallet.Application ] ──▶ [ Wallet.Domain ]
                            ▲
                            │
[ Wallet.Infrastructure ] ──┘
```

| Tầng | Vai trò | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `Wallet.Domain` | Lớp trung tâm: entity (`WalletAccount`, `WalletTransaction`, `WalletTopup`, `WithdrawalRequest`), enum, domain exception | ✅ (zero-dependency) | Tham chiếu project: 0; package: 0 |
| `Wallet.Application` | Phụ thuộc `Domain`; khai báo `IWalletDbContext`, Command/Query + Handler theo CQRS, `ValidationBehavior`, response records | ✅ | `Wallet.Application/Wallet.Application.csproj` (MediatR 12.4.1), `Wallet.Application/Features/` |
| `Wallet.Infrastructure` | Phụ thuộc `Application` + `Domain`; `WalletDbContext`, configurations, VNPay/DirectPayment gateways | ✅ | `Wallet.Infrastructure/Wallet.Infrastructure.csproj`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs` |
| `Wallet.Api` | Host REST API; thin controllers inject `ISender`, OpenTelemetry, Swagger | ✅ | `Wallet.Api/Controllers/Public/WalletsController.cs`, `Wallet.Api/Program.cs` |

---

## 2. QUY CHUẨN CODE TỪNG TẦNG

| Tầng | Quy chuẩn | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Domain | Mọi biến động số dư phải đi qua method entity: `Deposit(amount)`, `Withdraw(amount)`, `Deduct(amount)`; cấm gán trực tiếp `wallet.Balance = ...` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:20,29,41` |
| Domain | Thực thể ví tên `WalletAccount` (KHÔNG có entity `Wallet`); enum loại giao dịch là `WalletTransactionType` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:7`, `Wallet.Domain/Enums/WalletTransactionType.cs` |
| Application | Classic CQRS: mỗi use case một folder dưới `Features/{Area}/Commands/` hoặc `Features/{Area}/Queries/`, chứa Command/Query, Handler, response record | ✅ | `Wallet.Application/Features/**` |
| Application | Cấm tạo `IWalletService`/`IAdminWalletService` hoặc bất kỳ `I{Feature}Service` facade; cấm Generic Repository bọc ngoài EF Core | ✅ | Handler inject trực tiếp `IWalletDbContext` |
| Application | Mọi Command/Query đi qua `ValidationBehavior` (DataAnnotations); lỗi trả `VAL_INVALID_INPUT` | ✅ | `Wallet.Application/Common/Behaviors/ValidationBehavior.cs` |
| Api | Controller chỉ inject `ISender` và `ILogger`; dựng Command/Query, bơm `UserId`/`AdminUserId`/`ClientIp` qua `command with { ... }` | ✅ | `Wallet.Api/Controllers/Public/WalletsController.cs` |
| Infrastructure | Triển khai `IWalletDbContext`, kết nối PostgreSQL qua Npgsql | ✅ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs` |

---

## 3. DANH MỤC INDEX THẬT TRONG MIGRATION

| Index | Bảng | Unique | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `IX_Wallets_UserId` | `Wallets` | ✅ | `Wallet.Infrastructure/Persistence/Configurations/WalletAccountConfiguration.cs` |
| `IX_WalletTransactions_WalletId` | `WalletTransactions` | ❌ | `WalletTransactionConfiguration.cs` |
| `IX_WalletTopups_WalletId` | `WalletTopups` | ❌ | `WalletTopupConfiguration.cs` |
| `IX_WalletTopups_VnpTxnRef` | `WalletTopups` | ✅ | `WalletTopupConfiguration.cs` |
| `IX_WithdrawalRequests_WalletId` | `WithdrawalRequests` | ❌ | `WithdrawalRequestConfiguration.cs` |
| `IX_DirectPaymentOrders_OrderId` | `DirectPaymentOrders` | ❌ | `DirectPaymentOrderConfiguration.cs` |

> Migration hiện không nằm trong repo (`Wallet.Infrastructure/Migrations/` trống). Mọi thay đổi schema cần phê duyệt riêng.

---

## 4. QUY CHUẨN CẤU TRÚC CHUẨN HÓA (SERVICE BLUEPRINT)
1. **Giao tiếp đồng bộ (RESTful)**:
   - `Wallet.Api/Controllers/`: thin controller, nhận request, gọi `ISender.Send(...)`, trả `IActionResult`. Cấm logic nghiệp vụ và truy vấn DB trong controller.
   - `Wallet.Application/Features/`: Command/Query + Handler cho từng endpoint.
2. **Giao tiếp liên dịch vụ**:
   - Nhận yêu cầu thanh toán trực tiếp qua endpoint nội bộ: `POST /api/v1/internal/wallets/checkout/direct`.
3. **Quản lý Dependency Injection**:
   - `Wallet.Application/DependencyInjection.cs`: `AddMediatR` + `AddOpenBehavior(ValidationBehavior<,>)`.
   - `Wallet.Infrastructure/DependencyInjection.cs`: đăng ký `WalletDbContext`, `IVnPayService`, `IDirectPaymentGateway`.
