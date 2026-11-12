# Tasks: Stripe payment migration

**Input**: [spec.md](spec.md), [plan.md](plan.md),
[data-model.md](data-model.md), [contracts/payments.md](contracts/payments.md)

## Phase 1: Preflight

- [ ] T001 Confirm and record approval of the final Stripe design in `specs/003-stripe-migration/plan.md`.
- [ ] T002 Record fresh upstream impact for every changed symbol in `specs/003-stripe-migration/plan.md`; resolve unknown graph results with text search.
- [X] T003 Run `scripts/verify-wallet-architecture.sh` and record the baseline in `specs/003-stripe-migration/plan.md`.
- [ ] T004 Finalize the pre-code endpoint contract and error matrix in `docs/api/stripe/payments.md`.

## Phase 2: Foundational persistence and provider integration

- [ ] T005 Add attempt/event schema tests in `Wallet.Tests/StripePersistenceTests.cs` for unique provider identifiers, event IDs and active attempts.
- [X] T006 Add attempt and event entities in `Wallet.Domain/Entities/`, with UUID IDs, positive whole-VND amounts, immutable fingerprints, nullable unique session and PaymentIntent IDs and unique event IDs. (EF guards identity updates; raw SQL must preserve them.)
- [X] T007 Add state enums in `Wallet.Domain/Enums/` without reinterpreting existing persisted enum values.
- [ ] T008 Add EF mappings, transactional lock helpers and additive migration in `Wallet.Infrastructure/Persistence/` and `Wallet.Application/Interfaces/IWalletDbContext.cs`; preserve legacy schema without destructive approval. (ID-based helpers and nullable historical references added; concurrent PostgreSQL lock verification remains.)
- [X] T009 Define Stripe provider contracts in `Wallet.Application/Interfaces/` and implement bounded, cancellable Checkout Session calls in `Wallet.Infrastructure/Payments/`. (Session creation only; reconciliation remains T022.)
- [ ] T010 Configure the pinned Stripe SDK and environment-only secrets in `Wallet.Infrastructure/Wallet.Infrastructure.csproj`, `Wallet.Infrastructure/DependencyInjection.cs`, `Wallet.Api/appsettings.example.json` and `docker-compose.yml`. (Checkout configuration added; webhook secret validation remains with T021.)

## Phase 3: User Story 1 — Wallet top-ups

**Independent test**: A paid top-up credits the owner exactly once; unpaid,
failed and duplicate events do not credit it.

- [X] T011 [US1] Add top-up initiation, ownership and amount tests. (Preparation, status and controller/MediatR tests cover ownership, boundaries and idempotency.)
- [X] T012 [US1] Replace VNPay top-up command and handler under `Wallet.Application/Features/Topups/Commands/` with durable, idempotent Stripe attempts.
- [X] T013 [US1] Replace the VNPay return query with the owner-protected top-up status query in `Wallet.Application/Features/Topups/Queries/`. (MediatR query and HTTP action wired; no-tracking ownership query also tested against PostgreSQL.)
- [X] T014 [US1] Update top-up and status actions in `Wallet.Api/Controllers/Public/WalletsController.cs` without changing the handler count.

## Phase 4: User Story 2 — Direct checkout

**Independent test**: Stripe settles an order once without modifying wallet
balance, and a terminal attempt can be retried only after provider confirmation.

- [X] T015 [US2] Add retry, conflicting fingerprint, ownership and balance tests. (Covered by Stripe preparation and endpoint tests.)
- [X] T016 [US2] Implement durable direct checkout attempts and safe retry in `Wallet.Application/Features/Checkout/Commands/CheckoutDirect/`.
- [X] T017 [US2] Update direct payment status mapping in `Wallet.Application/Features/Checkout/Queries/GetDirectPaymentStatus/`. (Stripe PaymentIntent mapping is implemented and PostgreSQL-tested.)
- [X] T018 [US2] Update the direct-checkout API action in `Wallet.Api/Controllers/Public/WalletsController.cs`.

## Phase 5: User Story 3 — Webhooks and recovery

