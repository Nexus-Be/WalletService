# Tasks: Wallet Service Phase 2 Financial Operations

**Input**: Approved bounded plan addendum in `specs/002-wallet-phase2/plan.md`

**Current implementation scope**: User Story 3 only (admin balance adjustment by user ID). The broader withdrawal and reporting stories remain outside this approval.

## Phase 1: Preflight

- [x] T001 Confirm the scoped implementation plan is approved in `.agents/plan/PLAN-20261112-0827-ADMIN-ADJUSTMENT-USER-ID.md`.
- [x] T002 Record GitNexus impact for each changed symbol and confirm `UNKNOWN` results against source references.
- [x] T003 Run `./scripts/verify-wallet-architecture.sh` and record the existing architecture-verifier result without changing architecture counts.

## Phase 2: User Story 3 - Adjust a wallet balance using the owner's user ID (Priority: P2)

**Goal**: Authorized admin/staff callers can adjust a user's wallet without supplying a wallet ID.

**Independent Test**: A valid user-ID adjustment affects only that user's wallet; missing-wallet and insufficient-balance cases preserve financial records.

### Tests

- [x] T004 [US3] Update adjustment tests in `Wallet.Tests/WalletRequestValidatorTests.cs`, `Wallet.Tests/WalletOperationsEdgeCaseTests.cs`, `Wallet.Tests/WalletUseCaseBehaviorTests.cs`, and `Wallet.Tests/WalletContractRegressionTests.cs` to use `UserId`, cover lookup by owner ID and missing wallets, retain credit/debit and insufficient-balance assertions, and verify the admin route contract.

### Implementation

- [x] T005 [US3] Replace the server-filled `WalletId` target with validated `UserId` in `Wallet.Application/Features/AdminWallets/Commands/AdjustWalletBalance/AdjustWalletBalanceCommand.cs`.
- [x] T006 [US3] Resolve and lock the wallet by `request.UserId` in `Wallet.Application/Features/AdminWallets/Commands/AdjustWalletBalance/AdjustWalletBalanceCommandHandler.cs`, preserving the existing transaction, balance rules, audit, and response.
- [x] T007 [US3] Rename the route parameter to `userId` in `Wallet.Api/Controllers/Public/AdminWalletsController.cs` without changing the route shape or authorization.
- [x] T008 [P] [US3] Update the admin adjustment contract and example in `docs/api/wallet-payments.md`, `api/B_NONE/09_WALLET_PAYMENTS_API.md`, `specs/02_API_REST_CONTRACTS.md`, and `specs/002-wallet-phase2/contracts/admin-financial-operations.md`; preserve unrelated existing edits.

## Final Verification

- [x] T009 Run focused adjustment tests in `Wallet.Tests/Wallet.Tests.csproj`.
- [x] T010 Run `./scripts/verify-wallet-architecture.sh`.
- [x] T011 Run `dotnet build Wallet.sln -c Release`.
- [x] T012 Run `dotnet test Wallet.sln -c Release`.
- [x] T013 Verify route metadata exposes `userId` as the path parameter and review the route/body/response contract.
- [x] T014 Review the final diff and worktree for unrelated files and secrets; run GitNexus `detect-changes` before any commit.

## Dependencies and Execution Order

- T001-T003 are preflight gates.
- T004-T007 implement and test the single user story; complete these before final verification.
- T008 is documentation-only and may proceed independently of source changes.
- T009-T014 depend on T004-T008.
- No dependencies or implementation tasks are defined here for User Stories 1 and 2 because they are not part of this approval.

## Implementation Strategy

Deliver User Story 3 as the MVP. Do not change the existing handler or controller architecture. Do not begin withdrawal processing or ledger/reconciliation implementation under this task list.

## Phase 3: Convergence

- [ ] T015 Obtain separate governance approval and reconcile the 11-handler MUST in `.specify/memory/constitution.md` with the verified source/verifier baseline before changing architecture guidance, per Constitution I (contradicts).
- [ ] T016 After separate approval for the broader withdrawal scope, add and validate the manual transfer reference on withdrawal approval in `Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommand.cs`, `ProcessWithdrawalCommandHandler.cs`, and `Wallet.Tests/` per FR-002, US1/AC3, and SC-001 (partial).
- [ ] T017 After separate approval for the broader reporting scope, implement the admin ledger and reconciliation queries, endpoints, pagination, and tests in `Wallet.Application/Features/AdminWallets/Queries/`, `Wallet.Api/Controllers/Public/AdminWalletsController.cs`, and `Wallet.Tests/` per FR-003 and SC-002 (missing).
