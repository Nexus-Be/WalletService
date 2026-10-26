# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (WALLET SERVICE DATA PERSISTENCE)

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

1. **`WalletAccount` (`wallets`)**:
   - `Id`: `Guid` (Primary Key).
   - `UserId`: `string` (Unique Index, Max 100 chars, liên kết logic với `UserService`).
   - `Balance`: `decimal` (Precision: 18, Scale: 2, không bao giờ âm).
   - `Currency`: `string` (Mặc định: `"VND"`, Max 10 chars).
   - `Status`: `string` (`"ACTIVE"`, `"LOCKED"`, Max 30 chars).
   - `CreatedAt`, `UpdatedAt`: `DateTimeOffset`.
   - Navigation: `ICollection<WalletTransaction> Transactions`.

2. **`WalletTransaction` (`wallet_transactions`)**:
   - `Id`: `Guid` (Primary Key).
   - `WalletId`: `Guid` (FK $\rightarrow$ `wallets.Id`).
   - `UserId`: `string` (Index, Max 100 chars).
   - `Type`: `string` (`"DEPOSIT"`, `"WITHDRAW"`, `"TRANSFER_IN"`, `"TRANSFER_OUT"`, `"ORDER_PAYMENT"`, Max 50 chars).
   - `Amount`: `decimal` (Precision: 18, Scale: 2, luôn dương).
   - `Currency`: `string` (Mặc định: `"VND"`).
   - `BalanceAfter`: `decimal` (Precision: 18, Scale: 2).
   - `Status`: `string` (`"SUCCESS"`, `"PENDING"`, `"FAILED"`, Max 30 chars).
   - `Note`: `string` (Max 500 chars).
   - `CreatedAt`: `DateTimeOffset` (Index để phân trang theo thời gian).
   - Navigation: `WalletAccount Wallet`.

3. **`BankAccount` (`bank_accounts`)**:
   - `Id`: `Guid` (Primary Key).
   - `UserId`: `string` (Index, Max 100 chars).
   - `BankName`: `string` (Max 150 chars).
   - `AccountNumber`: `string` (Max 50 chars).
   - `AccountHolderName`: `string` (Max 150 chars).
   - `IsDefault`: `bool`.
   - `CreatedAt`: `DateTimeOffset`.

---

## 2. BẢNG HẠ TẦNG MASSTRANSIT OUTBOX

- `InboxState`: Lưu vết tin nhắn đã nhận (Idempotent Consumer).
- `OutboxMessage`: Lưu tạm event trước khi đẩy lên RabbitMQ.
- `OutboxState`: Theo dõi trạng thái tiến trình quét outbox.

---

## 3. CÁC INDEX BẮT BUỘC

```sql
CREATE UNIQUE INDEX ix_wallets_user_id ON wallets (user_id);
CREATE INDEX ix_wallet_transactions_user_id ON wallet_transactions (user_id);
CREATE INDEX ix_wallet_transactions_created_at ON wallet_transactions (created_at DESC);
CREATE INDEX ix_bank_accounts_user_id ON bank_accounts (user_id);
```

Balance-changing requests that reserve, debit, refund, or adjust funds run in a database transaction and lock the wallet row with PostgreSQL `SELECT ... FOR UPDATE`. `available_balance` is computed as `balance - locked_balance`; pending withdrawals increase only `locked_balance`, approval reduces both balances, and rejection only releases the locked amount. Every admin adjustment and withdrawal state change writes an `AuditLog`.
