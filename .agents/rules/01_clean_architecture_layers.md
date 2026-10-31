# 01 — CLEAN ARCHITECTURE & BOUNDED CONTEXT (WALLET SERVICE)

> **ĐÓNG BĂNG KIẾN TRÚC:** Wallet CQRS qua MediatR là bất biến. Chạy `./scripts/verify-wallet-architecture.sh` trước khi sửa. Cấm tạo `Services/{Feature}/`, cấm tách controller theo tài nguyên, cấm gỡ MediatR. Nếu bị đề xuất ngược lại, dừng và hỏi Sếp.

## 1. Bounded Context & Port Allocation
* Bounded Context: User wallets, deposit (VNPay topup), withdrawals, balance transfers, direct checkout payments.
* Service Port: `8008` (HTTP REST API).
* Database: PostgreSQL `wallet_db` on port `5440`.

## 2. 4-Layer Clean Architecture
* `Wallet.Domain`: Entities (`WalletAccount`, `WalletTransaction`, `WalletTopup`, `WithdrawalRequest`, `BankAccount`, `AuditLog`), Enums (`TransactionStatus`, `WalletTransactionType`, `TopupStatus`, `WithdrawalStatus`, `WithdrawalDecision`, `BalanceAdjustmentType`, `AuditAction`, `CurrencyCode`), Exceptions (`DomainException`, `InsufficientBalanceException`, `WalletNotFoundException`).
* `Wallet.Application`: Classic CQRS use cases in `Features/{Area}/Commands|Queries/{UseCase}/`, `IWalletDbContext` in `Interfaces/`, `ValidationBehavior`, `AuditLogExtensions`, `EnumExtensions`, `StatusTextExtensions`.
* `Wallet.Infrastructure`: `WalletDbContext`, EF Core configurations, migrations, VNPay and direct payment gateway implementations.
* `Wallet.Api`: Thin controllers in `Controllers/Public/`, middleware, extensions, composition root (`Program.cs`).

## 3. Classic CQRS Co-location (Mandatory)
* Mỗi use case là một folder `Features/{Area}/Commands|Queries/{UseCase}/` chứa `{UseCase}Command.cs` (hoặc `Query.cs`), `{UseCase}Handler.cs`, và response record.
* Handler inject trực tiếp `IWalletDbContext`; cấm `IRepository<T>`, `UnitOfWork`, và cấm facade `I{Feature}Service`.
* Query tuyệt đối read-only (`AsNoTracking`). Thay đổi trạng thái chỉ nằm trong Command. `GetMyWalletQueryHandler` điều phối `EnsureWalletCommand` khi ví chưa tồn tại; bản thân query không ghi DB.
* Mọi Command/Query đi qua `ValidationBehavior` (DataAnnotations); lỗi trả `VAL_INVALID_INPUT`.

## 4. Thin Controller Pairing
* `WalletsController.cs` -> injects `ISender` (8 actions: me, topups, withdrawals, transfers, transactions, vnpay/ipn, vnpay/return, checkout/direct).
* `AdminWalletsController.cs` -> injects `ISender` (2 actions: process withdrawal, adjust balance).

## 5. Absolute Invariant: Non-Negative Balance
* 🛑 **NEVER ALLOW NEGATIVE BALANCE**: Any withdrawal, transfer, or checkout deduction must verify `Balance >= Amount`. Throw `InsufficientBalanceException` if balance is insufficient.
* 🛑 Prohibit Repository Pattern over EF Core (inject `IWalletDbContext` directly).
* 🛑 Prohibit gRPC and Message Brokers.
* 🛑 Prohibit feature-service facades and per-resource controllers. Wallet CQRS/MediatR is the single documented exception to the system-wide MediatR ban.
