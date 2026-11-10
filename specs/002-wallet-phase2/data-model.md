# Data Model: Wallet Service Phase 2 Financial Operations

## Existing persisted data

This scope adds no new entity or table.

### WalletAccount

Existing source: `Wallet.Domain/Entities/WalletAccount.cs`

- `Id`: wallet GUID
- `UserId`: owning user GUID
- `Balance`: current total wallet balance
- `LockedBalance`: funds reserved from spending
- `AvailableBalance`: `Balance - LockedBalance`
- `Currency`, `Status`

The reconciliation report compares `Balance`, not `AvailableBalance`, with a transaction snapshot. Reserved withdrawals change `LockedBalance` but do not change total `Balance`.

### WalletTransaction

Existing source: `Wallet.Domain/Entities/WalletTransaction.cs`

- `Id`, `WalletId`, `Type`, `Amount`, `BalanceBefore`, `BalanceAfter`
- `ReferenceId`, `Status`, optional `UserId`, `Currency`, `Note`
- `CreatedAt` inherited from `BaseEntity<Guid>`

The admin report reads these records without altering them. Responses omit free-text notes and bank destination data.

### WithdrawalRequest

Existing source: `Wallet.Domain/Entities/WithdrawalRequest.cs`

- `Id`, `WalletId`, `Amount`, `Fee`, `NetAmount`
- `BankName`, `BankAccountNumber`, `BankAccountHolder`
- `Status`, `ProcessedBy`, `ProcessedAt`
- `GatewayReferenceId`: existing nullable 100-character field for the manual transfer reference

Approval stores its required transfer reference in `GatewayReferenceId`. Rejection does not require or set a transfer reference. The transaction note must not repeat the unmasked bank account number.

## Read models (not persisted entities)

### AdminWalletLedgerEntry

- Transaction ID
- Wallet ID
- Transaction type
- Amount and currency
- Balance before and after
- Status
- Optional business reference ID
- Creation timestamp

The list is filtered and paginated at the database. It excludes bank account fields and notes.

### WalletReconciliationResult

- Wallet ID and currency
- Current recorded wallet balance
- Latest successful transaction balance snapshot, if present
- Difference when comparable
- Successful transaction count
- Latest transaction timestamp
- Result: `Balanced`, `Discrepancy`, or `InsufficientHistory`

`InsufficientHistory` is returned whenever no successful transaction snapshot exists for the wallet, regardless of its current balance. `Balanced` and `Discrepancy` require a successful snapshot.

## State and invariants

- Withdrawal remains `Pending` until a single staff decision.
- Approval is recorded only after manual bank transfer confirmation; the reference is stored with the state transition.
- Rejection releases the reserved amount to available funds.
- Decision retries cannot apply a second balance change.
- All balance calculations remain non-negative and use the existing row-lock/transaction pattern.
- Reconciliation is read-only and does not repair or rewrite source records.
- No data model is defined here for purchase freezes or auction deposits; those capabilities are deferred.

## Migration assessment

No schema migration is expected: the reference column and all report source fields already exist. Review indexes against the PostgreSQL query plan before deciding on an additive migration. Destructive schema changes are out of scope.
