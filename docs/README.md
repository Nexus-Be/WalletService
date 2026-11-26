# WalletService documentation

| Area | Purpose |
|---|---|
| [Business use cases and flows](business-use-cases.md) | User stories, role boundaries, escrow model, request-to-response walkthroughs, and sequence diagrams (`00`–`09`) |
| [Escrow and order money flows](business-flows/04-wallet-orders.md) | Deep dive ([diagrams 4A & 4B](business-flows/04-wallet-orders.diagram.md)) on `OrderEscrow`, `AuctionDepositEscrow`, Settlement V2, subsidies, pre/post-payout refunds, and lock ordering |
| [Shadow ledger and reconciliation](business-flows/09-shadow-ledger-and-reconciliation.md) | Shadow ledger guard/posting pipeline and reconciliation invariants (`I1`–`I12`) |
| [Reconciliation catalog](RECONCILIATION_CATALOG.md) | Authoritative catalog of ledger accounts, invariants `I1`–`I12`, finding codes, and owner sign-off |
| [API reference](api/README.md) | Implemented WalletService routes, endpoint contracts, and proposals |
| [Integration notes](integrations/README.md) | Wallet-side handoff context and integration boundaries |
| [Operations](operations/production-readiness.md) | Production readiness, deployment boundaries, and remaining risks |
| [Architecture specs](../specs/README.md) | Service-specific architecture, persistence, and verification guidance |
| [Ecosystem API catalog](../api/README.md) | Cross-service API notes inside this repository, separated from verified WalletService handlers |

`README.md` at the repository root is the entry point for setup and verification. API behavior verified against this repository is documented under [docs/api/](api/README.md).
