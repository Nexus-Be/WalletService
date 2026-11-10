# Implementation Plan: Wallet Service Phase 2 Financial Operations

**Branch**: `agents/wallet-phase2-plan-execution` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

**Input**: User-selected WalletService slice of the Phase 2 API catalog. In scope: P2-03 withdrawal completion and P2-04 admin transaction reporting/reconciliation. P2-05 purchase freeze and Auction escrow are deferred pending an approved internal-service security contract.

## Summary

Extend the existing withdrawal flow to require and persist manual bank-transfer evidence at approval, and add read-only administrator transaction and reconciliation queries over the current wallet data. Preserve existing withdrawal routes/statuses, both public controllers, CQRS/MediatR, and the direct `IWalletDbContext` pattern. Do not introduce a double-entry accounting claim or bank-statement reconciliation.

The implementation adds two CQRS queries, taking the current verified 15 handlers (11 commands and 4 queries in code/verifier) to 17 (11 commands and 6 queries). This requires explicit implementation approval and synchronized updates to active architecture governance. The constitution currently says 11 handlers; reconcile this stale count to the verified baseline before applying the feature count.

## Technical Context

- **Runtime**: .NET 8 / ASP.NET Core
- **Primary dependencies**: MediatR, EF Core, Npgsql
- **Storage**: PostgreSQL, `wallet_db`
- **Testing**: `Wallet.Tests` (xUnit); PostgreSQL-backed verification for query behavior where available
- **Target platform**: Linux/container-hosted ASP.NET Core service
- **Project type**: REST API service
- **Performance goals**: Database-side filtering, counting, stable ordering, and pagination; use existing `page`/`pageSize` conventions (page size 1-100).
- **Constraints**: Keep exactly two controllers, CQRS/MediatR, direct `IWalletDbContext` access, non-negative available balance, ownership, audit, plural routes, pure DTOs, and inward-only documentation. No internal S2S endpoint in this scope.
- **Scale/scope**: Two new read-only admin use cases plus a contract-compatible extension to the existing withdrawal decision use case. No new domain entity or financial ledger table is planned.

## GitNexus Impact and Preflight

Repository binding for implementation: the current worktree at `/Users/tringuyen/Visual Studio Code/WalletService.worktrees/wallet-phase2-plan-execution`, branch `agents/wallet-phase2-plan-execution`, indexed locally as `WalletService-Phase2` before impact analysis.

| Target | Impact result | Planning consequence |
|---|---|---|
| `WalletAccount` | HIGH; 4 direct dependents and 1 depth-2 dependent; top-up flow affected | Do not change wallet balance methods. This implementation only reads wallet balances for reconciliation. |
| `WalletTransaction` | HIGH; 15 impacted symbols, lower-bound result; VNPay IPN, checkout, transaction history, and test/support paths appear in the graph | Read existing records only. Do not change the entity or shared transaction semantics. |
| `ProcessWithdrawalCommandHandler` | UNKNOWN; no callers resolved | Source search confirmed dispatch through `AdminWalletsController` and direct tests in `WalletUseCaseBehaviorTests`, `WalletContractRegressionTests`, and `WalletOperationsEdgeCaseTests`. |
| `AdminWalletsController` | UNKNOWN; no callers resolved | Source search confirmed the controller's route/action and test coverage; preserve both existing actions and routes. |

GitNexus identifies shared financial flows around `LockFunds`, `Withdraw`, and `Deduct`, and the entity impact shows the top-up, wallet initialization, and top-up URL generation paths. The current impact report is planning evidence, not an edit authorization; rerun it against the then-current index before implementation. Do not waive HIGH or UNKNOWN risk.

Baseline check run:

```text
./scripts/verify-wallet-architecture.sh
OK: Wallet CQRS architecture is intact (15 handlers, 2 controllers, MediatR wired).
```

## Constitution Check

