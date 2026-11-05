# WALLET SERVICE — AI AGENT MISSION PROTOCOL & RULES

> **Service:** WalletService (Port 8008, Database `wallet_db:5440`)
> **Scoping:** 100% Inward-Only. Never reference outward to root or sibling services.

## Local Governance Rules
Every AI Coding Agent working inside WalletService must strictly comply with the local rules in `.agents/rules/`:
1. [01 Clean Architecture Layers](rules/01_clean_architecture_layers.md): 4-layer structure, classic CQRS with MediatR (Wallet-only exception, frozen), non-negative balance invariant, No-Repository over EF Core.
2. [02 API Routing & Envelopes](rules/02_api_routing_and_envelopes.md): 100% Plural route convention, pure DTO return, zero ResultFilter.
3. [03 Security Headers & Internal API](rules/03_security_headers_and_internal.md): Public controllers; internal key check temporarily disabled, wallet ownership.
4. [04 Audit & Financial Logging](rules/04_audit_and_event_logging.md): AuditableEntity tracking, balance modification audit trails.
5. [05 Error Handling & Enums](rules/05_error_handling_and_enums.md): 100% English sentence-case messages, SCREAMING_SNAKE_CASE error codes, PascalCase JSON enum serialization.
6. [06 Database & Migrations](rules/06_database_and_migrations.md): Database isolation, PostgreSQL integer enums, soft delete, deterministic seed GUIDs.

## Endpoint Workflow
Before adding or changing an endpoint, follow `.agents/skills/new-feature-endpoint/SKILL.md`.

## Spec Kit Workflow
Spec Kit is installed with the GitHub Copilot integration. For a new feature or
multi-file change, use the `/speckit-*` skills and the project constitution at
`.specify/memory/constitution.md`:

1. `/speckit-specify` the bounded change; resolve ambiguity with
   `/speckit-clarify` when needed.
2. `/speckit-plan` and record the mandatory user-visible plan under
   `.agents/plan/PLAN-YYYYMMDD-HHMM-<NAME>.md`, linking to the Spec Kit plan in
   `specs/[###-feature-name]/plan.md`.
3. Present the plan for approval before code edits. Include file context,
   GitNexus upstream impact/risk, implementation steps, and verification.
4. Generate tasks with `/speckit-tasks`; run `/speckit-analyze` before
   `/speckit-implement`; finish with `/speckit-converge` and address remaining
   tasks.

Spec Kit does not replace the endpoint checklist, frozen architecture, or
verification gates below. Use the existing two controllers and CQRS handlers;
do not accept generated service/repository abstractions or optional tests.
Keep feature artifacts in `specs/[###-feature-name]/`, separate from the
numbered service-level documents.

## Verification Checklist
- `./scripts/verify-wallet-architecture.sh` must print `OK: Wallet CQRS architecture is intact`.
- `dotnet build Wallet.sln -c Release` must succeed with 0 errors.
- `dotnet test Wallet.sln -c Release` must pass 100%.

> **Frozen:** 11 handlers, 2 controllers, MediatR wired. Never recreate `Wallet.Application/Services/{Feature}/` or per-resource controllers.
