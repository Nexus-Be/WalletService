# WALLET SERVICE — AI AGENT MISSION PROTOCOL & RULES

> **Service:** WalletService (Port 8008, Database `wallet_db:5440`)
> **Scoping:** 100% Inward-Only. Never reference outward to root or sibling services.

## Local Governance Rules
Every AI Coding Agent working inside WalletService must strictly comply with the local rules in `.agents/rules/`:
1. [01 Clean Architecture Layers](rules/01_clean_architecture_layers.md): 4-layer structure, classic CQRS with MediatR (Wallet-only exception, frozen), non-negative balance invariant, No-Repository over EF Core.
2. [02 API Routing & Envelopes](rules/02_api_routing_and_envelopes.md): 100% Plural route convention, pure DTO return, zero ResultFilter.
3. [03 Security Headers & Internal API](rules/03_security_headers_and_internal.md): Public vs Internal controller segregation, `X-Internal-Service-Key`, wallet ownership.
4. [04 Audit & Financial Logging](rules/04_audit_and_event_logging.md): AuditableEntity tracking, balance modification audit trails.
5. [05 Error Handling & Enums](rules/05_error_handling_and_enums.md): 100% English sentence-case messages, SCREAMING_SNAKE_CASE error codes, PascalCase JSON enum serialization.
6. [06 Database & Migrations](rules/06_database_and_migrations.md): Database isolation, PostgreSQL integer enums, soft delete, deterministic seed GUIDs.

## Verification Checklist
- `./scripts/verify-wallet-architecture.sh` must print `OK: Wallet CQRS architecture is intact`.
- `dotnet build Wallet.sln -c Release` must succeed with 0 errors.
- `dotnet test Wallet.sln -c Release` must pass 100%.

> **Frozen:** 11 handlers, 3 controllers, MediatR wired. Never recreate `Wallet.Application/Services/{Feature}/` or per-resource controllers.
