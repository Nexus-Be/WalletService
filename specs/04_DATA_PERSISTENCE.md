# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (WALLET SERVICE DATA PERSISTENCE)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — 8 entity ↔ 8 `DbSet` (`Wallet.Infrastructure/Persistence/WalletDbContext.cs:14-21`) và migration duy nhất tạo **11 bảng** (`InboxState`, `OutboxState`, `OutboxMessage`, `Wallets`, `WalletTransactions`, `WalletTopups`, `WithdrawalRequests`, `VnpayIpnLogs`, `AuditLogs`, `BankAccounts`, `DirectPaymentOrders`). Nhưng **6/8 bảng không có đường ghi nào** trong code ứng dụng — chỉ `Wallets` và `WalletTransactions` được handler/consumer dùng.
> **Nguồn sự thật:** code (`Wallet.Infrastructure/Persistence/WalletDbContext.cs:14-33`, `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs`, `Wallet.Domain/Entities/*`).
> **Cập nhật lần cuối:** 2026-10-24.

> **Hiện trạng:** chỉ Wallet **track `appsettings.json` thật** trong 9 repo (9 repo còn lại chỉ track `*.example.json`) — file này chứa connection string có mật khẩu (`Wallet.Api/appsettings.json:10`) và mật khẩu RabbitMQ (`:16`). Tài liệu này **không in lại** các giá trị đó.
> Tên bảng là **PascalCase** trong PostgreSQL (EF Core giữ nguyên tên `DbSet`), **không phải snake_case** như tài liệu cũ ngụ ý.

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

### 1.0 Tổng quan: bảng ↔ entity ↔ có đường ghi?

