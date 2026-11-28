# Tasks: Wallet Service Phase 2 Financial Operations

**Input**: Approved [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts](contracts/), and [quickstart.md](quickstart.md)

**Current scope**: P2-03 withdrawal completion and P2-04 administrator ledger and reconciliation. The user authorized increasing the architecture count from 15 to 17 handlers. Stripe checkout/top-up changes, P2-05 internal operations, and Auction escrow are out of scope.

## Previously completed: User Story 3 - Adjust a wallet balance using the owner's user ID

The bounded adjustment work T001-T014 was approved and completed before this branch; it covers FR-008 and SC-004 and is present in the base branch. It is not reimplemented here.

- [x] T001 Confirm the scoped implementation plan was approved in `.agents/plan/PLAN-20261112-0827-ADMIN-ADJUSTMENT-USER-ID.md`.
- [x] T002 Record GitNexus impact for each changed symbol and confirm UNKNOWN results against source references.
- [x] T003 Run `./scripts/verify-wallet-architecture.sh` and record the existing architecture-verifier result without changing architecture counts.
- [x] T004 [US3] Update adjustment tests in `Wallet.Tests/WalletRequestValidatorTests.cs`, `Wallet.Tests/WalletOperationsEdgeCaseTests.cs`, `Wallet.Tests/WalletUseCaseBehaviorTests.cs`, and `Wallet.Tests/WalletContractRegressionTests.cs` to use `UserId`, cover lookup by owner ID and missing wallets, retain credit/debit and insufficient-balance assertions, and verify the admin route contract.
- [x] T005 [US3] Replace the server-filled `WalletId` target with validated `UserId` in `Wallet.Application/Features/AdminWallets/Commands/AdjustWalletBalance/AdjustWalletBalanceCommand.cs`.
- [x] T006 [US3] Resolve and lock the wallet by `request.UserId` in `Wallet.Application/Features/AdminWallets/Commands/AdjustWalletBalance/AdjustWalletBalanceCommandHandler.cs`, preserving the existing transaction, balance rules, audit, and response.
- [x] T007 [US3] Rename the route parameter to `userId` in `Wallet.Api/Controllers/Public/AdminWalletsController.cs` without changing the route shape or authorization.
- [x] T008 [P] [US3] Update the admin adjustment contract and example in `docs/api/wallet-payments.md`, `api/B_NONE/09_WALLET_PAYMENTS_API.md`, `specs/02_API_REST_CONTRACTS.md`, and `specs/002-wallet-phase2/contracts/admin-financial-operations.md`; preserve unrelated existing edits.
- [x] T009 Run focused adjustment tests in `Wallet.Tests/Wallet.Tests.csproj`.
- [x] T010 Run `./scripts/verify-wallet-architecture.sh`.
- [x] T011 Run `dotnet build Wallet.sln -c Release`.
- [x] T012 Run `dotnet test Wallet.sln -c Release`.
- [x] T013 Verify route metadata exposes `userId` as the path parameter and review the route/body/response contract.
- [x] T014 Review the final diff and worktree for unrelated files and secrets; run GitNexus `detect-changes` before any commit.

## Phase 1: Preflight

- [x] T015 Run `./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release`, and `dotnet test Wallet.sln -c Release` on `feature/wallet-phase2-stripe`; record the 15-handler baseline and any pre-existing failures.
- [x] T016 Re-run GitNexus impact for each source symbol to change and confirm UNKNOWN results with text search; record caller, process, and risk details in `specs/002-wallet-phase2/plan.md`.

## Phase 2: User Story 1 - Request and process a seller withdrawal (Priority: P1)

**Goal**: Staff approvals record manual bank-transfer evidence, while approvals and rejections preserve the existing one-time balance and audit behavior.

**Independent Test**: Approve with a valid reference and reject without one; verify the reference, state transition, audit, masked transaction note, and no duplicate balance effects on retry.

### Tests

- [x] T017 [US1] Add approval-reference, optional-rejection-reference, retry, audit, and masked-bank-account assertions in `Wallet.Tests/WalletRequestValidatorTests.cs`, `Wallet.Tests/WalletOperationsEdgeCaseTests.cs`, `Wallet.Tests/WalletUseCaseBehaviorTests.cs`, and `Wallet.Tests/WalletContractRegressionTests.cs`; preserve existing request-boundary and available-balance reservation coverage.

### Implementation

- [x] T018 [US1] Add `referenceCode` validation to `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommand.cs`; require a non-blank value of at most 100 characters only for `Approve`.
- [x] T019 [US1] Persist the approval reference in `WithdrawalRequest.GatewayReferenceId`, include it in `ProcessWithdrawalResponse` and audit data, and mask the bank account number in transaction notes in `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommandHandler.cs`; preserve locking, transactionality, statuses, and retry protection.
- [x] T020 [US1] Update `specs/002-wallet-phase2/contracts/admin-financial-operations.md`, `specs/02_API_REST_CONTRACTS.md`, and `docs/api/wallet-payments.md` to document approval/rejection reference rules, response, audit, and manual-payout semantics.

