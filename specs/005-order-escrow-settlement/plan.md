# Implementation Plan: Order Escrow Settlement

**Branch**: `005-order-escrow-settlement` (spec name; current work branch remains `dev`)  
**Date**: 2026-11-17  
**Spec**: [spec.md](spec.md)
**Plan status**: Approved by the user after clarifying that captured deposits
must remain separate and fully refundable.

**Input**: Wallet-only refactor. Keep the existing order operation routes and payloads; Wallet executes funded transactions, idempotency, and escrow accounting without taking ownership of order business rules.

## Summary

Refactor the existing order-payment, seller-settlement, and order-refund handlers so AuctionOrder movements are backed by persisted wallet-payment escrow. Payment records the amount debited from the buyer's digital Wallet balance under the current order ID. Settlement consumes only available wallet-payment escrow and credits only the caller-supplied seller amount; it rejects a payout exceeding that escrow even if a captured deposit exists. Order refunds also consume only remaining wallet-payment escrow. Captured deposits remain unchanged and fully refundable through the existing hold operation. Physical cash/COD collection and reconciliation are outside Wallet scope. Keep current route/DTO shapes, regular marketplace child-operation behavior, and release gates unchanged.

No Commerce files, business calculations, payout rules, or lifecycle decisions change. No new endpoint or CQRS use case is added; the frozen 26-handler architecture remains intact.

## Technical Context

**Language/Version**: C# 12 / .NET 8  
**Primary Dependencies**: Existing ASP.NET Core, MediatR, EF Core, Npgsql; no new packages  
**Storage**: PostgreSQL (`wallet_db`)  
**Testing**: xUnit; disposable PostgreSQL integration tests use `WALLET_STRIPE_TEST_DATABASE`  
**Target Platform**: Existing WalletService API host  
**Project Type**: ASP.NET Core REST service with classic CQRS  
**Performance Goals**: Preserve current synchronous response path; escrow lookup uses an indexed order ID and remains within the existing database transaction.  
**Constraints**: No route or JSON change; no Commerce repository edits; no change to feature gates or service authentication; no platform-fee or shipping calculation in Wallet; captured-deposit escrow is never a seller-payout source; preserve regular parent/child order behavior where the current request has no parent-payment mapping.  
**Scale/Scope**: One wallet-payment escrow record per order payment; deposit escrow remains in the existing deposit-specific record and is not changed by seller settlement or order-payment refund.

## Constitution Check

| Principle | Assessment |
|---|---|
| Preserve the frozen architecture | **PASS** — no new handler or controller; 26 handlers and two controllers remain. |
| Keep business logic in CQRS handlers | **PASS** — handlers enforce funding, locking, idempotency, and atomic ledger changes only. |
| Protect balances, ownership, and auditability | **PASS** — use existing order advisory locks and wallet row locks; persist escrow, wallet transactions, and audit records in the same transaction. |
| Preserve REST contract and service boundary | **PASS** — routes and request/response shapes are unchanged; no order decisions or Commerce calculations are added to Wallet. |
| Plan, test, and verify | **PASS** — focused PostgreSQL tests plus architecture verifier, Release build, full test suite, and GitNexus change analysis are planned. |
| Migration safety | **PASS** — additive `OrderEscrows` table and indexes only; no destructive migration. |

## Repository Research and Impact

- The Wallet worktree is `dev` at commit `f55185e`; its GitNexus index was refreshed and is current.
- The attached order-service worktree was inspected read-only at commit `922372c`; its GitNexus index is current. No files in that worktree will be modified.
- Current GitNexus upstream impact for `PayOrderWithWalletCommandHandler`,
  `SettleSellerOrderCommandHandler`, and `RefundOrderPaymentCommandHandler`:
  **UNKNOWN** (no graph callers resolved). Text search confirmed the Wallet
  controller dispatches all three commands and tests directly invoke the
  payment handler. Treat the graph result as unresolved, not as unused; focused
  and integration tests are required.
- `IWalletDbContext` impact: **LOW** (one direct implementation edge to
  `WalletDbContext`). `WalletDbContext` impact: **MEDIUM** (10 persistence and
  migration files). `TestWalletDbContext` and `AuditAction` are **UNKNOWN**;
  text search confirmed the test context is the second interface implementation
  and that audit actions are referenced throughout financial handlers.
