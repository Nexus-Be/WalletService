# Implementation Plan: Auction deposits and Stripe top-up recovery

**Branch**: `dev` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

## Summary

Implement Auction's wallet-funded hold lifecycle and frontend Stripe top-up
recovery. Retain the user's uncommitted Phase 2 work in the dev worktree.
Deposit escrow is isolated from Commerce's existing settlement and refund
flows until a separate integration contract is agreed.

## Technical Context

.NET 8, ASP.NET Core, EF Core/PostgreSQL and MediatR. Keep the two existing
controllers, one handler per use case, direct `IWalletDbContext` access,
database transactions and existing ProblemDetails error conventions.

## GitNexus Impact and Preflight

The dev worktree was indexed on 2026-11-17. Architecture verification passed
with 17 handlers and two controllers. The baseline tests passed 350, with nine
PostgreSQL tests skipped. GitNexus classified `IStripeCheckoutClient` as
CRITICAL (31 direct dependents), `WalletAccount` as MEDIUM, and
`StripeCheckoutClient` as LOW. Controller, settlement, lock, top-up preparation
and payment handler walks returned UNKNOWN; source searches establish actual
callers, including the webhook, checkout and withdrawal handlers. Run impact
for every additional changed symbol before editing; do not treat UNKNOWN as
safe. The checkout request constructor has LOW and UNKNOWN candidates and is
used by both direct checkout and top-up.

The `AddTopupContext` impact is HIGH: it is called by
`StripeCheckoutClient.CreateSessionAsync`, with four affected flows including
top-up and direct checkout preparation and handlers. Its change is confined to
requests carrying a top-up ID; direct-order compatibility is tested.
`ReconcilePendingTopupsCommandHandler.Handle` impact is UNKNOWN because
MediatR dispatch is not resolved by the graph; the worker sends its command.

For the hold persistence foundation, `IWalletDbContext` impact is LOW but
limited by interface dispatch; `WalletDbContext` is MEDIUM (seven direct
imports, no indexed processes). `WalletAccountConfiguration.Configure` and
`TestWalletDbContext` are UNKNOWN: assembly scanning applies the former and
the latter is used across the test suite. The schema change is additive and
the new wallet check constraint may reject existing invalid balance data,
which must be assessed before deploying the migration. The user confirmed
that there is no production data because the project has not entered
production; this does not establish the state of any development database.
The generated `WalletDbContextModelSnapshot` impact is UNKNOWN; EF Core
discovers it by metadata, and the migration snapshot was validated with
`dotnet ef migrations has-pending-model-changes` and PostgreSQL migration tests.
The existing `LockOrderPaymentAsync` method has HIGH impact (direct and Stripe
checkout flows); it was not changed. Auction locking uses a separate
transaction-scoped advisory-lock method and a separate hold-row lock.
The `WalletResponse` record has UNKNOWN graph impact; its constructor is LOW
with two direct handlers (`GetMyWallet` and `EnsureWallet`). The additive
`heldBalance` property derives from `lockedBalance` in both paths.
The controller and audit enum have UNKNOWN graph impact; source search confirms
the controller is exercised by endpoint tests and the enum is used throughout
existing audit paths. The change adds routes and appends audit enum values
without changing existing actions or values. The new create handler's graph
impact was UNKNOWN; direct lifecycle tests and MediatR registration are its
known callers.

Capture preflight: `AuctionHold` is CRITICAL (35 direct dependents and three
indexed processes); capture data will be stored in separate related entities
without changing the hold model. `WalletDbContext` is MEDIUM (nine direct
imports), and `IWalletDbContext` is LOW with unresolved interface dispatch.
The controller, transaction/audit enums, query handler, test context and
snapshot are UNKNOWN. Source searches confirm their existing runtime and test
uses; enum values are appended and framework-dispatched paths require tests.
The `GetHoldQueryHandler.Handle` walk is UNKNOWN; source confirms both service
lookup actions dispatch it through MediatR and PostgreSQL lookup tests call it
directly. Capture purpose is fetched in the same SQL statement as hold status.
The `ListTransactionsQueryHandler.Handle` walk is UNKNOWN; the public
transaction action dispatches it and existing tests invoke it directly.
Extend the existing refund category to include deposit refunds.

## Constitution Check

- [x] User approved additive endpoint/response changes, additional handlers,
  scoped service-key protection and coordinated governance updates.
- [x] No feature facade, repository abstraction or per-resource controller.
- [x] Locking, ownership, idempotency, funds conservation and auditing required.
- [x] Endpoint documentation must be written before endpoint code.
- [x] Plan presented and approved before implementation.

## Repository Structure

- `Wallet.Domain/Entities/WalletAccount.cs`: shared available funds.
- `Wallet.Domain/Entities/` and `Wallet.Domain/Enums/`: holds, transfers and
  deposit escrow state.
- `Wallet.Application/Features/`: handlers for holds, top-up listing, resume
  and reconciliation, and carefully scoped existing balance flow changes.
- `Wallet.Application/Interfaces/IWalletDbContext.cs`: persistence access.
- `Wallet.Infrastructure/Persistence/`: mappings, additive migration and locks.
- `Wallet.Infrastructure/Payments/StripeCheckoutClient.cs`: top-up redirects.
- `Wallet.Api/Controllers/Public/WalletsController.cs`: routes through MediatR.
- `Wallet.Api/`: scoped internal-key protection and reconciliation worker.
- `Wallet.Tests/`: unit, contract, PostgreSQL and provider-concurrency tests.
- `docs/api/`: approved endpoint specifications and integration handoff.
- `scripts/verify-wallet-architecture.sh`, `AGENTS.md`, `.agents/AGENTS.md`
  and `.specify/memory/constitution.md`: approved governance changes.

## Data and API Changes

Create a uniquely referenced durable hold with immutable owner, amount,
currency and request identity, plus durable resolution/transfer records.
Use `LockedBalance` for both withdrawals and holds; total locked cannot exceed
balance. Lock the relevant wallet and hold rows in deterministic order in one
transaction, persist an audit trail, and enforce idempotency and conservation
under concurrent requests. Deposit escrow is explicitly not wired to existing
Commerce payouts; its funds can be returned through the deposit refund API.
Platform balances must be explicit, not minted or silently discarded.

Buyer routes enforce owner isolation; Auction routes require the approved
service key. Continue returning Wallet's documented DTOs/ProblemDetails.
Top-up history and holds lists are database-paginated. Provider status is
authoritative for resume and reconciliation; browser redirects are not.

## Verification

Run focused tests for each state transition, duplicate/conflicting requests,
ownership, internal authentication, frozen accounts, insufficient available
funds, direct-checkout compatibility and worker/provider failures. Run
PostgreSQL concurrency and migration tests when a disposable test database is
available, including webhook versus worker and competing holds versus debits.
Check Swagger and run:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

End-to-end Stripe test-mode and gateway forwarding are separate acceptance
checks; report if credentials or an isolated database are unavailable.

## Complexity Tracking

User approved the increased handler count, additive API changes and scoped
service key. Commerce escrow consumption remains explicitly out of scope.