- [x] Preserve the existing withdrawal routes and status values; new API fields are additive and require plan approval before implementation.
- [ ] Preserve the frozen handler count as currently written. **Planned exception**: add two CQRS query handlers and update the handler-count invariant to 17 after explicit approval; keep two controllers and all other architecture constraints.
- [x] Add no feature-service facade, repository abstraction, or per-resource controller.
- [x] Preserve balance, ownership, authorization, transaction/locking, and audit invariants.
- [x] Follow `.agents/skills/new-feature-endpoint/SKILL.md`.
- [x] Present this plan for approval before implementation.
- [x] Keep P2-05 and Auction internal operations deferred; no unauthenticated internal routes.
- [x] Report only what existing transaction records can substantiate; do not call it double-entry or external bank reconciliation.

**Governance inconsistency to resolve in the approved implementation**: the architecture verifier and active guidance use 15 handlers, while `.specify/memory/constitution.md` and some Spec Kit artifacts still say 11. The source tree and verifier establish the current baseline as 15. Update active normative guidance to 17 (11 commands, 6 queries) and preserve historical feature records as historical records.

## Repository Structure and Expected Changes

### Application/API

- `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommand.cs` — accept the bank transfer reference; validate it when `Action` is `Approve`.
- `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommandHandler.cs` — persist the reference in the existing `WithdrawalRequest.GatewayReferenceId`; require manual transfer confirmation before approval; include the reference in the audit record and response. Redact/mask the bank account number in any transaction note rather than persisting the full number there.
- `Wallet.Application/Features/AdminWallets/Queries/ListWalletLedger/` — new read-only query, handler, and response contract for a filtered, paginated transaction report.
- `Wallet.Application/Features/AdminWallets/Queries/GetWalletReconciliation/` — new read-only query, handler, and response contract comparing wallet balance snapshots with existing successful transaction history.
- `Wallet.Api/Controllers/Public/AdminWalletsController.cs` — add the two query actions to the existing controller. Restrict these report actions to `admin`; the current class-level authorization also permits `staff`.
- `Wallet.Tests/` — extend withdrawal request/decision tests and add report, reconciliation, authorization, pagination, and failure-path tests.

### Documentation and governance

- `docs/api/` — document the withdrawal reference and the two administrator read routes, including filters, response shape, authorization, errors, and the reconciliation limitations.
- `specs/02_API_REST_CONTRACTS.md` and current architecture guidance — update the WalletService contract and accurately record the approved handler count.
- `AGENTS.md`, `.agents/AGENTS.md`, `.agents/rules/`, `.specify/memory/constitution.md`, active Spec Kit templates/skills, `README.md`, and `scripts/verify-wallet-architecture.sh` — synchronize current architecture invariants to 17 handlers and 2 public controllers. Do not rewrite historical feature plans solely to change their original baseline statements.

### Persistence

- No entity or table is planned. `WithdrawalRequest.GatewayReferenceId` already exists in the model and database.
- No migration is expected for the report. Inspect existing indexes and representative query plans before deciding whether an additive index is needed; do not add one without evidence and a migration review.

## Data and API Changes

### Withdrawal decision

- Keep `POST /api/v1/wallets/withdrawals` and `PATCH /api/v1/admin/wallets/withdrawals/{id}`.
- Extend the approval request with `referenceCode`; require a non-empty value of at most the existing 100-character persistence limit for `Approve`. It is not required for `Reject`.
- Store the value in `WithdrawalRequest.GatewayReferenceId`; include it in the direct response and audit data.
- Keep current `Approved`/`Rejected` status values. `Approve` means staff confirms the manual transfer has already occurred; it does not initiate a bank payout. A reference is required as evidence.
- Continue row-locking the request and wallet, committing balance, transaction, status, and audit changes atomically, and rejecting repeat decisions without a second debit.
- Mask the destination account number in the transaction note and exclude bank details from administrator ledger response DTOs.

### Administrator transaction report

- `GET /api/v1/admin/wallets/ledger`
- Admin-only; filters: `page`, `pageSize`, `walletId`, transaction `type`, `status`, `fromDate`, and `toDate`.
- Returns `PagedData<T>` using the established pagination metadata, database `CountAsync`, and stable `CreatedAt`/ID ordering.
- Return only fields needed for finance review: transaction ID, wallet ID, type, amount, currency, balance before/after, status, reference ID, and timestamp. Exclude bank account values and free-text notes.

