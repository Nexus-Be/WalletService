# Tasks: [FEATURE NAME]

**Input**: Design documents from `specs/[###-feature-name]/`

**Prerequisites**: Approved `spec.md` and `plan.md`; add `research.md`,
`data-model.md`, or `contracts/` when relevant.

**Tests**: Automated tests for changed behavior are required. Place tests in
`Wallet.Tests`; do not mark them optional.

## Format

`[ID] [P?] [Story] Description`

- Include exact repository-relative file paths.
- Use `[P]` only for tasks that do not edit the same files or depend on one
  another.
- Group implementation and tests by independently verifiable user story.

## Phase 1: Preflight

- [ ] T001 Confirm the feature plan has been presented and approved before
      implementation.
- [ ] T002 Record GitNexus upstream impact for each changed symbol, including
      callers, processes, and risk; investigate `UNKNOWN` with text search.
- [ ] T003 Run `./scripts/verify-wallet-architecture.sh` and record baseline.

## Phase 2: User Story 1 - [Title] (Priority: P1)

**Goal**: [User-visible outcome]

**Independent Test**: [Test proving this story works]

### Tests

- [ ] T004 [P] [US1] Add or update tests in `Wallet.Tests/[exact test path]`
      for the acceptance scenarios and relevant failure cases.

### Implementation

- [ ] T005 [US1] Add or update the CQRS request, handler, and response under
      `Wallet.Application/Features/[Area]/[Commands|Queries]/[UseCase]/`.
- [ ] T006 [US1] Update the existing public controller action in
      `Wallet.Api/Controllers/Public/[WalletsController|AdminWalletsController].cs`
      only when an endpoint change is required.
- [ ] T007 [US1] Update domain/persistence files and add a migration only when
      the approved contract requires a schema change.
- [ ] T008 [US1] Update directly related API documentation and feature
      artifacts.

## Final Verification

- [ ] T009 Run focused tests for changed behavior.
- [ ] T010 Run `./scripts/verify-wallet-architecture.sh`.
- [ ] T011 Run `dotnet build Wallet.sln -c Release`.
- [ ] T012 Run `dotnet test Wallet.sln -c Release`.
- [ ] T013 For endpoint changes, verify the Swagger contract and document the
      final route, status codes, errors, and enums.
- [ ] T014 Run GitNexus `detect-changes` before any commit; inspect the final
      worktree for unrelated files and secrets.

## Notes

- Preserve the two-controller, 15-handler architecture unless the user
  explicitly approves changing it.
- Never create a feature service, repository/unit-of-work abstraction, or
  per-resource controller.
- Do not commit after individual tasks; follow the repository's review and
  commit workflow.
