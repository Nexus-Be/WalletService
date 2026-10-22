# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (WALLET SERVICE DATA PERSISTENCE)

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

1. **`Wallet` / `WalletAccount`**:
   - `Id`: `Guid` (Primary Key).
   - `UserId`: `Guid` (Unique Index, liên kết logic với `UserService`).
   - `Balance`: `decimal` (Precision: 18, Scale: 2).
   - `LockedBalance`: `decimal` (Precision: 18, Scale: 2).
   - `Currency`: `CurrencyCode` (`Vnd = 0`, `Usd = 1`).
   - `Status`: `string` (`"ACTIVE"`).
   - `CreatedAt`, `UpdatedAt`: `DateTimeOffset`.
   - Navigation: `Transactions`, `Topups`, `WithdrawalRequests`.

2. **`WalletTransaction`**:
   - `Id`: `Guid` (Primary Key).
   - `WalletId`: `Guid` (FK $\rightarrow$ `Wallet.Id`).
   - `Type`: `WalletTransactionType` (11 loại: `TopupVnpay`, `PurchaseDebit`, `RefundCredit`, `EscrowCredit`, `EscrowRelease`, `WithdrawalDebit`, `WithdrawalReversal`, `PlatformFeeCredit`, `AdjustmentCredit`, `AdjustmentDebit`, `DirectVnpayDebit`).
   - `Amount`: `decimal` (Precision: 18, Scale: 2).
   - `BalanceBefore`, `BalanceAfter`: `decimal`.
   - `ReferenceId`: `Guid?`.
   - `Status`: `TransactionStatus` (`Pending = 0`, `Success = 1`, `Failed = 2`, `Cancelled = 3`).
   - `CreatedAt`: `DateTimeOffset`.
   - Navigation: `Wallet`.

3. **`WalletTopup`**:
   - `Id`: `Guid` (Primary Key).
   - `WalletId`: `Guid` (FK $\rightarrow$ `Wallet.Id`).
   - `VnpTxnRef`: `string` (Unique Index, mã tham chiếu gửi sang VNPay).
   - `VnpTransactionNo`: `string?`.
   - `VnpBankCode`: `string?`.
   - `Amount`: `decimal` (Precision: 18, Scale: 2).
   - `Status`: `TopupStatus` (`Pending = 0`, `Success = 1`, `Failed = 2`, `Expired = 3`).
   - `RawResponse`: `string?` (`jsonb`).
   - Navigation: `Wallet`, `IpnLogs`.

4. **`DirectPaymentOrder` (Thanh toán trực tiếp qua cổng VNPay)**:
   - `Id`: `Guid` (Primary Key).
   - `OrderId`: `Guid` (Index, Cross-service Ref $\rightarrow$ `CommerceService.Orders.Id`).
   - `VnpTxnRef`: `string` (Unique Index, mã tham chiếu gửi sang VNPay).
   - `VnpTransactionNo`: `string?` (Mã giao dịch phía VNPay phản hồi).
   - `Amount`: `decimal` (Precision: 18, Scale: 2).
   - `Status`: `TopupStatus` (`Pending = 0`, `Success = 1`, `Failed = 2`, `Expired = 3`).
   - `RawResponse`: `string?` (`jsonb`).
   - `CreatedAt`, `UpdatedAt`: `DateTimeOffset`.
   - Navigation: `ICollection<VnpayIpnLog> IpnLogs`.

5. **`WithdrawalRequest`**:
   - `Id`: `Guid` (Primary Key).
   - `WalletId`: `Guid` (FK $\rightarrow$ `Wallet.Id`).
   - `Amount`: `decimal`.
   - `Fee`, `NetAmount`: `decimal`.
   - `BankName`, `BankAccountNumber`, `BankAccountHolder`: `string`.
   - `Status`: `WithdrawalStatus` (`Pending = 0`, `Approved = 1`, `Processing = 2`, `Completed = 3`, `Rejected = 4`, `Failed = 5`).
   - Navigation: `Wallet`.

6. **`VnpayIpnLog`**:
   - `Id`: `Guid`.
   - `VnpTxnRef`: `string` (Mã tham chiếu liên kết logic với `WalletTopup` hoặc `DirectPaymentOrder`).
   - `VnpResponseCode`, `VnpTransactionNo`, `ClientIp`: `string?`.
   - `RawPayload`: `string?` (`jsonb`).
   - `ProcessingStatus`: `IpnProcessingStatus`.
   - Navigation: `WalletTopup?`, `DirectPaymentOrder?`.

7. **`BankAccount`**:
   - `Id`: `Guid`.
   - `UserId`: `string`.
   - `BankName`, `AccountNumber`, `AccountHolderName`: `string`.
   - `IsDefault`: `bool`.

8. **`AuditLog`**:
   - Ghi vết bất biến (Append-only).
   - `ServiceName`, `TraceId`, `UserId`, `UserName`, `Action`, `EntityName`, `EntityId`, `OldValues`, `NewValues`, `ChangedColumns`, `IpAddress`, `UserAgent`.

---

## 2. BẢNG HẠ TẦNG MASSTRANSIT OUTBOX

- `InboxState`: Lưu vết tin nhắn đã nhận (Idempotent Consumer).
- `OutboxMessage`: Lưu tạm event trước khi đẩy lên RabbitMQ.
- `OutboxState`: Theo dõi trạng thái tiến trình quét outbox.
