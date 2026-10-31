# 06 — DATABASE PERSISTENCE & MIGRATIONS (WALLET SERVICE)

## 1. Database Isolation
* Dedicated database: `wallet_db` on host port `5440`.
* Primary key: 100% `Guid` for entities (`Wallets`, `WalletTransactions`, `WalletTopups`, `BankAccounts`).
* Currency & Balance Precision: `Balance`, `Amount` must configure `HasPrecision(18, 2)`.

## 2. Entity Configuration & Soft Delete
* Every entity declares `IsDeleted` and `DeletedAt`. Enforce query filter: `builder.HasQueryFilter(x => !x.IsDeleted)`.
* Filtered unique index on `Wallets.UserId`:
  ```csharp
  builder.HasIndex(x => x.UserId).IsUnique();
  ```

## 3. Migration Safety
* No destructive migrations (`DROP TABLE`, `DROP COLUMN`) without explicit authorization.
* Startup safe auto-migration: `db.Database.Migrate()` wrapped in `try-catch` checking `db.Database.IsRelational()`.
