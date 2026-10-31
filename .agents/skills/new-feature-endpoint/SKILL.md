---
name: new-feature-endpoint
description: Add or change a REST endpoint in WalletService (port 8008). Use when adding a Wallet CQRS command or query, a controller action, a wallet transaction contract, or a database field, or when changing an existing wallet endpoint.
---

# New Endpoint in WalletService

> Scope: this repository only. Never reference the parent workspace, its documentation, or a sibling service.
> Synced with the root `new-feature-endpoint` skill on 2026-10-31.

## Local facts

- Service: WalletService
- HTTP port: 8008
- Database: `wallet_db` on host port `5440`
- Solution: `Wallet.sln`
- Tests: `Wallet.Tests` (xUnit)
- DbContext contract: `IWalletDbContext`
- Controllers: `Wallet.Api/Controllers/Public/`
- Application features: `Wallet.Application/Features/{Area}/`
- API contract spec: `specs/02_API_REST_CONTRACTS.md`
- Architecture verifier: `scripts/verify-wallet-architecture.sh`

## Read first

- `AGENTS.md`, including the architecture freeze section.
- `.agents/rules/01_clean_architecture_layers.md` through `.agents/rules/06_database_and_migrations.md`.
- `specs/00_AGENT_MISSION_GUARDRAILS.md` through `specs/05_DEV_VERIFICATION_CHECKLIST.md`.
- The nearest existing command or query, its handler, and its controller action.
- `scripts/verify-wallet-architecture.sh`.

## Checklist

### 0. Preflight

- [ ] Read the files above.
- [ ] Run `./scripts/verify-wallet-architecture.sh` and confirm the baseline is intact.
- [ ] Run GitNexus impact for every symbol to change and record the risk.
- [ ] Confirm the contract: method, route, RBAC, ownership, amount rules, idempotency, database change.
- [ ] Confirm whether the change adds a use case. Adding one changes the frozen count of 11, so get explicit approval first.
- [ ] Create `.agents/plan/PLAN-YYYYMMDD-HHMM-<NAME>.md`.
- [ ] Stop and ask if the contract is ambiguous.

### 1. Contract

- [ ] Route is `/api/v1/wallets/...` or `/api/v1/admin/wallets/...`; collections are plural.
- [ ] One route attribute per action; no alias.
- [ ] Request and response records are defined next to the use case.
- [ ] Status codes and error codes are listed.
- [ ] Error codes are SCREAMING_SNAKE_CASE; messages are English sentence case.
- [ ] Enums are documented with integer database value and PascalCase API value.
- [ ] List endpoints use `page` and `pageSize` and return `PagedData<T>`.
- [ ] Internal API stays off: no `/api/v1/internal`, no `Controllers/Internal/`, no `X-Internal-Service-Key`.

### 2. Persistence

- [ ] Entity inherits `BaseEntity<Guid>` or `AuditableEntity<Guid>`.
- [ ] Enum values are explicit integers from 0; no `HasConversion<string>()`.
- [ ] Money columns use `HasPrecision(18, 2)`.
- [ ] Configuration has keys, indexes, soft delete filter, and `DeleteBehavior.Restrict` or `SetNull`.
- [ ] Migration is additive, timestamped, and the snapshot and designer are synchronized.
- [ ] Seeds use deterministic GUIDs and BCrypt hashes.
- [ ] No DROP, TRUNCATE, or RENAME without approval.
- [ ] Skip this phase with a written reason when no schema changes.

### 3. CQRS use case

- [ ] Create `Wallet.Application/Features/{Area}/Commands|Queries/{UseCase}/`.
- [ ] Add `{UseCase}Command.cs` or `{UseCase}Query.cs` implementing `IRequest<TResponse>`.
- [ ] Add `{UseCase}Handler.cs` implementing `IRequestHandler<TRequest, TResponse>`.
- [ ] Add the response record in the same folder.
- [ ] Handler injects `IWalletDbContext` directly. No repository and no unit of work.
- [ ] Reads use `AsNoTracking()`. Writes load tracked entities and save once.
- [ ] Balance changes use one transaction and never allow a negative balance.
- [ ] Sensitive mutations call `AddAuditLog(...)`.
- [ ] Errors use stable codes and English messages.
- [ ] Do not create `I{Feature}Service` or `{Feature}Service` facade.

### 4. Api

- [ ] Keep the existing controller set. The repository currently has `WalletsController` and `AdminWalletsController`; do not split controllers per resource.
- [ ] `Controllers/Internal/` stays absent while the internal API is off.
- [ ] Controller injects `ISender` and `ILogger` only.
- [ ] Action builds the command or query and calls `_sender.Send(...)`.
- [ ] Server-filled fields (`UserId`, `AdminUserId`, `ClientIp`) are `[JsonIgnore]` and injected with `command with { ... }`.
- [ ] One route attribute per action and one-line XML `<summary>`.
- [ ] Identity comes from `this.TryGetUserId(...)`; admin actions use the role filter.
- [ ] DTOs are returned directly; delete returns `NoContent()`.
- [ ] No `ResultFilter`, no manual envelope, no business branching in the controller.

### 5. Tests

- [ ] Happy path and every error code are covered.
- [ ] Insufficient balance and negative-balance prevention are covered.
- [ ] Pagination boundaries and ownership `401`/`403` are covered.
- [ ] Enum serialization and Swagger parameters are covered.
- [ ] Run `dotnet test Wallet.sln -c Release`.

### 6. Verify

- [ ] `./scripts/verify-wallet-architecture.sh` prints the intact message.
- [ ] `dotnet build Wallet.sln -c Release` has 0 errors.
- [ ] `dotnet test Wallet.sln -c Release` passes 100%.
- [ ] Fetch `/swagger/v1/swagger.json` and confirm the new route and parameters are present.
- [ ] Confirm `git status` has no build output or secret.
- [ ] Run GitNexus `detect_changes` before commit.

### 7. Handoff

- [ ] Tick the session plan.
- [ ] Append the session summary to `.agents/history/YYYY-MM-DD_session_summary.md`.
- [ ] Fill the contract handoff block (service, endpoint, controller, application, entity, RBAC, request, response, errors, enums, tests, build) and give it to the repository owner.
- [ ] Do not link outward from this repository.

## Variant

Wallet is the only CQRS service in Nexus. MediatR and the use-case layout are frozen. If the task requires a new use case, the count moves from 11 to 12; get explicit approval, update `AGENTS.md`, and update `scripts/verify-wallet-architecture.sh` in the same change. Never reintroduce feature services, never split controllers, and never remove MediatR.

## Commands

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

## Done when

- [ ] Architecture verifier prints the intact message.
- [ ] Build passes with 0 errors.
- [ ] Tests pass 100%.
- [ ] No forbidden pattern is present.
- [ ] The repository owner has the contract handoff block.