### Reconciliation report

- `GET /api/v1/admin/wallets/reconciliation`
- Admin-only; paginated per-wallet results; no date filter because the snapshot comparison needs the latest successful transaction across the wallet's history. A `walletId` filter may narrow the result to one wallet.
- Compare `WalletAccount.Balance` with the latest successful `WalletTransaction.BalanceAfter`, ordered by `CreatedAt` and then transaction ID. Report `Balanced`, `Discrepancy`, or `InsufficientHistory`; a wallet with no successful transaction is never reported as balanced.
- This is a stored-balance-to-transaction-snapshot check only. It does not prove that records form a double-entry journal or reconcile a bank statement.

### Deferred endpoints

Do not implement purchase freeze/unfreeze/status or Auction deposit/refund/deduct/forfeit endpoints in this plan. Keep them out of Swagger and public routes until a separate plan approves the internal API authentication model, route contract, idempotency keys, and cross-service behavior.

## Implementation Sequence

1. **Approval and baseline**
   - Obtain approval for this plan and for the coordinated 15-to-17 handler governance update.
   - Reconcile the constitution's stale handler count with the verified 15-handler baseline.
   - Re-run the architecture verifier, Release build, tests, and exact GitNexus impacts against the implementation checkout.
2. **Withdrawal evidence**
   - Extend the existing approval command, validation, handler, DTO, and audit record with the bank transfer reference.
   - Mask account-number data in the withdrawal transaction note.
   - Add regression coverage for approve/reject, missing/oversized reference, retries, audit fields, and unchanged route/status behavior.
3. **Administrator ledger query**
   - Add a read-only CQRS query and action in `AdminWalletsController`.
   - Implement filters, authorization, deterministic ordering, and database pagination over existing `WalletTransaction` data.
   - Test filters, page boundaries, empty results, authorization, and omission of sensitive bank information.
4. **Wallet reconciliation query**
   - Add a read-only CQRS query and action comparing current wallet balances to the latest successful transaction snapshots.
   - Test balanced wallets, mismatched snapshots, wallets without transaction history, timestamp ties, and multiple currencies.
5. **Architecture and documentation alignment**
   - Update active architecture rules/verifier from 15 to 17 handlers (11 Command, 6 Query), while retaining exactly two public controllers.
   - Document the manual payout meaning, report semantics, pagination, and the explicit limits of reconciliation.
6. **Verification and handoff**
   - Run focused tests, architecture verifier, Release build, full test suite, Swagger inspection, and PostgreSQL query-plan review.
   - Review all changes for secrets, external links, generated build output, and contract drift; run GitNexus detect-changes before any commit.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Increase CQRS handlers from 15 to 17 | Two independent administrator read use cases are required and the frozen CQRS convention requires one handler per use case. | Folding unrelated report and reconciliation behavior into existing mutation handlers would violate CQRS and blur authorization/query semantics. |
| Add report endpoints to `AdminWalletsController` | Existing architecture requires keeping exactly the two controllers. | New per-resource controllers are explicitly forbidden. |
| Reconcile active architecture guidance from 11/15 to 17 | The verifier and source establish 15 as the current baseline; the constitution's 11 count is inconsistent. | Leaving different counts would make future verification and approvals unreliable. |

## Verification

Focused scenarios:

- Withdrawal approval with a valid reference consumes reserved funds once; rejection releases reserved funds once.
- Invalid/missing approval reference and duplicate decision produce the expected problem response and no additional financial effects.
- Ledger filters and pagination produce correct stable pages without exposing bank-account data.
- Reconciliation returns `Balanced`, `Discrepancy`, and `InsufficientHistory` for corresponding fixtures and does not write data.
- Non-admin, unauthenticated, invalid filter, and database-failure paths are covered.

Required final checks:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

Also inspect `/swagger/v1/swagger.json` for the two new admin routes and authorization documentation. Run the query tests against PostgreSQL and review query plans before adding any migration/index.

## Approval Gate

The user explicitly approved implementation on 2026-11-10, including the coordinated handler-count update to 17. Keep changes within this plan; preserve two controllers and all other frozen architecture, financial, and security invariants.