## Phase 3: User Story 2 - Review wallet transactions and balance reconciliation (Priority: P1)

**Goal**: Admin callers can inspect a bounded report over existing wallet transactions and identify wallet balances that do not match their latest successful transaction snapshots.

**Independent Test**: Query ledger pages and verify every reconciliation result (`Balanced`, `Discrepancy`, `InsufficientHistory`) without changing source records; non-admin callers are denied.

### Tests

- [x] T021 [US2] Add ledger filters, stable ordering, database pagination, empty-page, authorization, and sensitive-field exclusion tests in `Wallet.Tests/WalletLedgerQueryTests.cs` and `Wallet.Tests/WalletContractRegressionTests.cs`; verify inclusive UTC date bounds and rejection of reversed ranges.
- [x] T022 [P] [US2] Add reconciliation tests for matching/mismatched balances, missing successful history, timestamp ties, wallet filtering, authorization, pagination, and read-only behavior in `Wallet.Tests/WalletReconciliationQueryTests.cs`.

### Implementation

- [x] T023 [P] [US2] Add the read-only `ListWalletLedger` request, handler, and response under `Wallet.Application/Features/AdminWallets/Queries/ListWalletLedger/`; implement `page`, `pageSize`, `walletId`, `type`, `status`, `fromDate`, and `toDate` filters, `CountAsync`, stable `CreatedAt`/ID ordering, database pagination, and fields that omit bank data and notes.
- [x] T024 [P] [US2] Add the read-only `GetWalletReconciliation` request, handler, and response under `Wallet.Application/Features/AdminWallets/Queries/GetWalletReconciliation/`; compare each current wallet balance with its latest successful transaction snapshot ordered by `CreatedAt` and ID, returning `Balanced`, `Discrepancy`, or `InsufficientHistory`.
- [x] T025 [US2] Add `GET /api/v1/admin/wallets/ledger` and `GET /api/v1/admin/wallets/reconciliation` to `Wallet.Api/Controllers/Public/AdminWalletsController.cs`; keep exactly two controllers and restrict both new report actions to admin callers without changing existing admin/staff mutations.
- [x] T026 [US2] Update the architecture verifier and active 15-handler guidance to 17 handlers (11 commands, 6 queries) in `scripts/verify-wallet-architecture.sh`, `AGENTS.md`, `.agents/AGENTS.md`, `.specify/memory/constitution.md`, `.agents/skills/controller-conventions/SKILL.md`, `.agents/skills/pr-review-audit/SKILL.md`, `.specify/templates/overrides/plan-template.md`, `.specify/templates/overrides/tasks-template.md`, `README.md`, `PRODUCTION_READINESS.md`, `specs/00_AGENT_MISSION_GUARDRAILS.md`, `specs/02_API_REST_CONTRACTS.md`, and `specs/05_DEV_VERIFICATION_CHECKLIST.md`; preserve the two-controller/no-facade invariants and historical records.
- [x] T027 [US2] Document both admin read routes, filters, response fields, authorization, pagination, and reconciliation limitations in `specs/002-wallet-phase2/contracts/admin-financial-operations.md`, `specs/02_API_REST_CONTRACTS.md`, and `docs/api/wallet-payments.md`.

## Phase 4: Verification and convergence

- [x] T028 Run the focused withdrawal and admin reporting tests in `Wallet.Tests/Wallet.Tests.csproj`.
- [x] T029 Run `./scripts/verify-wallet-architecture.sh` and confirm exactly 17 handlers and 2 public controllers.
- [x] T030 Run `dotnet build Wallet.sln -c Release`.
- [x] T031 Run `dotnet test Wallet.sln -c Release`.
- [ ] T032 **Blocked in this environment**: run PostgreSQL-backed report/concurrency tests and inspect query plans when a disposable PostgreSQL test database is available. `WALLET_STRIPE_TEST_DATABASE` is unset, and the nine existing PostgreSQL integration tests were skipped.
- [x] T033 Inspect Swagger for both report routes, filters, and authorization; review the final diff/worktree for secrets, generated output, and outward links.

## Dependencies and Execution Order

- T015-T016 are preflight gates.
- T017-T020 complete the withdrawal story.
- T021-T027 complete the admin-report story; T023 and T024 are separate handlers, but both must be complete before T025 controller wiring and T026 architecture-count verification.
- T017 and T021-T022 may be developed independently because their test scenarios and files are separate. Do not edit `AdminWalletsController` in parallel with another task that modifies it.
- T028-T033 depend on the implementation and documentation tasks.

## Implementation Strategy

Deliver the withdrawal approval evidence first, then the read-only admin reports. Keep each story independently testable. Do not change Stripe payment processing, the balance-adjustment behavior already present on the base branch, or deferred internal/Auction APIs.
