# Implementation Plan: Comprehensive Request Validation

**Branch**: `001-comprehensive-request-validation` | **Date**: 2026-11-03 |
**Spec**: [spec.md](spec.md)

**Input**: Approved P1 task 2 from
`docs/operations/production-readiness.md`.

## Summary

Complete validation across the existing 11 MediatR requests and standardize
MVC and pipeline validation responses. Preserve valid behavior, routes,
callback response codes, and the frozen CQRS architecture.

## Technical Context

- **Runtime**: .NET 8 / ASP.NET Core
- **Architecture**: `Wallet.Api`, `Wallet.Application`, `Wallet.Domain`,
  `Wallet.Infrastructure`
- **Application pattern**: Classic CQRS/MediatR; `IWalletDbContext` in
  handlers
- **Persistence**: EF Core / PostgreSQL (`wallet_db`)
- **Tests**: `Wallet.Tests` (xUnit)
- **Constraints**: Exactly 11 handlers and two public controllers; no new
  routes, use cases, or persistence abstractions.

## GitNexus Impact and Preflight

Impact for the validation pipeline was `LOW`; request-record impacts were
ambiguous or `UNKNOWN` because the graph does not resolve every record and
constructor reference. This is not an all-clear: source search confirmed
handler, controller, and test references, which will be preserved and tested.
`WalletsController` was `UNKNOWN`; source confirms its API routes, sender calls,
and MVC controller registration. `RequestValidationException` was `LOW` with a
lower-bound dispatch limitation. The index is two commits behind. No HIGH or
CRITICAL impact was reported.

The approved baseline architecture is 11 handlers and two public controllers.

## Constitution Check

- [x] Preserve existing routes and valid HTTP/JSON behavior.
- [x] Add no feature service, repository abstraction, handler, or controller.
- [x] Preserve balance, ownership, authorization, transaction, audit, and
  payment callback behavior.
- [x] Validate requests through the existing MediatR pipeline.
- [x] Present plan for review before implementation; user approved.

## Repository Structure

Implementation remains within the existing layers:

```text
Wallet.Application/Common/Validation/       Request validation
Wallet.Application/Common/Behaviors/         MediatR validation
Wallet.Application/Common/Exceptions/        Validation exception
Wallet.Application/Features/                 Existing 11 request contracts
Wallet.Api/Common/                           Problem-details payload/factory
Wallet.Api/Middleware/                        Exception mapping
Wallet.Api/Controllers/Public/                Existing query boundary
Wallet.Tests/                                Validation and response tests
specs/001-comprehensive-request-validation/  Feature artifacts
```

## Data and API Changes

No schema, route, or successful response changes. Invalid pagination changes
from silent normalization to HTTP 400. Invalid DTOs and model-binding errors
share the validation problem-details response defined in `spec.md`. Existing
handler state checks and VNPay callback response codes remain unchanged.

## Verification

Run focused validation tests, then:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
git diff --check
node .gitnexus/run.cjs detect-changes --scope all --repo .
```

## Complexity Tracking

No constitution deviation or architecture change is planned.
