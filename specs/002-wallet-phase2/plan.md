# Implementation Plan: Wallet Service Phase 2 Financial Operations

**Branch**: `feature/wallet-phase2-stripe` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: User selected P2-03 withdrawal completion and P2-04 admin transaction reporting/reconciliation. P2-05 purchase freeze and Auction escrow remain deferred pending an approved internal-service security contract. Stripe checkout/top-up flows are already migrated and are not changed by this work.

## Summary

Extend the existing withdrawal flow to require and persist manual bank-transfer evidence at approval, and add read-only administrator transaction and reconciliation queries over the current wallet data. Preserve existing withdrawal routes/statuses, both public controllers, CQRS/MediatR, and the direct `IWalletDbContext` pattern. Do not introduce a double-entry accounting claim or bank-statement reconciliation.

The implementation adds two CQRS queries, taking the verified baseline of 15 handlers (11 commands and 4 queries) to 17 (11 commands and 6 queries). The user explicitly authorized this count change and approved this concrete plan before source edits. Preserve exactly two controllers, MediatR, direct `IWalletDbContext` access, and all other frozen constraints.

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

Repository: `/Users/tringuyen/Visual Studio Code/WalletService`; implementation branch: `feature/wallet-phase2-stripe`. GitNexus was refreshed against the current checkout before this plan. The worktree contains Phase 2 specification/planning changes only; no source files have changed.

| Target | Impact result | Planning consequence |
|---|---|---|
| `WalletTransaction` | CRITICAL, lower-bound; 18 impacted symbols, 13 direct dependents, 5 processes. Affected flows include Stripe direct preparation, Stripe webhook processing, Stripe top-up, and settlement. | Add read-only query consumers only. Do not edit the entity or its mapping, and do not touch Stripe settlement flows. Run financial regression tests. |
| `WalletAccount` | LOW, exact; 6 impacted symbols, 4 direct dependents; no affected processes. | Read the existing balance only. Do not change wallet balance methods. |
| `ProcessWithdrawalCommandHandler` | UNKNOWN; no callers resolved. | Text search confirms controller dispatch and direct tests in `WalletUseCaseBehaviorTests`, `WalletContractRegressionTests`, and `WalletOperationsEdgeCaseTests`. Preserve the existing transaction, row locks, and one-time state transition. |
| `AdminWalletsController` | UNKNOWN; no callers resolved. | Text search confirms its actions and API/test references. Keep both existing actions and routes unchanged except the additive reference contract and new report actions. |
| `ProcessWithdrawalCommand` | UNKNOWN/ambiguous record-constructor result; no callers resolved. | Text search confirms construction in validation, behavior, regression, and edge-case tests. Preserve existing `Action` and `AdminNote` semantics. |
| `ProcessWithdrawalResponse` | LOW impact for the record candidate; one direct relationship. | Add the reference as an additive nullable response field and update contract tests. |

The index is current at planning time. The CRITICAL `WalletTransaction` result is a warning, not an all-clear: the report queries touch a highly shared financial record type, so the change must remain read-only and must not alter shared settlement behavior. Re-run exact impacts immediately before implementation. Treat UNKNOWN results as unresolved graph coverage and confirm call sites with source search.

Baseline check run:

```text
./scripts/verify-wallet-architecture.sh
OK: Wallet CQRS architecture is intact (15 handlers, 2 controllers, MediatR wired).
```

## Constitution Check

- [x] Preserve the existing withdrawal routes and status values; new API fields are additive and require plan approval before implementation.
- [x] Apply the user-authorized exception to the handler count: add two CQRS query handlers and update the invariant to 17 (11 commands, 6 queries); keep two controllers and all other architecture constraints. The concrete plan was approved before source edits.
- [x] Add no feature-service facade, repository abstraction, or per-resource controller.
- [x] Preserve balance, ownership, authorization, transaction/locking, and audit invariants.
- [x] Follow `.agents/skills/new-feature-endpoint/SKILL.md`.
- [x] Present this plan for approval before implementation.
- [x] Keep P2-05 and Auction internal operations deferred; no unauthenticated internal routes.
- [x] Report only what existing transaction records can substantiate; do not call it double-entry or external bank reconciliation.

Update all active normative architecture guidance and the verifier to 17 handlers (11 commands, 6 queries). Preserve historical feature records as historical records; do not make unrelated Stripe migration or old feature-spec edits merely to replace historical handler counts.

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
- `AGENTS.md`, `.agents/AGENTS.md`, `.specify/memory/constitution.md`, `.agents/skills/controller-conventions/SKILL.md`, `.agents/skills/pr-review-audit/SKILL.md`, `.specify/templates/overrides/plan-template.md`, `.specify/templates/overrides/tasks-template.md`, `README.md`, `PRODUCTION_READINESS.md`, `specs/00_AGENT_MISSION_GUARDRAILS.md`, `specs/02_API_REST_CONTRACTS.md`, `specs/05_DEV_VERIFICATION_CHECKLIST.md`, and `scripts/verify-wallet-architecture.sh` — synchronize active architecture invariants to 17 handlers and 2 public controllers. Do not rewrite historical feature plans solely to change their original baseline statements.

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
- `fromDate` and `toDate` are inclusive ISO 8601 instants with offsets, normalized to UTC; reject ranges where `fromDate` is later than `toDate`.
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
   - Plan approval and user authorization for the 15-to-17 handler governance update are recorded.
   - Re-run the architecture verifier, Release build, tests, and exact GitNexus impacts on this branch before source changes.
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
| Synchronize active architecture guidance from 15 to 17 | The verifier and source establish 15 as the current baseline; the two approved queries raise it to 17. | Leaving different counts would make future verification and approvals unreliable. |

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

## Bounded contract addition: balance adjustment by user ID

This bounded user-ID adjustment was implemented under separate approval before the current Phase 2 scope was selected. It is included as existing feature history, not as new source work in this plan.

- Change only the admin balance-adjustment target from `walletId` to the owning `userId`. Keep the route shape `POST /api/v1/admin/wallets/{userId:guid}/adjustments`, renaming only the route parameter.
- Resolve and lock the wallet through the existing `IWalletDbContext.LockWalletByUserIdAsync` operation.
- Keep the request body, response DTO, role authorization, balance validation, transaction, audit behavior, and error semantics unchanged. Do not alter withdrawal decision identifiers.
- Add regression tests for user-ID lookup, missing wallets, and existing credit/debit invariants. Update the service API contract and caller example to describe the path value as a user ID.
- Do not add a use case, handler, entity, database schema, or migration; the frozen handler and controller counts remain unchanged.

GitNexus impact recorded during planning:

| Target | Impact | Consequence |
|---|---|---|
| `AdminWalletsController.AdjustBalance` | UNKNOWN; no callers resolved | Text search confirms the controller route and documentation references. External callers are not represented in the local graph, so preserve the route shape and explicitly document the identifier semantic change. |
| `AdjustWalletBalanceCommandHandler` | UNKNOWN; no callers resolved | Text search confirms direct test usage and controller dispatch. Preserve all financial behavior and add targeted handler tests. |
| `IWalletDbContext` | LOW; one direct relationship to `WalletDbContext`, lower-bound | No interface change is planned; reuse its existing `LockWalletByUserIdAsync` operation. |

## Approval Gate

The user selected P2-03/P2-04, explicitly approved the 15-to-17 handler-count amendment, and approved this plan. Source implementation is authorized within this scope. The bounded user-ID adjustment addendum was separately implemented and is not part of this branch's source scope.