| Entity | Bảng thật | Kế thừa | Có đường ghi trong app? | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---|:---:|:---:|:---|
| `WalletAccount` | `Wallets` | `AuditableEntity<Guid>` | ✅ Có (Deposit/Transfer/GetWallet/Consumer) | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:7`; `Wallet.Infrastructure/Persistence/WalletDbContext.cs:14` |
| `WalletTransaction` | `WalletTransactions` | `BaseEntity<Guid>` | ✅ Có (mọi command + consumer) | ✅ | `Wallet.Domain/Entities/WalletTransaction.cs:6`; `WalletDbContext.cs:15` |
| `BankAccount` | `BankAccounts` | `BaseEntity<Guid>` | ❌ Không (chỉ seed 1 dòng) | ❌ | `Wallet.Domain/Entities/BankAccount.cs:5`; `Wallet.Infrastructure/Persistence/Seeds/WalletSeedData.cs:90` |
| `WalletTopup` | `WalletTopups` | `BaseEntity<Guid>` | ❌ Không | ❌ | `Wallet.Domain/Entities/WalletTopup.cs:6`; `WalletDbContext.cs:16` |
| `DirectPaymentOrder` | `DirectPaymentOrders` | `BaseEntity<Guid>` | ❌ Không | ❌ | `Wallet.Domain/Entities/DirectPaymentOrder.cs:6`; `WalletDbContext.cs:18` |
| `WithdrawalRequest` | `WithdrawalRequests` | `BaseEntity<Guid>` | ❌ Không (chỉ là navigation của `WalletAccount`) | ❌ | `Wallet.Domain/Entities/WithdrawalRequest.cs:6`; `WalletAccount.cs:18` |
| `VnpayIpnLog` | `VnpayIpnLogs` | `BaseEntity<Guid>` | ❌ Không | ❌ | `Wallet.Domain/Entities/VnpayIpnLog.cs:6`; `WalletDbContext.cs:20` |
| `AuditLog` | `AuditLogs` | **POCO thuần** (không kế thừa `BaseEntity`) | ❌ Không | ❌ | `Wallet.Domain/Entities/AuditLog.cs:5`; `WalletDbContext.cs:21` |

### 1.1 `WalletAccount` → bảng `Wallets`

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `Id` (từ base) | `Guid`, PK | ✅ | `Wallet.Domain/Base/BaseEntity.cs:5` |
| `UserId` | `Guid`, **required**, UNIQUE index | ✅ | `Wallet.Domain/Entities/WalletAccount.cs:9`; `Wallet.Infrastructure/Persistence/Configurations/WalletAccountConfiguration.cs:12` |
| `Balance` | `decimal(18,2)` | ✅ | `WalletAccount.cs:10`; `WalletAccountConfiguration.cs:13` |
| `LockedBalance` | `decimal(18,2)` — **chưa có code nào dùng** | ⚠️ | `WalletAccount.cs:11`; `WalletAccountConfiguration.cs:14` |
| `Currency` | `CurrencyCode` (`Vnd=0`, `Usd=1`), lưu **string** max 10 | ✅ | `WalletAccount.cs:12`; `WalletAccountConfiguration.cs:15`; `Wallet.Domain/Enums/CurrencyCode.cs` |
| `Status` | `string`, mặc định `"ACTIVE"`, max 30 | ✅ | `WalletAccount.cs:13`; `WalletAccountConfiguration.cs:16` |
| `CreatedBy`, `UpdatedBy` (từ `AuditableEntity`) | `Guid?` | ✅ | `Wallet.Domain/Base/IAuditable.cs:15-18`; quan hệ cấu hình `WalletAccountConfiguration.cs:18-28` |
| `IsDeleted`, `DeletedAt`, `CreatedAt`, `UpdatedAt` (từ base) | `bool`, `DateTimeOffset?`, `DateTimeOffset` | ⚠️ có cột nhưng **không có query filter** | `Wallet.Domain/Base/BaseEntity.cs:6-9`; chỉ `DirectPaymentOrderConfiguration.cs:29` có `HasQueryFilter` |
| Navigation: `Transactions`, `Topups`, `WithdrawalRequests` | `ICollection<…>` | ✅ | `WalletAccount.cs:16-18` |

### 1.2 `WalletTransaction` → bảng `WalletTransactions`

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `WalletId` | `Guid`, required, FK → `Wallets.Id` | ✅ | `Wallet.Domain/Entities/WalletTransaction.cs:8`; `WalletTransactionConfiguration.cs:18-20` |
| `Type` | `WalletTransactionType` (11 giá trị), lưu **string** | ✅ | `WalletTransaction.cs:9`; `WalletTransactionConfiguration.cs:12`; `Wallet.Domain/Enums/WalletTransactionType.cs` |
| `Amount`, `BalanceBefore`, `BalanceAfter` | `decimal(18,2)` | ✅ | `WalletTransaction.cs:10-12`; `WalletTransactionConfiguration.cs:13-15` |
| `ReferenceId` | `Guid?` — chỉ seed dùng (trỏ SubOrder) | ⚠️ | `WalletTransaction.cs:13`; `Wallet.Infrastructure/Persistence/Seeds/WalletSeedData.cs:106-119` |
| `Status` | `TransactionStatus` (`Pending=0`, `Success=1`, `Failed=2`, `Cancelled=3`), lưu string | ✅ | `WalletTransaction.cs:14`; `WalletTransactionConfiguration.cs:16` |
| `UserId` | `string?` — **không nằm trong tài liệu cũ**, là cột lọc chính của `ListTransactions` | ⚠️ (không index, không max length) | `WalletTransaction.cs:17`; dùng để lọc `Wallet.Application/Features/Queries/ListTransactions/ListTransactionsQuery.cs:27` |
| `Currency` | `string?` — không cấu hình max length | ⚠️ | `WalletTransaction.cs:18` |
| `Note` | `string?` — không cấu hình max length | ⚠️ | `WalletTransaction.cs:19` |
| Navigation `Wallet` | `WalletAccount?` | ✅ | `WalletTransaction.cs:22` |

### 1.3 `BankAccount` → bảng `BankAccounts`

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `UserId` | `string` (không phải `Guid`), max 100 | ✅ | `Wallet.Domain/Entities/BankAccount.cs:7`; `BankAccountConfiguration.cs:12` |
| `BankName` | `string`, max 150 | ✅ | `BankAccount.cs:8`; `BankAccountConfiguration.cs:13` |
| `AccountNumber` | `string`, max 100 | ✅ | `BankAccount.cs:9`; `BankAccountConfiguration.cs:14` |
| `AccountHolderName` | `string`, max 255 | ✅ | `BankAccount.cs:10`; `BankAccountConfiguration.cs:15` |
| `IsDefault` | `bool`, mặc định `true` | ✅ | `BankAccount.cs:11` |

### 1.4 `WalletTopup` → bảng `WalletTopups` (nạp tiền VNPay)

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `WalletId` | `Guid`, required, FK → `Wallets.Id` | ✅ | `Wallet.Domain/Entities/WalletTopup.cs:8`; `WalletTopupConfiguration.cs:20-27` |
| `VnpTxnRef` | `string`, **required**, UNIQUE index | ✅ | `WalletTopup.cs:9`; `WalletTopupConfiguration.cs:13` |
| `VnpTransactionNo`, `VnpBankCode` | `string?` | ✅ | `WalletTopup.cs:10-11` |
| `Amount` | `decimal(18,2)` | ✅ | `WalletTopup.cs:12` |
| `Status` | `TopupStatus` (`Pending=0`, `Success=1`, `Failed=2`, `Expired=3`) | ✅ | `WalletTopup.cs:13`; `Wallet.Domain/Enums/TopupStatus.cs` |
| `RawResponse` | `string?` → `jsonb` | ✅ | `WalletTopup.cs:14`; `WalletTopupConfiguration.cs:18` |
| Navigation `Wallet`, `IpnLogs` | `WalletAccount?`, `ICollection<VnpayIpnLog>` | ✅ | `WalletTopup.cs:17-18` |
| **Đường ghi** | không command/handler nào dùng | ❌ | grep `WalletTopups` ngoài DbContext/Interface/Migration = 0 |

### 1.5 `DirectPaymentOrder` → bảng `DirectPaymentOrders` (thanh toán trực tiếp VNPay)

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `OrderId` | `Guid`, required, index (tham chiếu logic đơn hàng, không FK) | ✅ | `Wallet.Domain/Entities/DirectPaymentOrder.cs:8`; `DirectPaymentOrderConfiguration.cs:14` |
| `VnpTxnRef` | `string`, required, UNIQUE index | ✅ | `DirectPaymentOrder.cs:9`; `DirectPaymentOrderConfiguration.cs:17` |
| `VnpTransactionNo` | `string?` | ✅ | `DirectPaymentOrder.cs:10` |
| `Amount` | `decimal(18,2)` | ✅ | `DirectPaymentOrder.cs:11` |
| `Status` | `TopupStatus` — dùng **chung** enum với `WalletTopup` | ✅ | `DirectPaymentOrder.cs:12` |
| `RawResponse` | `string?` → `jsonb` | ✅ | `DirectPaymentOrder.cs:13`; `DirectPaymentOrderConfiguration.cs:22` |
| `IsDeleted` query filter | `HasQueryFilter(x => !x.IsDeleted)` — **chỉ entity này có** | ⚠️ không đồng nhất | `DirectPaymentOrderConfiguration.cs:29` |
| Navigation `IpnLogs` | `ICollection<VnpayIpnLog>` | ✅ | `DirectPaymentOrder.cs:16` |
| **Đường ghi** | không command/handler nào dùng | ❌ | grep `DirectPaymentOrders` ngoài DbContext/Interface/Migration = 0 |

### 1.6 `WithdrawalRequest` → bảng `WithdrawalRequests` (yêu cầu rút tiền)

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `WalletId` | `Guid`, required, FK → `Wallets.Id`, index | ✅ | `Wallet.Domain/Entities/WithdrawalRequest.cs:8`; `WithdrawalRequestConfiguration.cs` |
| `Amount`, `Fee`, `NetAmount` | `decimal(18,2)` | ✅ | `WithdrawalRequest.cs:9-11`; `WithdrawalRequestConfiguration.cs:12-14` |
| `BankName`, `BankAccountNumber`, `BankAccountHolder` | `string`, required | ✅ | `WithdrawalRequest.cs:12-14` |
| `Status` | `WithdrawalStatus` (`Pending=0`, `Approved=1`, `Processing=2`, `Completed=3`, `Rejected=4`, `Failed=5`) | ✅ | `WithdrawalRequest.cs:15`; `Wallet.Domain/Enums/WithdrawalStatus.cs` |
| `ProcessedBy` | `Guid?` — **không có trong tài liệu cũ** | ✅ | `WithdrawalRequest.cs:16` |
| `GatewayReferenceId` | `string?`, max 100 | ✅ | `WithdrawalRequest.cs:17`; `WithdrawalRequestConfiguration.cs:19` |
| `ProcessedAt` | `DateTimeOffset?` | ✅ | `WithdrawalRequest.cs:18` |
| **Đường ghi** | `WithdrawCommandHandler` **không** tạo bản ghi nào | ❌ | `Wallet.Application/Features/Commands/Withdraw/WithdrawCommand.cs:33-59` |

### 1.7 `VnpayIpnLog` → bảng `VnpayIpnLogs` (log IPN của VNPay)

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `VnpTxnRef` | `string`, required, index (liên kết logic, **không** FK) | ✅ | `Wallet.Domain/Entities/VnpayIpnLog.cs:8`; `VnpayIpnLogConfiguration.cs:19-22` |
| `VnpResponseCode`, `VnpTransactionNo`, `ClientIp` | `string?` | ✅ | `VnpayIpnLog.cs:9-11` |
| `RawPayload` | `string?` → `jsonb` | ✅ | `VnpayIpnLog.cs:12` |
| `ProcessingStatus` | `IpnProcessingStatus` (`Processed=0`, `Duplicate`, `InvalidSignature`, `OrderNotFound`, `InvalidAmount`, `SystemError`), lưu string | ✅ | `VnpayIpnLog.cs:13`; `VnpayIpnLogConfiguration.cs:17`; `Wallet.Domain/Enums/IpnProcessingStatus.cs` |
| Navigation `WalletTopup?`, `DirectPaymentOrder?` | `null`-able | ✅ | `VnpayIpnLog.cs:16-17` |
| **Đường ghi** | không handler IPN nào tồn tại | ❌ | grep `VnpayIpnLogs` ngoài DbContext/Interface/Migration = 0 |

### 1.8 `AuditLog` → bảng `AuditLogs` (append-only)

| Thuộc tính | Kiểu / ánh xạ | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `Id` | `Guid`, tự sinh `Guid.NewGuid()` | ✅ | `Wallet.Domain/Entities/AuditLog.cs:7` |
| `ServiceName` | `string`, required | ✅ | `AuditLog.cs:8` |
| `TraceId`, `UserId`, `UserName` | `string?` | ✅ | `AuditLog.cs:9-11` |
| `Action` | `AuditAction` (9 giá trị `WalletCreate=0` … `BalanceAdjustment=8`), lưu string | ✅ | `AuditLog.cs:12`; `AuditLogConfiguration.cs:16`; `Wallet.Domain/Enums/AuditAction.cs` |
| `EntityName`, `EntityId` | `string`, required | ✅ | `AuditLog.cs:13-14` |
| `OldValues`, `NewValues`, `ChangedColumns` | `string?` → `jsonb` | ✅ | `AuditLog.cs:15-17`; `AuditLogConfiguration.cs:19-21` |
| `IpAddress`, `UserAgent` | `string?` | ✅ | `AuditLog.cs:18-19` |
| `CreatedAt` | `DateTimeOffset`, mặc định `UtcNow` | ✅ | `AuditLog.cs:20` |
| `IsSynced` | `bool` — **không có trong tài liệu cũ** | ⚠️ chưa có code set | `AuditLog.cs:21` |
| **Đường ghi** | 0 lời gọi | ❌ | grep `AuditLogs` ngoài DbContext/Interface/Migration = 0 |

---

## 2. BẢNG HẠ TẦNG MASSTRANSIT OUTBOX

| Bảng | Vai trò | Có trong migration | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---:|:---|
| `InboxState` | Lưu vết tin nhắn đã nhận (idempotent consumer) | ✅ (dòng 85) | ✅ | `Wallet.Infrastructure/Persistence/WalletDbContext.cs:31`; `.../Migrations/20261024103202_InitialWalletSchema.cs:85` |
| `OutboxState` | Theo dõi tiến trình quét outbox | ✅ (dòng 108) | ✅ | `WalletDbContext.cs:33`; `.../20261024103202_InitialWalletSchema.cs:108` |
| `OutboxMessage` | Lưu tạm event trước khi đẩy lên RabbitMQ | ✅ (dòng 146) | ✅ | `WalletDbContext.cs:32`; `.../20261024103202_InitialWalletSchema.cs:146` |

> 3 bảng này do `AddEntityFrameworkOutbox<WalletDbContext>` quản lý (`Wallet.Infrastructure/InfrastructureDependencyInjection.cs:36-40`), **không** thuộc bounded context nghiệp vụ — đừng thêm cột vào chúng.

---

## 3. MIGRATION & 11 BẢNG THẬT

Chỉ có **một** migration được track: `Wallet.Infrastructure/Migrations/20261024103202_InitialWalletSchema.cs`.

| Bảng | Dòng `CreateTable` | Đường ghi trong code | Trạng thái | Bằng chứng (file:line) |
|:---|:---:|:---|:---:|:---|
| `AuditLogs` | 18 | ❌ không có | ❌ | `.../20261024103202_InitialWalletSchema.cs:18` |
| `BankAccounts` | 43 | ❌ không có (chỉ seed) | ❌ | `.../20261024103202_InitialWalletSchema.cs:43`; `Seeds/WalletSeedData.cs:90` |
| `DirectPaymentOrders` | 63 | ❌ không có | ❌ | `.../20261024103202_InitialWalletSchema.cs:63` |
| `InboxState` | 85 | ✅ MassTransit | ✅ | `.../20261024103202_InitialWalletSchema.cs:85` |
| `OutboxState` | 108 | ✅ MassTransit | ✅ | `.../20261024103202_InitialWalletSchema.cs:108` |
| `Wallets` | 124 | ✅ handler + consumer | ✅ | `.../20261024103202_InitialWalletSchema.cs:124` |
| `OutboxMessage` | 146 | ✅ MassTransit | ✅ | `.../20261024103202_InitialWalletSchema.cs:146` |
| `WalletTopups` | 188 | ❌ không có | ❌ | `.../20261024103202_InitialWalletSchema.cs:188` |
| `WalletTransactions` | 217 | ✅ mọi command + consumer | ✅ | `.../20261024103202_InitialWalletSchema.cs:217` |
| `WithdrawalRequests` | 248 | ❌ không có | ❌ | `.../20261024103202_InitialWalletSchema.cs:248` |
| `VnpayIpnLogs` | 280 | ❌ không có | ❌ | `.../20261024103202_InitialWalletSchema.cs:280` |

---

## 4. CẤU HÌNH EF CORE & QUERY FILTER

| Hạng mục | Thực tế | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| `IWalletDbContext` khai báo gì | Chỉ **4** `DbSet`: `Wallets`, `WalletTransactions`, `BankAccounts`, `DirectPaymentOrders` + `SaveChangesAsync` | ⚠️ thiếu 4 DbSet so với `WalletDbContext` | `Wallet.Application/Common/Interfaces/IWalletDbContext.cs:6` |
| Connection + retry | `UseNpgsql` + `EnableRetryOnFailure(5, 10s)` | ✅ | `Wallet.Infrastructure/InfrastructureDependencyInjection.cs:20-24` |
| Đăng ký DI | `AddScoped<IWalletDbContext>(sp => sp.GetRequiredService<WalletDbContext>())` | ✅ | `InfrastructureDependencyInjection.cs:27` |
| Soft delete tự động | Chỉ `DirectPaymentOrder` có `HasQueryFilter`; `Wallets`, `WalletTransactions`, `WalletTopups`, `WithdrawalRequests` **không** có ⇒ bản ghi `IsDeleted=true` vẫn hiện ra | ❌ không đồng nhất | `DirectPaymentOrderConfiguration.cs:29` |
| Enum lưu dạng chuỗi | `WalletAccount.Currency`/`Status`, `WalletTransaction.Type`/`Status`, `VnpayIpnLog.ProcessingStatus`, `AuditLog.Action` | ✅ | `WalletAccountConfiguration.cs:15-16`, `WalletTransactionConfiguration.cs:12,16`, `VnpayIpnLogConfiguration.cs:17`, `AuditLogConfiguration.cs:16` |
| Cột `jsonb` | `WalletTopup.RawResponse`, `DirectPaymentOrder.RawResponse`, `VnpayIpnLog.RawPayload`, `AuditLog.OldValues/NewValues/ChangedColumns` | ✅ | `WalletTopupConfiguration.cs:18`, `DirectPaymentOrderConfiguration.cs:22`, `AuditLogConfiguration.cs:19-21` |
| Auto-migrate khi khởi động | Chạy `Database.Migrate()` trong `try/catch`, **nuốt lỗi** chỉ log warning | ⚠️ che giấu lỗi DB | `Wallet.Api/Program.cs:49-64` |

---

## 5. DỮ LIỆU HẠT GIỐNG (SEED) — `Wallet.Infrastructure/Persistence/Seeds/WalletSeedData.cs`

```sql
CREATE UNIQUE INDEX ix_wallets_user_id ON wallets (user_id);
CREATE INDEX ix_wallet_transactions_user_id ON wallet_transactions (user_id);
CREATE INDEX ix_wallet_transactions_created_at ON wallet_transactions (created_at DESC);
CREATE INDEX ix_bank_accounts_user_id ON bank_accounts (user_id);
```

Balance-changing requests that reserve, debit, refund, or adjust funds run in a database transaction and lock the wallet row with PostgreSQL `SELECT ... FOR UPDATE`. `available_balance` is computed as `balance - locked_balance`; pending withdrawals increase only `locked_balance`, approval reduces both balances, and rejection only releases the locked amount. Every admin adjustment and withdrawal state change writes an `AuditLog`.
