# WalletService specifications

This directory contains service architecture, persistence, and integration guidance. Source code is authoritative for runtime behavior; verify routes in `Wallet.Api/Controllers/Public/`, use cases in `Wallet.Application/Features/`, and schema in `Wallet.Infrastructure/Persistence/`.

| Document | Scope |
|---|---|
| [00 — Mission and guardrails](00_AGENT_MISSION_GUARDRAILS.md) | Wallet bounded context, architecture boundaries, and operational constraints |
| [01 — Clean architecture](01_CLEAN_ARCHITECTURE_LAYERS.md) | Project layers and dependency direction |
| [02 — REST contracts](02_API_REST_CONTRACTS.md) | WalletService routes, request ownership, and response conventions |
| [03 — Event messaging](03_EVENT_MESSAGING.md) | Current synchronous REST and in-process messaging boundaries |
| [04 — Data persistence](04_DATA_PERSISTENCE.md) | Entities, EF Core mappings, and database schema |
| [05 — Verification checklist](05_DEV_VERIFICATION_CHECKLIST.md) | Architecture, build, and test commands |
| [06 — Synchronous service integration](06_SYNCHRONOUS_SERVICE_INTEGRATION.md) | Wallet-side integration constraints and deployment verification |

Use the [WalletService API documentation](../docs/api/README.md) for route-specific contracts. The separate [ecosystem API catalog](../api/README.md) includes proposals and is not evidence that another service or route is implemented here.
