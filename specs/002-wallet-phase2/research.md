# Research: Wallet Service Phase 2 Financial Operations

**Date**: 2026-11-10

## Decision: extend the existing withdrawal use cases

- **Decision**: Keep the current withdrawal creation and decision endpoints and add a reference to the existing approval request/response. Require a reference when staff confirms a manual bank transfer. Preserve `Approved` and `Rejected`.
- **Rationale**: Request creation already validates VND and the 50,000-500,000,000 amount range, locks funds, writes an audit record, and returns `Pending`. Decision handling already locks the withdrawal and wallet, prevents reprocessing, and either consumes or releases locked funds. `WithdrawalRequest.GatewayReferenceId` and its database column already exist.
- **Alternatives considered**: Rebuild withdrawals under the singular `/api/v1/wallet/withdraw` route and add a new processing endpoint; rejected because it duplicates existing behavior and conflicts with existing route/contract safeguards. Automatically execute a bank transfer; rejected because no payout provider is currently implemented.

## Decision: report existing wallet transactions, not a general ledger

- **Decision**: Add separate read-only admin ledger and reconciliation queries over the existing `WalletTransaction` and `WalletAccount` data. Use the existing `PagedData<T>` contract and pagination metadata. Reconciliation compares the current stored balance with the latest successful transaction's `BalanceAfter`, ordered by timestamp and ID; non-zero balances without successful history are reported as `InsufficientHistory`.
- **Rationale**: The user selected a report over existing transaction records rather than a new double-entry accounting model. The current transaction entity records per-wallet amount, type, status, before/after balances, reference, and timestamp; the wallet entity holds current and locked balances.
- **Alternatives considered**: Add general-ledger accounts, journal batches, debit/credit legs, and bank statement reconciliation; rejected for this sprint because the user explicitly selected the existing-transaction report. Calling the report a double-entry ledger would overstate what the data can prove.

## Decision: defer P2-05 and Auction S2S operations

- **Decision**: Do not add freeze/unfreeze or Auction escrow routes in this sprint. Require a separate plan for internal authentication and cross-service contracts first.
- **Rationale**: The current service has internal-service authentication registration disabled, and repository endpoint rules prohibit new `/internal` endpoints while that boundary remains off.
- **Alternatives considered**: Expose the routes immediately on the existing public host using forwarded user/role headers; rejected because internal operations would trust a caller-controlled boundary without an approved service identity contract.

## Decision: coordinate architecture governance updates

- **Decision**: Keep two public controllers, MediatR CQRS, and no service/repository facades. Add two independent read handlers (admin transaction list and reconciliation), raising the verified count from 15 (11 commands, 4 queries) to 17 (11 commands, 6 queries), subject to implementation approval. Update active governance and verifier consistently.
- **Rationale**: The source tree and architecture verifier count 15 handlers. `.specify/memory/constitution.md` and some prior artifacts incorrectly state 11, while current operational guidance states 15. The report and reconciliation are distinct queries with different result semantics.
- **Alternatives considered**: Keep the handler count at 15 by embedding report logic inside existing handlers; rejected because it violates the one-use-case-per-handler convention. Add new controllers or service facades; rejected by the frozen architecture.

## Local implementation patterns reviewed

- Withdrawal uses PostgreSQL row locks through `IWalletDbContext`, explicit transactions, and `_context.AddAuditLog(...)`.
- Read-only transaction history uses `AsNoTracking`, database filtering/`CountAsync`, stable ordering, and `Skip/Take`.
- `WalletTransactionConfiguration` already indexes `(UserId, CreatedAt)` and `(ReferenceId, Type)`. An admin-wide date report may need a different index, but no migration should be added until a PostgreSQL query plan demonstrates the need.
- `WalletTransaction` and `WalletAccount` have HIGH GitNexus impact. Existing withdrawal handlers and the admin controller return UNKNOWN impact; source search confirms references in API dispatch and tests. The impact analysis must be rerun before implementation.

## Resolved unknowns

There are no remaining `[NEEDS CLARIFICATION]` markers. P2-05/Auction internal work is explicitly deferred, and the ledger is explicitly a transaction-record report rather than double-entry accounting.
