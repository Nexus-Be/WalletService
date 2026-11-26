# Transfer between wallets

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Sequence diagram](05-transfers.diagram.md)

**Story:** As a wallet owner, I want to transfer available VND funds to another user's wallet atomically without partial updates or deadlocks.

1. **Request:** Send `POST /api/v1/wallets/transfers` (`[RequireRole]`) with JSON `{ "recipientUserId", "amount", "note?" }` and optional header `Idempotency-Key` (1–100 chars `[A-Za-z0-9-_]`).
   - Sender ID is injected from `x-user-id` by [WalletsController](../../Wallet.Api/Controllers/Public/WalletsController.cs).
   - Amount must be between `1,000` and `100,000,000` VND (`[MoneyPrecision]`); `note` up to 500 characters; self-transfers are rejected (`400`).
2. **Deterministic locking ([TransferCommandHandler](../../Wallet.Application/Features/Transfers/Commands/Transfer/TransferCommandHandler.cs)):**
   - Begins database transaction and locks both sender and recipient `WalletAccount` rows in **ascending `UserId` GUID order** (`LockWalletByUserIdAsync`) to eliminate AB-BA deadlocks when two users transfer to each other concurrently.
   - Both wallets must already exist (`404 WALLET_NOT_FOUND` if either is missing; does **not** auto-create recipient wallets) and must share the same currency (`CurrencyCode`).
3. **Optional idempotency replay:**
   - When `Idempotency-Key` is provided, computes SHA-256 fingerprint over `(SenderGuid, RecipientGuid, Amount, TrimmedNote)`.
   - Queries `WalletTransactions(WalletId == senderWallet.Id, Type == TransferOut, ClientIdempotencyKey == key)`: exact fingerprint match returns the stored `TransferResponse` (`200`); different fingerprint throws `409 IDEMPOTENCY_KEY_REUSED`.
   - Requests sent **without** `Idempotency-Key` are not deduplicated; callers should always supply an `Idempotency-Key` when retrying ambiguous network errors.
4. **Active status & atomic movement:**
   - Enforces `EnsureActive()` on **both** sender and recipient (`403 WALLET_FROZEN` if either wallet is frozen).
   - `senderWallet.Deduct(amount)` checks `AvailableBalance >= amount` (`400 INSUFFICIENT_BALANCE` if locked funds make spendable balance too small); `recipientWallet.Deposit(amount)` credits recipient.
   - Inserts `TransferOut` and `TransferIn` transactions (sharing `ReferenceId = transferId` and identical `CreatedAt`), writes `AuditAction.Transfer`, commits atomically (emitting balanced `TRANSFER_CLEARING` shadow-ledger events for both wallets when `LedgerSettings.Enabled = true`), and returns `200 TransferResponse` (`transactionId`, `amount`, `currency`, `status`, `createdAt`).
