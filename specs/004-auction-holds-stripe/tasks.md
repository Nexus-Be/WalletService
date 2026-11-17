# Tasks: Auction deposits and Stripe top-up recovery

**Input**: [Specification](spec.md) and [approved plan](plan.md).

## Phase 1: Preflight

- [x] T001 Confirm approval and baseline in `specs/004-auction-holds-stripe/plan.md`.
- [ ] T002 Record upstream impact for every edited symbol in `specs/004-auction-holds-stripe/plan.md`.
- [x] T003 Verify baseline with `scripts/verify-wallet-architecture.sh`.
- [x] T004 Write pre-code contracts in `docs/api/stripe/` and `docs/api/holds/`.

## Phase 2: Top-up recovery (US3)

**Independent test**: Multiple owned top-ups can be listed and resumed without
creating new charges; webhook and background reconciliation settle once.

- [x] T005 [US3] Integrate the existing top-up implementation into `Wallet.Application/Features/Topups/` and `Wallet.Api/Controllers/Public/WalletsController.cs`.
- [x] T006 [US3] Implement safe reconciliation and correlated return URLs in `Wallet.Application/Features/Stripe/` and `Wallet.Infrastructure/Payments/StripeCheckoutClient.cs`.
- [x] T007 [US3] Add ownership, pagination, resume, return URL and reconciliation tests in `Wallet.Tests/`.
- [x] T008 [US3] Update architecture gates and Stripe documentation in `scripts/verify-wallet-architecture.sh` and `docs/api/stripe/`.

## Phase 3: Reserve and release deposits (US1)

**Independent test**: Concurrent reservations and existing withdrawals cannot
exceed available funds; retries and reference lookups preserve one hold.

- [x] T009 [US1] Add hold entities, constraints and migration in `Wallet.Domain/` and `Wallet.Infrastructure/Persistence/`.
- [x] T010 [US1] Add scoped service-key authorization in `Wallet.Api/`.
- [x] T011 [US1] Implement create, release, status and buyer list handlers in `Wallet.Application/Features/Holds/`.
- [x] T012 [US1] Wire routes in `Wallet.Api/Controllers/Public/WalletsController.cs`.
- [x] T013 [US1] Add lifecycle, authorization, idempotency and concurrent debit tests in `Wallet.Tests/`.

Progress: create, release, reference lookup and buyer listing use the scoped
service-key and transactional idempotency foundation. PostgreSQL duplicate
and overspend races and live HTTP authorization were tested. Additional
frozen-account and concurrent withdrawal coverage was subsequently added.
Concurrent checkout, transfer and admin debit races were also verified.

## Phase 4: Capture and refund deposits (US2)

**Independent test**: Captures and refunds conserve funds and execute exactly
once, including conflicting concurrent requests.

- [x] T014 [US2] Implement transfer and deposit-specific escrow persistence in `Wallet.Domain/` and `Wallet.Infrastructure/Persistence/`.
- [x] T015 [US2] Implement capture and refund handlers in `Wallet.Application/Features/Holds/`.
- [x] T016 [US2] Wire capture and refund routes in `Wallet.Api/Controllers/Public/WalletsController.cs`.
- [x] T017 [US2] Test escrow, forfeiture, rollback and concurrency in `Wallet.Tests/`.

Progress: capture and refund are wired and exercised by HTTP and PostgreSQL
tests, including duplicate operations, frozen wallets, forfeiture,
capture-versus-release, escrow errors, rollback after a database failure and
opposing wallet lock order.

## Phase 5: Verification and handoff

- [x] T018 Audit all debit and withdrawal paths in `Wallet.Application/Features/` for locked funds.
- [x] T019 Run focused tests, PostgreSQL concurrency tests and migration checks in `Wallet.Tests/`.
- [x] T020 Run `scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release` and `dotnet test Wallet.sln -c Release`.
- [x] T021 Verify Swagger routes and update contracts under `docs/api/`.
- [x] T022 Run GitNexus change analysis before any commit; inspect worktree and report unverified external integration.

## Dependencies

T004 precedes all endpoint edits. US3 can be delivered independently. US1
requires the shared hold persistence and authorization foundation. US2
requires US1. Verification follows all relevant implementation tasks.

Verification on 2026-11-17: architecture check passed (26 handlers), Release
build passed with no warnings or errors, and 419 tests passed with no skips
against disposable PostgreSQL. The migration applied and the model snapshot
has no pending changes. Live HTTP exercised capture/refund and replay, service
key rejection and status; Swagger exposes both service and idempotency
headers. Stripe gateway acceptance remains external and unverified.
The pre-commit GitNexus analysis classified the change as critical because
financial and Stripe execution flows are affected; no partial or truncated
result was reported. Commerce escrow integration and uncertain Stripe session
creation without a stored session ID remain explicit operational gaps.