**Independent test**: Verified duplicate, delayed, concurrent and out-of-order
events never duplicate financial effects; ambiguous provider outcomes remain
visible and recoverable.

- [ ] T019 [US3] Add raw-body signature, event replay, concurrency, late event and crash recovery tests in `Wallet.Tests/StripeWebhookTests.cs`. (Staged helper and real PostgreSQL concurrency/rollback tests added; HTTP and real Stripe tests remain.)
- [X] T020 [US3] Replace VNPay IPN handling with the Stripe webhook command in `Wallet.Application/Features/Stripe/Commands/`; atomically persist event, payment, ledger and audit state. (Reconciliation outcomes are durably recorded and logged at critical severity.)
- [X] T021 [US3] Implement raw request body and signature handling in `Wallet.Api/Controllers/Public/WalletsController.cs`; reject invalid signatures and propagate retryable failures.
- [ ] T022 [US3] Add provider confirmation and reconciliation handling for ambiguous or duplicate charges in `Wallet.Application/Features/Stripe/` and `Wallet.Infrastructure/Payments/`. (Provider reads, matching, transactional success/finality transitions and sanitized 503 provider errors are staged; recovery of unknown session creation and operator alerting remain.)

## Phase 6: Cutover and verification

- [ ] T023 Replace VNPay configuration, runtime references, seeds and tests under `Wallet.Api/`, `Wallet.Application/`, `Wallet.Infrastructure/` and `Wallet.Tests/`; retain immutable historical migration files. (Runtime routes, provider registration and configuration removed; historical entities and isolated legacy test fixtures remain.)
- [X] T024 Update `docs/api/wallet-payments.md`, `docs/api/stripe/payments.md`, example configuration and Stripe test-mode setup guidance.
- [ ] T025 Verify gateway raw-body forwarding, anonymous webhook routing and unenveloped acknowledgement; document consumer handoff in `docs/api/stripe/payments.md`.
- [ ] T026 Run targeted tests, PostgreSQL migration/concurrency tests and Stripe test-mode scenarios described in `specs/003-stripe-migration/quickstart.md`.
- [X] T027 Run `scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release` and `dotnet test Wallet.sln -c Release`. (Release build succeeded; 343 PostgreSQL-backed tests passed, with no pending EF model changes.)
- [X] T028 Inspect Swagger contracts and update `specs/003-stripe-migration/contracts/payments.md` with verified results. (Running API exposes both initiation routes, status and anonymous webhook with signature header; no VNPay route appears.)
- [ ] T029 Before any commit, run GitNexus change analysis and inspect `git status` for unintended files and secrets.
- [ ] T030 Verify and document Stripe account eligibility, VND presentment, enabled payment methods and transferable-wallet approval; block live launch until confirmed in `specs/003-stripe-migration/quickstart.md`.
- [ ] T031 Obtain and document an approved refunds, disputes and reversals policy before live launch in `specs/003-stripe-migration/quickstart.md`; automated handling remains outside this test-mode migration.
- [ ] T032 Add tests for late success after failure or expiry, unresolved attempts after idempotency retention, and manual escalation of extra charges in `Wallet.Tests/StripeWebhookTests.cs` and `Wallet.Tests/StripeDirectCheckoutTests.cs`. (A PostgreSQL test covers late success during replacement creation and durable escalation of a second paid attempt; retention and operator workflow remain.)
- [X] T033 Record the VNPay cutover decision. (User waived the drain requirement because all prior VNPay use was sandbox-only; this is not evidence that real transactions were reconciled.)
- [ ] T034 Verify Checkout Session recovery is disabled before treating session expiry as provider-final replacement evidence; add tests and carry the provider setting into the reconciliation snapshot. (Implemented with unit tests; real Stripe test-mode verification remains.)

## Dependencies and parallel work

T001–T004 precede implementation. T005–T010 are shared prerequisites for
all stories. Top-up and direct-checkout test design can proceed independently,
but shared persistence and controller edits must be sequenced. Webhook handling
depends on the attempt model and both payment flows. Cutover and end-to-end
verification require all three stories.
T030 and T031 are live-release gates and do not block test-mode implementation;
T032 is required before implementation is considered verified.
