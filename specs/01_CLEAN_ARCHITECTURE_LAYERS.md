# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (WALLET SERVICE)

> **Trạng thái triển khai (2026-10-29):** ✅ ĐÃ CHUẨN HÓA — 4 tầng Clean Architecture chuẩn hóa: Inbound Consumers được phân vùng rõ ràng theo nguồn phát tại `Wallet.Application/Consumers/` (`Commerce/` và `User/`), đóng gói DI đăng ký MassTransit qua `ConsumerExtensions.cs`, giao tiếp đồng bộ 100% RESTful qua `WalletsController.cs`.
> **Nguồn sự thật:** code (`Wallet.sln`, `Wallet.Application/Consumers/`, `Wallet.Infrastructure/InfrastructureDependencyInjection.cs`, `Wallet.Api/Controllers/WalletsController.cs`).
> **Cập nhật lần cuối:** 2026-10-29.

> **Lệch format so với 6/7 repo backend còn lại:** Wallet và Log dùng `ApplicationDependencyInjection.cs` / `InfrastructureDependencyInjection.cs` (Auction, Catalog, Commerce, Fulfillment, Notification, User dùng `DependencyInjection.cs`); interface DbContext đặt tên **`IWalletDbContext`** trong `Wallet.Application/Common/Interfaces/`; `Wallet.Domain` có 2 khái niệm base trùng: `Wallet.Domain/Base/BaseEntity.cs:3` (`BaseEntity<TKey>`) và `Wallet.Domain/Common/BaseEntity.cs:3` (`BaseEntity : BaseEntity<Guid>`); 3/4 `.csproj` có BOM UTF-8 (`Wallet.Application`, `Wallet.Domain`, `Wallet.Infrastructure`), riêng `Wallet.Api/Wallet.Api.csproj` không có BOM và thụt lề 8 space thay vì 4.

Solution `Wallet.sln` bao gồm 4 project tương ứng với 4 tầng kiến trúc sạch (Clean Architecture):

```
Wallet.sln
├── Wallet.Domain/           # Lõi thực thể (WalletAccount, WalletTransaction, BankAccount, Domain Events, Exceptions)
├── Wallet.Application/      # Use cases CQRS (Commands, Queries, DTOs, Event Consumers, FluentValidation)
├── Wallet.Infrastructure/   # EF Core DbContext, PostgreSQL mappings, MassTransit Outbox Pattern, Migrations
└── Wallet.Api/              # Điểm khởi chạy gRPC Server & Web API (Program.cs, DI setup, OpenTelemetry)
```

---

## 1. NGUYÊN TẮC LUỒNG PHỤ THUỘC (DEPENDENCY RULE)

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
| `Wallet.Domain` | Lớp trung tâm: entity, enum, domain event, exception nghiệp vụ | ✅ (không thuần zero-dependency) | Tham chiếu project: **không có**; nhưng có package `MediatR.Contracts 2.0.1` (`Wallet.Domain/Wallet.Domain.csproj:10`) **không được dùng ở bất kỳ file `.cs` nào** (grep `MediatR` trong `Wallet.Domain/` = 0) |
| `Wallet.Application` | Phụ thuộc `Domain`; khai báo `IWalletDbContext`, Commands/Queries, DTO, Consumer | ✅ | `Wallet.Application/Wallet.Application.csproj:4`, `Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6`, `Wallet.Application/ApplicationDependencyInjection.cs:17-21` |
| `Wallet.Infrastructure` | Phụ thuộc `Application` + `Domain`; `WalletDbContext`, configurations, migrations, MassTransit Outbox | ✅ | `Wallet.Infrastructure/Wallet.Infrastructure.csproj:4-5`, `Wallet.Infrastructure/Persistence/WalletDbContext.cs:8` |
| `Wallet.Api` | Host gRPC + HTTP; map `WalletGrpcService`, OpenTelemetry, Swagger | ✅ | `Wallet.Api/Wallet.Api.csproj:30-31`, `Wallet.Api/Program.cs:19,23,37-42,47` |

---

## 2. QUY CHUẨN CODE TỪNG TẦNG

| Tầng | Quy chuẩn | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Domain | Mọi biến động số dư phải đi qua method entity: `Deposit(amount)`, `Withdraw(amount)`, `Deduct(amount)`; cấm gán trực tiếp `wallet.Balance = ...` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:20,29,41`; handler chỉ gọi method (`Wallet.Application/Features/Commands/Transfer/TransferCommand.cs:63,70`) |
| Domain | Thực thể ví tên `WalletAccount` (KHÔNG có entity `Wallet`); enum loại giao dịch là `WalletTransactionType` | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:7`, `Wallet.Domain/Enums/WalletTransactionType.cs` |
| Application | Tách `Commands/` và `Queries/` qua MediatR | ✅ | `Wallet.Application/Features/Commands/**`, `Wallet.Application/Features/Queries/**` |
| Application | `ValidationBehavior` tự động chạy FluentValidation trước handler | ✅ | `Wallet.Application/Common/Behaviors/ValidationBehavior.cs:6,30`; đăng ký pipeline `ApplicationDependencyInjection.cs:20` |
| Application | Query phân trang luôn `.AsNoTracking()` + `Skip/Take`, tối đa 100 item/trang | ✅ | `Wallet.Application/Features/Queries/ListTransactions/ListTransactionsQuery.cs:26` (AsNoTracking), `:23` (cap 100) |
| Infrastructure | Index trên trường tìm kiếm thường xuyên: `UserId` unique trên bảng `Wallets` **và** `(UserId, CreatedAt)` trên `WalletTransactions` | ⚠️ chỉ phần 1 đúng | Có `IX_Wallets_UserId` UNIQUE (`Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs:382-386`); **KHÔNG có** index `(UserId, CreatedAt)` trên `WalletTransactions` — chỉ có `IX_WalletTransactions_WalletId` (`:400`) |
| Infrastructure | Transactional Outbox Pattern để bảo toàn tin nhắn RabbitMQ | ⚠️ | Cấu hình `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:36-40` (`AddEntityFrameworkOutbox` + `UseBusOutbox`) và map 3 bảng (`Wallet.Infrastructure/Persistence/WalletDbContext.cs:31-33`); **không có retry/DLQ**, **không có test** chứng minh outbox ghi bảng |

---

## 3. DANH MỤC INDEX THẬT TRONG MIGRATION (KHÔNG SUY DIỄN)

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
   - `Wallet.Api/Controllers/`: Thin Dispatcher Controller, chỉ nhận request, gọi Service Interface trong Application, trả về `IActionResult`. Tuyệt đối cấm logic nghiệp vụ, cấm map anonymous object, cấm khai báo class DTO trong file controller.
   - `Wallet.Application/`: Chứa các Request/Response DTOs dùng chung cho Controller, Query/Command handlers.
2. **Giao tiếp bất đồng bộ (RabbitMQ MassTransit)**:
   - `Wallet.Application/Contracts/`: Chứa các Integration Events (ví dụ: `PaymentCompletedIntegrationEvent`, `PaymentFailedIntegrationEvent`).
   - `Wallet.Infrastructure/Consumers/`: Chứa các MassTransit Consumer xử lý sự kiện trừ tiền/hoàn tiền.
3. **Quản lý Dependency Injection**:
   - `Wallet.Application/`: Đăng ký Application/MediatR Services.
   - `Wallet.Infrastructure/`: Đăng ký `WalletDbContext`, MassTransit Bus, Consumers.
4. **Validation**:
   - Input format validation đặt tại Request DTOs qua Data Annotations hoặc FluentValidation.
   - Business validation đặt tại Application Services hoặc Domain Exceptions.