- `CaptureHoldCommandHandler` and `RefundCapturedHoldCommandHandler` were inspected and are intentionally not changed: the existing captured-deposit refund requires the full original deposit to remain available.
- The existing `LockOrderPaymentAsync` implementation is a PostgreSQL transaction advisory lock; reuse it for payment, same-ID seller settlement, and order refund.
- No HIGH or CRITICAL impact warning was reported. The monetary/concurrency risk is still material and requires database-backed race and rollback tests.

## Project Structure

### Documentation

```text
specs/005-order-escrow-settlement/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/wallet-order-escrow.md
└── checklists/requirements.md

.agents/plan/PLAN-20261117-1428-order-escrow-settlement.md
docs/api/checkout/order-escrow-settlement.md   # Pre-code API handoff, after plan approval
```

### Source Code

```text
Wallet.Domain/
├── Entities/OrderEscrow.cs
└── Enums/AuditAction.cs

Wallet.Application/
├── Interfaces/IWalletDbContext.cs
└── Features/
    ├── Checkout/Commands/PayOrderWithWallet/
    ├── Checkout/Commands/SettleSellerOrder/
    └── Checkout/Commands/RefundOrderPayment/

Wallet.Infrastructure/
├── Persistence/WalletDbContext.cs
├── Persistence/Configurations/OrderEscrowConfiguration.cs
└── Persistence/Migrations/   # additive migration and synchronized snapshot

Wallet.Api/Controllers/Public/WalletsController.cs  # route contract verification only
Wallet.Tests/
├── Support/TestWalletDbContext.cs
└── AuctionOrderEscrowPostgresTests.cs
```

**Structure Decision**: Extend existing checkout handlers and infrastructure; leave hold handlers unchanged. Do not add services, controllers, routes, or use cases.

## Implementation Phases

1. **Pre-code contract and schema**: The approved Wallet-internal API handoff is
   at `docs/api/checkout/order-escrow-settlement.md`; define the additive
   `OrderEscrow` entity with unique `OrderId`, funding wallet, original
   wallet-payment amount, and remaining wallet-payment amount; add precision,
   checks, query filter, and restricted wallet FK.
2. **Payment funding**: Add `OrderEscrows` to `IWalletDbContext`, `WalletDbContext`, and `TestWalletDbContext`. In `PayOrderWithWalletCommandHandler`, create the wallet-payment escrow atomically with the existing buyer debit and audit record. For a pre-existing order payment without an escrow row, seed escrow from its matching `PurchaseDebit` only when the funding wallet and amount are identifiable and no settlement or refund transaction has already resolved that order reference. Otherwise, do not synthesize escrow or move funds. Preserve exact replay/conflict behavior.
3. **Order settlement**: In `SettleSellerOrderCommandHandler`, serialize by order ID and consume only matching remaining wallet-payment escrow. Reject a payout exceeding that escrow even if captured-deposit escrow exists. Credit the seller only for the supplied amount. Preserve the existing legacy child-operation path where no matching order payment exists.
4. **Order refund**: In `RefundOrderPaymentCommandHandler`, serialize by order ID and draw refunds only from the matching wallet-payment escrow. One partial payout and one refund may both succeed when each is covered by the remaining amount. Do not reduce captured deposit escrow; preserve unrelated legacy child-operation behavior.
5. **Preserve deposit flow**: Do not change `CaptureHoldCommandHandler` or `RefundCapturedHoldCommandHandler`. Seller settlement and order-payment refund leave captured-deposit escrow untouched, so the existing full-deposit refund remains available.
6. **Persistence and verification**: Generate an additive migration and model snapshot for `OrderEscrows`. Add audit action enum values only as append-only values; write audit rows through `AddAuditLog` in the same transaction as each payment, payout, or refund. Add database-backed tests for wallet-payment conservation, unchanged/full-refund deposit escrow, retries/conflicts, concurrent settlement/refund, insufficient wallet-payment escrow even with deposit, and audit/financial rollback atomicity.
7. **Docs and closeout**: Update the Wallet payment and feature docs; verify architecture, build, tests, and GitNexus changes. Do not enable production gates or change hold behavior.

## Verification

```bash
./scripts/verify-wallet-architecture.sh
dotnet test Wallet.sln -c Release --filter FullyQualifiedName~AuctionOrderEscrowPostgresTests
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
node .gitnexus/run.cjs detect-changes --scope all --repo .
git status --short
```

PostgreSQL integration tests require `WALLET_STRIPE_TEST_DATABASE` to point to a disposable database. Do not run them against a shared or production database.

## Approval

The user approved this revised plan after confirming that seller settlement
must be funded only by the buyer's digital Wallet payment and must not spend
captured deposits. Source implementation may proceed within the scope above.
