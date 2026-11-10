# Tasks: Wallet Service Phase 2 Financial Operations

**Input**: Design documents from `specs/002-wallet-phase2/`

**Prerequisites**: Approved `spec.md` and `plan.md`; `research.md`,
`data-model.md`, `contracts/`, and `quickstart.md` define implementation scope.

**Tests**: Automated tests for changed behavior are required in `Wallet.Tests`.

## Phase 1: Preflight

- [X] T001 Confirm approval of `specs/002-wallet-phase2/plan.md`, including the
      coordinated 15-to-17 handler governance update, before implementation.
- [X] T002 Record fresh GitNexus impact, callers, processes, and risk for
      changed symbols; confirm UNKNOWN results with references in
      `Wallet.Tests/` and `Wallet.Api/Controllers/Public/`.
- [X] T003 Run `./scripts/verify-wallet-architecture.sh`,
      `dotnet build Wallet.sln -c Release`, and
      `dotnet test Wallet.sln -c Release` as the implementation baseline.

## Phase 2: User Story 1 - Request and process a seller withdrawal (Priority: P1)

**Goal**: Preserve the existing withdrawal flow while recording manual bank
transfer evidence on approval, masking account data, and preventing retries
from causing duplicate balance effects.

**Independent Test**: Approve and reject separate pending requests; verify
reference, actor, audit, masked note, locked/available balances, and retry
behavior.

### Tests

- [X] T004 [US1] Add conditional approval-reference validation tests for
      missing, blank, 100-character, and over-100-character values in
      `Wallet.Tests/WalletRequestValidatorTests.cs`.
- [X] T005 [US1] Add withdrawal amount-boundary, approval/rejection, reference
      persistence, audit, repeated-decision retry, and bank-account masking
      regression tests in `Wallet.Tests/WalletOperationsEdgeCaseTests.cs` and
      `Wallet.Tests/WalletUseCaseBehaviorTests.cs`; preserve the existing
      PostgreSQL row-lock path in
      `Wallet.Infrastructure/Persistence/WalletDbContext.cs`.

### Implementation

- [X] T006 [US1] Extend the request validation and response contract with
      nullable `ReferenceCode`, requiring a non-blank value of at most 100
      characters only for approval, in
      `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommand.cs`.
- [X] T007 [US1] Persist the confirmed transfer reference in
      `WithdrawalRequest.GatewayReferenceId`, include it in audit/response data,
      and mask the bank account number in the withdrawal transaction note in
      `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommandHandler.cs`.
- [X] T008 [US1] Preserve the existing PATCH route and status behavior while
      dispatching the additive reference field through
      `Wallet.Api/Controllers/Public/AdminWalletsController.cs`.
- [X] T009 [US1] Document manual-transfer confirmation, reference validation,
      response shape, stable status/errors, and account-data handling in
      `docs/api/admin-financial-operations.md` and
      `specs/02_API_REST_CONTRACTS.md`.

## Phase 3: User Story 2 - Review wallet transactions and balance reconciliation (Priority: P1)

**Goal**: Provide admin-only, read-only, database-paginated transaction and
per-wallet reconciliation reports over existing transaction records.

**Independent Test**: Verify ledger filters/pages and `Balanced`,
`Discrepancy`, and `InsufficientHistory` outcomes; confirm unauthorized callers
are rejected and source records remain unchanged.

### Tests

- [X] T010 [US2] Add ledger filter/date-range validation, page/page-size
      boundaries, empty and stable pages, sensitive-data exclusion,
      reconciliation outcomes/ties/multiple currencies, read-only behavior,
      and admin/staff authorization tests in
      `Wallet.Tests/AdminWalletReportingTests.cs` and
      `Wallet.Tests/RequireRoleAttributeTests.cs`.

### Implementation

- [X] T011 [US2] Implement the read-only, filtered, stable-ordered,
      database-paginated ledger query and pure response record under
      `Wallet.Application/Features/AdminWallets/Queries/ListWalletLedger/`.
