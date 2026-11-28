# View wallet, transaction history, and bank accounts

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Withdrawals](06-withdrawals.md) | [Sequence diagram](01-wallet-and-history.diagram.md)

## View my wallet

**Story:** As a wallet owner, I want to see my total, locked, and spendable balances (or inspect an existing user wallet from admin/internal tools).

1. **Public owner lookup:** Send `GET /api/v1/wallets/me` (`[RequireRole]`). Identity comes from `x-user-id` (missing/invalid returns `401 UNAUTHORIZED`).
2. [GetMyWalletQueryHandler](../../Wallet.Application/Features/Wallets/Queries/GetMyWallet/GetMyWalletQueryHandler.cs) reads `Wallets` (`AsNoTracking`). When `AutoCreate = true` (default for `/me`) and no wallet exists yet, it dispatches `EnsureWalletCommand` ([EnsureWalletCommandHandler](../../Wallet.Application/Features/Wallets/Commands/EnsureWallet/EnsureWalletCommandHandler.cs)), which acquires advisory lock `"wallet-create:{userId:D}"`, creates a zero-balance `ACTIVE` VND wallet, and records `AuditAction.WalletCreate` (re-reading safely on concurrent insert race).
3. **Internal/Admin lookup (`AutoCreate = false`):** `GET /api/v1/admin/wallets/{userId:guid}` (`admin`/`staff`) and `GET ~/api/v1/internal/wallets/{userId:guid}` (`[RequireInternalServiceKey]`) query with `AutoCreate = false` and return `404 WALLET_NOT_FOUND` instead of creating a wallet on read.
4. Returns `200` with `walletId`, `userId`, `balance`, `availableBalance`, `lockedBalance`, `heldBalance` (alias equal to `lockedBalance`), `currency`, `status` (`"Active"` or `"Frozen"`), and `updatedAt`.

## List and inspect my transactions

**Story:** As a user, I want to filter and inspect my paginated ledger entries and see linked gateway status.

1. Send `GET /api/v1/wallets/transactions` (`[RequireRole]`) with optional query parameters: `page` (`>= 1`, default `1`), `pageSize` (`1`–`100`, default `10`), `type`, `status`, `referenceId` (GUID), `fromDate`, `toDate` (`FromDate <= ToDate`, normalized to UTC before querying PostgreSQL).
2. [ListTransactionsQueryHandler](../../Wallet.Application/Features/Wallets/Queries/ListTransactions/ListTransactionsQueryHandler.cs) filters strictly by the authenticated user's wallet, counts and paginates directly in PostgreSQL (`CreatedAt DESC, Id DESC`), and accepts either individual `WalletTransactionType` enum names or group aliases: `ALL`, `DEPOSIT`, `WITHDRAWAL`, `PAYMENT`, `REFUND`, `TRANSFER`, `ADJUSTMENT`, `AUCTION`.
3. Send `GET /api/v1/wallets/transactions/{id:guid}` (`[RequireRole]`). [GetTransactionByIdQueryHandler](../../Wallet.Application/Features/Wallets/Queries/GetTransactionById/GetTransactionByIdQueryHandler.cs) enforces wallet ownership (`404 TRANSACTION_NOT_FOUND` otherwise) and returns `WalletTransactionDetailResponse` enriched with `gatewayStatus` and `gatewayReference` (resolved from `WalletTopups`, `DirectPaymentOrders`, `StripePaymentAttempts`, `WithdrawalRequests`, or `AuctionHolds`).

## Manage saved bank accounts

**Story:** As a seller, I want to manage my payout bank accounts so withdrawals can reference a saved or default account.

All routes require `[RequireRole("seller")]` (accepts `seller` and `admin`; **`staff` is rejected with `403 FORBIDDEN`**). Stored account numbers returned to sellers are always masked via `WithdrawalWorkflow.MaskAccount` (exposing only the last 4 characters).

| Action | Route & Handler | Key Rules & Outcomes |
|---|---|---|
| List mine | `GET /api/v1/wallets/bank-accounts` ([ListMyBankAccountsQueryHandler](../../Wallet.Application/Features/BankAccounts/Queries/ListMyBankAccounts/ListMyBankAccountsQueryHandler.cs)) | `200` ordered `IsDefault DESC, CreatedAt DESC, Id ASC` (`AsNoTracking`, soft-deleted excluded). |
| Add account | `POST /api/v1/wallets/bank-accounts` ([CreateBankAccountCommandHandler](../../Wallet.Application/Features/BankAccounts/Commands/CreateBankAccount/CreateBankAccountCommandHandler.cs)) | `201 Created`. Locks `"bank-account:{userId}"`; validates bank name (2–150), ASCII account number (4–100), holder name (2–255). Rejects case-insensitive bank name + exact account number duplicate (`409 BANK_ACCOUNT_DUPLICATE`). Automatically becomes default if it is the user's first active account; when setting default, flushes prior `IsDefault = false` before inserting to satisfy PostgreSQL partial unique index `IX_BankAccounts_UserId_Default`. |
| Set default | `PATCH /api/v1/wallets/bank-accounts/{id:guid}/default` ([SetDefaultBankAccountCommandHandler](../../Wallet.Application/Features/BankAccounts/Commands/SetDefaultBankAccount/SetDefaultBankAccountCommandHandler.cs)) | `200 OK` (or `404 BANK_ACCOUNT_NOT_FOUND`). Locks `"bank-account:{userId}"`, clears existing default and flushes `SaveChangesAsync` before setting the target `IsDefault = true`. |
| Delete account | `DELETE /api/v1/wallets/bank-accounts/{id:guid}` ([DeleteBankAccountCommandHandler](../../Wallet.Application/Features/BankAccounts/Commands/DeleteBankAccount/DeleteBankAccountCommandHandler.cs)) | `204 NoContent` (or `404 BANK_ACCOUNT_NOT_FOUND`). Blocks deletion (`409 BANK_ACCOUNT_IN_USE`) if any `Pending`, `Approved`, or `Processing` withdrawal references `BankAccountId` (or legacy unlinked matching bank name + account number). Soft-deletes row and, if it was default, clears `IsDefault` first then promotes the newest active account. |

All bank-account mutations write `AuditAction.BankAccountChange` with masked account details.

## Frozen wallet behavior summary

When `WalletAccount.Status == "FROZEN"`, `wallet.EnsureActive()` raises `403 WALLET_FROZEN`:
- **Blocked:** `TopupStripe`, `ResumeTopup`, `CheckoutDirect`, `ResumeDirectPayment`, `PayOrderWithWallet`, `AdjustOrderPayment(+)` debit, `Transfer` (either sender or recipient), `RequestWithdrawal`, withdrawal `Approve`/`Complete`, and `CreateHold`.
- **Still permitted (so owed funds are not stranded):** Wallet & transaction reads (`GetMyWallet`, `ListTransactions`, `GetTransactionById`), `CancelWithdrawal`, withdrawal `Reject`/`Fail`/`ResolveFailed`, `RefundOrderPayment`, `AdjustOrderPayment(-)` refund, `CancelOrderEscrow`, `PayRefundFundCase` buyer credit, confirmed Stripe webhook `TopupStripe` credit, `ReleaseHold`/`CaptureHold`/`RefundCapturedHold`, `SettleSellerOrder`/`SettleOrderPayout` seller credits, and admin `AdjustWalletBalance`.
