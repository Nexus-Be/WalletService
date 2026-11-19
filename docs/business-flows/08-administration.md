# Administrative adjustments and reports

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

Administrative routes begin with `/api/v1/admin/wallets`. The controller
requires admin or staff; the two reports additionally require admin.
Withdrawal processing is described in [withdrawals](06-withdrawals.md).

## Adjust a wallet

**Story:** As admin or staff, I need to correct a wallet balance with a reason
and an audit trail.

1. Send `POST /api/v1/admin/wallets/{userId}/adjustments` with `amount`,
   `adjustmentType` (`Credit` or `Debit`) and `reason`. The controller gets
   the operator's identity and supplies the target user ID from the route.
2. [AdjustWalletBalanceCommandHandler](../../Wallet.Application/Features/AdminWallets/Commands/AdjustWalletBalance/AdjustWalletBalanceCommandHandler.cs)
   locks the target wallet in a transaction. Credit deposits the amount; debit
   requires sufficient available balance. It stores an adjustment transaction
   and audit entry with the operator ID and commits.
3. Wallet returns `200` with `adjustmentId`, `walletId`, `amount`,
   `balanceAfter`, `createdAt`. A missing wallet, invalid request or
   insufficient available balance returns an error. This handler does not
   implement an explicit idempotency key.

## Inspect the ledger

**Story:** As an admin, I want a paginated transaction report for investigation.

1. Send `GET /api/v1/admin/wallets/ledger` with optional pagination and
   `walletId`, `type`, `status`, `fromDate`, `toDate` filters. Staff is
   forbidden by the additional admin check.
2. [ListWalletLedgerQueryHandler](../../Wallet.Application/Features/AdminWallets/Queries/ListWalletLedger/ListWalletLedgerQueryHandler.cs)
   filters and counts ledger rows and fetches a newest-first database page.
3. Wallet returns `200` with paged entries including transaction and wallet
   IDs, type, amount, currency, before/after balances, status, reference and
   creation time.

## Compare current wallet balances to ledger snapshots

**Story:** As an admin, I want to identify wallets whose current balance does
not match the latest successful ledger snapshot.

1. Send `GET /api/v1/admin/wallets/reconciliation` with optional `walletId`
   and pagination. Staff is forbidden.
2. [GetWalletReconciliationQueryHandler](../../Wallet.Application/Features/AdminWallets/Queries/GetWalletReconciliation/GetWalletReconciliationQueryHandler.cs)
   reads a page of wallets and, per wallet, its latest successful transaction
   `balanceAfter`, transaction count and timestamp.
3. Wallet returns `200` with paged results marked `Balanced`, `Discrepancy`,
   or `InsufficientHistory`, plus current balance, latest snapshot and
   difference. This is a snapshot comparison, not a full external bank or
   Stripe reconciliation, and it does not modify balances.

Both reports return the shared `PagedData` structure. When sent through the
checked-in Gateway, successful JSON is normally wrapped as described in the
[shared path](00-shared-request-path.md).