- [X] T012 [US2] Implement the read-only paginated reconciliation query and
      response record under
      `Wallet.Application/Features/AdminWallets/Queries/GetWalletReconciliation/`,
      comparing each wallet with its latest successful transaction ordered by
      `CreatedAt` and transaction ID.
- [X] T013 [US2] Add admin-only ledger and reconciliation actions to
      `Wallet.Api/Controllers/Public/AdminWalletsController.cs` and ensure
      `RequireRoleAttribute` honors only its explicitly allowed roles in
      `Wallet.Api/Filters/RequireRoleAttribute.cs`.
- [X] T014 [US2] Document both report routes, filters, pagination, response
      fields, authorization, error behavior, and reconciliation limitations in
      `docs/api/admin-financial-operations.md` and
      `specs/02_API_REST_CONTRACTS.md`.

## Phase 4: Architecture and documentation alignment

- [X] T015 Update the active frozen-handler-count guidance from the verified
      baseline of 15 to 17 (11 commands, 6 queries), retain exactly two public
      controllers, and update the verifier output/count in `AGENTS.md`,
      `.agents/AGENTS.md`, `.agents/rules/01_clean_architecture_layers.md`,
      `.agents/skills/new-feature-endpoint/SKILL.md`,
      `.agents/skills/controller-conventions/SKILL.md`,
      `.specify/memory/constitution.md`,
      `.github/skills/speckit-implement/SKILL.md`,
      `scripts/verify-wallet-architecture.sh`, and `README.md`.
- [X] T016 Update the active endpoint inventory and feature handoff in
      `specs/02_API_REST_CONTRACTS.md`, `docs/api/README.md`, and
      `docs/api/admin-financial-operations.md`; leave historical feature
      records unchanged.

## Final Verification

- [ ] T017 Run focused withdrawal, query, validation, and role tests in
      `Wallet.Tests/`.
- [ ] T018 Run `./scripts/verify-wallet-architecture.sh`.
- [ ] T019 Run `dotnet build Wallet.sln -c Release`.
- [ ] T020 Run `dotnet test Wallet.sln -c Release`.
- [ ] T021 Inspect `/swagger/v1/swagger.json` and review PostgreSQL query plans
      for the new routes in `Wallet.Api/` and
      `Wallet.Application/Features/AdminWallets/Queries/`; add no migration or
      index without query-plan evidence.
- [ ] T022 Run GitNexus `detect-changes` and inspect `git status` for unrelated
      files, secrets, and generated output before any commit.

## Dependencies and Execution Order

- Preflight tasks T001-T003 must complete before source implementation.
- User Story 1 tests T004-T005 precede its implementation T006-T008.
- User Story 2 tests T010 precede its query/controller implementation T011-T013.
- T009 and T014 document their corresponding completed behavior.
- Architecture alignment T015-T016 follows both stories so the final handler
  and endpoint inventory is accurate.
- Final verification T017-T022 follows implementation and documentation.
- P2-05 purchase-freeze and Auction escrow endpoints remain deferred and are
  not part of this task list.

## Parallel Execution

- T004 and T005 may be implemented in parallel because they edit separate test
  files; implementation tasks wait for both.
- T011 and T012 may be implemented in parallel after T010 because they use
  separate query folders; T013 follows both so the controller can dispatch
  their final request/response types.
- T009 and T014 share a documentation file and must be applied sequentially.

## Implementation Strategy

Complete the withdrawal evidence slice first as the minimal independently
verifiable increment. Then deliver both administrator reports, align the
active architecture guidance/verifier, and run all required verification
before handoff.

## Phase 5: Convergence

- [ ] T023 Add a PostgreSQL-backed test that processes the same pending
      withdrawal decision simultaneously from separate contexts and verifies
      exactly one balance transition, transaction, and audit record per
      SC-003/FR-005 (partial).
- [ ] T024 Add report-query persistence-failure tests and verify database
      exceptions are surfaced rather than returned as successful empty reports
      per plan: verification (partial).
- [ ] T025 Review PostgreSQL query plans for the ledger and reconciliation
      reports when a test database is available; record the results and do not
      add indexes without query-plan evidence per plan: verification (partial).
