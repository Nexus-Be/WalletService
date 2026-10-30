# 01 — PHÂN TẦNG CLEAN ARCHITECTURE (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-30):** ✅ ĐÃ CHUẨN HÓA — 4 tầng Clean Architecture chuẩn hóa: Feature Co-location (`Services/Wallet`, `Services/Admin`), gỡ bỏ hoàn toàn MediatR và MassTransit. Giao tiếp đồng bộ 100% RESTful qua `WalletsController.cs` và `AdminWalletsController.cs`.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Api/Controllers/WalletsController.cs`, `Wallet.Application/Services/Wallet/WalletService.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

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
| `Wallet.Domain` | Lớp trung tâm: entity (`WalletAccount`, `WalletTransaction`...), enum, domain exception | ✅ (zero-dependency) | Tham chiếu project: 0; package: 0 |
| `Wallet.Application` | Phụ thuộc `Domain`; khai báo `IWalletDbContext`, Feature Services (`WalletService`, `AdminWalletService`), DTOs | ✅ | `Wallet.Application/Wallet.Application.csproj`, `Wallet.Application/Services/Wallet/WalletService.cs` |
| `Wallet.Infrastructure` | Phụ thuộc `Application` + `Domain`; `WalletDbContext`, configurations, migrations, VNPay/DirectPayment gateways | ✅ | `Wallet.Infrastructure/Wallet.Infrastructure.csproj`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs` |
| `Wallet.Api` | Host REST API; map `WalletsController`, `AdminWalletsController`, OpenTelemetry, Swagger | ✅ | `Wallet.Api/Controllers/WalletsController.cs`, `Wallet.Api/Program.cs` |

---

## 2. QUY CHUẨN CODE TỪNG TẦNG

| Tầng | Quy chuẩn | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Domain | Mọi biến động số dư phải đi qua method entity: `Deposit(amount)`, `Withdraw(amount)`, `Deduct(amount)`; cấm gán trực tiếp `wallet.Balance = ...` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:20,29,41` |
| Domain | Thực thể ví tên `WalletAccount` (KHÔNG có entity `Wallet`); enum loại giao dịch là `WalletTransactionType` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:7`, `Wallet.Domain/Enums/WalletTransactionType.cs` |
| Application | Feature Co-location: `Services/Wallet/` và `Services/Admin/` chứa Service, Requests, Responses | ✅ | `Wallet.Application/Services/Wallet/**`, `Wallet.Application/Services/Admin/**` |
| Application | Không sử dụng MediatR hoặc Generic Repository bọc ngoài EF Core | ✅ | Inject trực tiếp `IWalletDbContext` vào `WalletService` |
| Infrastructure | Triển khai `IWalletDbContext`, kết nối PostgreSQL qua Npgsql | ✅ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs` |

---

## 3. DANH MỤC INDEX THẬT TRONG MIGRATION

| Index | Bảng | Unique | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `IX_Wallets_UserId` | `Wallets` | ✅ | `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs:382-386` |
| `IX_WalletTransactions_WalletId` | `WalletTransactions` | ❌ | `.../20261024103202_InitialWalletSchema.cs:400` |
| `IX_WalletTopups_WalletId` | `WalletTopups` | ❌ | `.../20261024103202_InitialWalletSchema.cs:395` |
| `IX_WalletTopups_VnpTxnRef` | `WalletTopups` | ✅ | `.../20261024103202_InitialWalletSchema.cs:389` |
| `IX_WithdrawalRequests_WalletId` | `WithdrawalRequests` | ❌ | `.../20261024103202_InitialWalletSchema.cs:405` |
| `IX_VnpayIpnLogs_VnpTxnRef` | `VnpayIpnLogs` | ❌ | `.../20261024103202_InitialWalletSchema.cs:378` |
| `IX_DirectPaymentOrders_OrderId` | `DirectPaymentOrders` | ❌ | `.../20261024103202_InitialWalletSchema.cs:335` |
| `IX_DirectPaymentOrders_VnpTxnRef` | `DirectPaymentOrders` | ✅ | `.../20261024103202_InitialWalletSchema.cs:340` |

---

## 4. QUY CHUẨN CẤU TRÚC CHUẨN HÓA (SERVICE BLUEPRINT)
1. **Giao tiếp đồng bộ (RESTful)**:
   - `Wallet.Api/Controllers/`: Thin Controller, nhận request, gọi Feature Service trong Application, trả về `IActionResult`. Cấm logic nghiệp vụ trong controller.
   - `Wallet.Application/Services/`: Chứa Feature Service và Request/Response DTOs.
2. **Giao tiếp liên dịch vụ**:
   - Nhận yêu cầu thanh toán trực tiếp qua endpoint REST: `POST /api/v1/wallets/checkout/direct`.
3. **Quản lý Dependency Injection**:
   - `Wallet.Application/ApplicationDependencyInjection.cs`: Đăng ký `IWalletService`, `IAdminWalletService`.
   - `Wallet.Infrastructure/InfrastructureDependencyInjection.cs`: Đăng ký `WalletDbContext`, `IVnPayService`, `IDirectPaymentGateway`.
