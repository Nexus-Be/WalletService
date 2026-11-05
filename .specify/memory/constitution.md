# WalletService Constitution

## Core Principles

### I. Preserve the Frozen Architecture
WalletService remains a .NET 8 REST service using the existing four-layer
architecture and classic CQRS through MediatR. Keep exactly 11 handlers and the
two public controllers (`WalletsController` and `AdminWalletsController`). Do
not add feature-service facades, repository abstractions, or per-resource
controllers. Any requested change to these invariants requires explicit user
approval and a coordinated update to the architecture verifier and governing
documentation.

### II. Keep Business Logic in CQRS Handlers
Each use case belongs in `Wallet.Application/Features/{Area}/Commands` or
`Queries`, and handlers use `IWalletDbContext` directly. Queries remain
read-only; controllers stay thin, inject `ISender` and `ILogger`, and dispatch
requests through MediatR. Do not query the database or implement business
branching in controllers.

### III. Protect Balances, Ownership, and Auditability
No operation may create a negative available wallet balance. Balance-changing
operations must use the established database transaction and locking patterns,
and record required audit data. Derive user identity from trusted server-side
claims or headers, never from request bodies; enforce existing admin/staff
authorization on administrative operations.

### IV. Preserve the REST Contract and Service Boundary
Keep existing routes, HTTP status codes, and JSON contracts unless the user
explicitly approves a contract change. Use plural collection routes, English
sentence-case error messages, SCREAMING_SNAKE_CASE error codes, and PascalCase
enum values in API responses. Keep service communication RESTful; do not
reintroduce gRPC, RabbitMQ, MassTransit, or outbox/inbox infrastructure. Keep
secrets out of source control.

### V. Plan, Test, and Verify Every Change
Before editing code, present a plan containing the objective, relevant files,
GitNexus upstream impact, implementation steps, and verification. Treat
GitNexus `UNKNOWN` risk as unresolved and confirm references with text search.
Add or update tests for changed behavior; tests are not optional for
implementation work. Complete the architecture verifier, Release build, and
full test suite before declaring implementation complete.

## Project Constraints

- Runtime: .NET 8 / ASP.NET Core; persistence: EF Core and PostgreSQL.
- API port: `8008`; local PostgreSQL host port: `5440`; database: `wallet_db`.
- Existing endpoint changes must follow
  `.agents/skills/new-feature-endpoint/SKILL.md`.
- Read `AGENTS.md`, `.agents/AGENTS.md`, `.agents/rules/`, and the applicable
  files under `specs/` before planning. Source code is authoritative when
  documentation conflicts with the implementation.
- Keep all project documentation and paths inward-only within this repository.

## Development Workflow

For a feature or multi-file change, use the Copilot Spec Kit skills in order:
`/speckit-specify`, `/speckit-clarify` when requirements are ambiguous,
`/speckit-plan`, `/speckit-tasks`, `/speckit-analyze`, `/speckit-implement`,
and `/speckit-converge` until no work remains. Review and present the plan
before implementation. Keep the required session plan under `.agents/plan/`
and link it to the Spec Kit plan under `specs/`.

Before completion, run:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

## Governance

This constitution supplements and does not replace `AGENTS.md`,
`.agents/AGENTS.md`, the local rules, or the endpoint skill. In a conflict,
stop and ask the user rather than weakening a frozen invariant. Amendments to
architecture, public API contracts, or financial invariants require explicit
user approval; update the affected rules, templates, verifier, and tests in the
same approved change. Review Spec Kit artifacts against the implementation
before merging.

**Version**: 1.0.0 | **Ratified**: 2026-11-03 | **Last Amended**: 2026-11-03
