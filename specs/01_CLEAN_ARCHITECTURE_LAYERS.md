# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (WALLET SERVICE)

Solution `Wallet.sln` bao gồm 4 project tương ứng với 4 tầng kiến trúc sạch (Clean Architecture):

```
Wallet.sln
├── Wallet.Domain/           # Lõi thực thể (WalletAccount, WalletTransaction, BankAccount, Domain Events, Exceptions)
├── Wallet.Application/      # Use cases CQRS (Commands, Queries, DTOs, Event Consumers, FluentValidation)
├── Wallet.Infrastructure/   # EF Core DbContext, PostgreSQL mappings, MassTransit Outbox Pattern
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

- **`Wallet.Domain`**: Lớp trung tâm, độc lập 100%, không tham chiếu bất kỳ project tầng ngoài nào. Chứa Entity `WalletAccount`, `WalletTransaction`, `BankAccount`, và các Exceptions nghiệp vụ (`InsufficientBalanceException`, `WalletNotFoundException`).
- **`Wallet.Application`**: Phụ thuộc vào `Wallet.Domain`. Khai báo interface `IWalletDbContext`, các Commands CQRS (`DepositCommand`, `WithdrawCommand`, `TransferCommand`), Queries (`GetWalletQuery`, `ListTransactionsQuery`), các DTOs và Consumers nghe RabbitMQ (`StockReservedConsumer`).
- **`Wallet.Infrastructure`**: Phụ thuộc `Wallet.Application` và `Wallet.Domain`. Chứa `WalletDbContext`, cấu hình EntityTypeConfiguration Fluent API, migrations, và tích hợp MassTransit EntityFramework Outbox (`AddInboxStateEntity`, `AddOutboxMessageEntity`).
- **`Wallet.Api`**: Điểm khởi động gRPC Server, đăng ký `app.MapGrpcService<WalletGrpcService>()`, tích hợp OpenTelemetry Distributed Tracing và Swagger.

---

## 2. QUY CHUẨN CODE TỪNG TẦNG

1. **`Wallet.Domain`**:
   - Entities: `WalletAccount`, `WalletTransaction`, `BankAccount`.
   - Mọi biến động số dư phải được bao gói trong các methods của entity: `Deposit(amount)`, `Withdraw(amount)`, `Deduct(amount)`.
   - Tuyệt đối cấm gán trực tiếp `wallet.Balance = ...` từ bên ngoài domain entity.

2. **`Wallet.Application`**:
   - Tách biệt rõ ràng `Commands/` và `Queries/` qua `MediatR`.
   - Áp dụng `ValidationBehavior` tự động kích hoạt FluentValidation trước khi handler được gọi.
   - Với các Queries phân trang (`ListTransactions`): luôn dùng `.AsNoTracking()` và `.Skip().Take()` theo `page` và `pageSize` (tối đa 100 items/trang).

3. **`Wallet.Infrastructure`**:
   - Cấu hình Index trên các trường tìm kiếm thường xuyên: `UserId` (Unique Index trên bảng `wallets`), `UserId` và `CreatedAt` trên bảng `wallet_transactions`.
   - Áp dụng Transactional Outbox Pattern để bảo toàn tin nhắn RabbitMQ.
