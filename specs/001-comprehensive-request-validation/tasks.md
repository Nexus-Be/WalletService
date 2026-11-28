# Tasks: Comprehensive Request Validation

**Input**: Design documents from
`specs/001-comprehensive-request-validation/`

**Prerequisites**: Approved `spec.md` and `plan.md`

**Tests**: Automated tests for changed behavior are required in `Wallet.Tests`.

## Phase 1: Preflight

- [x] T001 Present and receive approval for the implementation plan.
- [x] T002 Record GitNexus impact and confirm unresolved request-record
      references with source search.
- [x] T003 Read the validation pipeline, all 11 request contracts, API problem
      response mapping, endpoint boundaries, and persistence field limits.

## Phase 2: User Story 1 - Reject invalid API inputs consistently (Priority: P1)

**Goal**: Validate all existing requests and provide field-level errors with a
consistent problem-details response.

**Independent Test**: Request and MVC validation tests cover every existing
request, response shape, and absence of side-effects for invalid requests.

### Tests

- [x] T004 [P] [US1] Expand `Wallet.Tests/WalletRequestValidatorTests.cs` to
      cover valid and invalid required, identifier, enum, amount/precision,
      date, pagination, and known-length boundaries across all 11 requests.
- [x] T005 [P] [US1] Add application pipeline tests for multiple validation
      errors, field association, stable error code, and handler non-execution.
- [x] T006 [P] [US1] Extend API validation tests for matching MVC and MediatR
      problem-details fields, content type, error dictionary, trace ID, and
      timestamp.
- [x] T007 [P] [US1] Cover query default values and invalid page/page-size
      rejection in `Wallet.Tests/WalletRequestValidatorTests.cs`.

### Implementation

- [x] T008 Add or complete DataAnnotations/request-level validation for the
      existing request contracts under `Wallet.Application/Features/`.
- [x] T009 Aggregate validation results into field-keyed errors in
      `Wallet.Application/Common/Behaviors/ValidationBehavior.cs` and
      `Wallet.Application/Common/Exceptions/RequestValidationException.cs`.
- [x] T010 Share one problem-details response model between
      `Wallet.Api/Common/ApiValidationProblemFactory.cs` and
      `Wallet.Api/Middleware/GlobalExceptionHandlerMiddleware.cs`.
- [x] T011 Remove silent invalid-pagination normalization in
      `Wallet.Api/Controllers/Public/WalletsController.cs` while preserving
      valid defaults.
- [x] T012 Update `specs/02_API_REST_CONTRACTS.md` and
      `docs/api/wallet-payments.md` with the validation response shape and
      pagination boundary behavior.

## Final Verification

- [x] T013 Run focused request-validation and API problem-details tests
      (26 passed).
- [x] T014 Run `./scripts/verify-wallet-architecture.sh`.
- [x] T015 Run `dotnet build Wallet.sln -c Release` (0 warnings, 0 errors).
- [x] T016 Run `dotnet test Wallet.sln -c Release` (88 passed).
- [x] T017 Run `git diff --check`, inspect worktree for unrelated files/secrets,
      and run GitNexus `detect-changes` before any commit.
