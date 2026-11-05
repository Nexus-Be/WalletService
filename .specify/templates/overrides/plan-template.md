# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `specs/[###-feature-name]/spec.md`

## Summary

[Required behavior, compatibility boundaries, and proposed approach]

## Technical Context

- **Runtime**: .NET 8 / ASP.NET Core
- **Architecture**: `Wallet.Api`, `Wallet.Application`, `Wallet.Domain`,
  `Wallet.Infrastructure`
- **Application pattern**: Classic CQRS/MediatR; handlers use
  `IWalletDbContext` directly
- **Persistence**: EF Core / PostgreSQL (`wallet_db`)
- **Tests**: `Wallet.Tests` (xUnit)
- **Service/API ports**: API `8008`; local PostgreSQL `5440`
- **Constraints**: Preserve the 11-handler/two-controller architecture,
  existing REST contracts, balance safety, ownership, and security rules.

## GitNexus Impact and Preflight

Before source edits, record the upstream impact for every changed symbol:
callers, affected processes, and risk. `UNKNOWN` is unresolved; confirm with
text search and report the uncertainty. Run
`./scripts/verify-wallet-architecture.sh` and record the baseline result.

## Constitution Check

- [ ] Existing route, status-code, and JSON contracts are preserved or an
      explicit user-approved change is documented.
- [ ] The plan adds no feature-service facade, repository abstraction, or
      per-resource controller.
- [ ] Balance, ownership, authorization, transaction/locking, and audit
      invariants are preserved where applicable.
- [ ] Endpoint changes follow `.agents/skills/new-feature-endpoint/SKILL.md`.
- [ ] The plan is presented for review before implementation begins.

## Repository Structure

Use the existing project layout; do not create a parallel application layout:

```text
Wallet.Api/Controllers/Public/       Existing Wallets/AdminWallets controllers
Wallet.Application/Features/         CQRS commands, queries, and handlers
Wallet.Domain/                       Entities, enums, and business rules
Wallet.Infrastructure/Persistence/   EF Core context and configurations
Wallet.Tests/                        Automated tests
specs/[###-feature-name]/            Feature specification and plan artifacts
```

List the exact files expected to change and explain why each belongs to its
existing layer.

## Data and API Changes

Describe request/response compatibility, persistence and migration impact,
authorization/ownership, idempotency, balance invariants, and error behavior.
Use N/A with a reason where a category does not apply.

## Verification

List focused tests for the changed behavior and the required final checks:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

For route changes, include a Swagger contract check. Do not weaken the
repository-wide gates for a feature-specific plan.

## Complexity Tracking

Document any proposed deviation from the constitution and its reason. Stop for
explicit user approval before implementing an architecture, API-contract, or
financial-invariant change.
