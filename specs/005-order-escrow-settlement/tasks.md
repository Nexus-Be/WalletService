# Tasks: Order Escrow Settlement

**Input**: Clarified design documents from `specs/005-order-escrow-settlement/`

**Prerequisites**: Revised `spec.md` and `plan.md` must be approved before
source-code changes. Implementation remains Wallet-only.

**Tests**: Automated tests for changed behavior are required. Place tests in
`Wallet.Tests`; use a disposable PostgreSQL database for PostgreSQL integration
tests.

## Format

`- [ ] T### [P?] [Story?] Description with exact repository-relative file paths`

## Phase 1: Setup

- [x] T001 [P] Draft the pre-code API handoff in
      `docs/api/checkout/order-escrow-settlement.md` from
      `specs/005-order-escrow-settlement/contracts/wallet-order-escrow.md`;
      document current routes, wallet-payment-only settlement, independent deposit
      refunds, unchanged request/response behavior, and existing release gates
      before editing C#.
- [x] T002 [P] Record the approved baseline by running
      `./scripts/verify-wallet-architecture.sh` and confirm the two-controller,
      26-handler invariants before implementation.

## Phase 2: Foundational

- [x] T003 Add `Wallet.Domain/Entities/OrderEscrow.cs` with a required unique
      `OrderId` (`Guid`), required `WalletId` (`Guid` FK with `Restrict` delete
      behavior), positive immutable `OriginalAmount` (`decimal(18,2)`), and
      `RemainingAmount` (`decimal(18,2)`) constrained to
      `0 <= RemainingAmount <= OriginalAmount`; inherit standard base entity
      fields.
- [x] T004 Configure `OrderEscrow` in
      `Wallet.Infrastructure/Persistence/Configurations/OrderEscrowConfiguration.cs`
      and register its DbSet in `Wallet.Application/Interfaces/IWalletDbContext.cs`,
      `Wallet.Infrastructure/Persistence/WalletDbContext.cs`, and
      `Wallet.Tests/Support/TestWalletDbContext.cs`; apply the standard
      soft-delete query filter.
- [x] T005 Generate the additive `OrderEscrows` migration and synchronized model
      snapshot under `Wallet.Infrastructure/Persistence/Migrations/`; include
      the unique order-ID index, amount constraints, and restricted wallet FK,
      and do not rewrite existing `AuctionDepositEscrow` rows.

## Phase 3: User Story 1 - Settle a funded auction order (Priority: P1)

**Goal**: Pay the caller-specified seller amount exactly once from buyer-paid
digital Wallet funds, without spending a captured deposit.

**Independent Test**: Pay an order, optionally capture a deposit, then settle.
Verify exact seller credit and wallet-payment escrow reduction; confirm deposit escrow
remains unchanged. Reject a payout exceeding wallet-payment escrow even if the deposit
could cover the difference.

### Tests

- [x] T006 [US1] Add PostgreSQL tests in
      `Wallet.Tests/AuctionOrderEscrowPostgresTests.cs` for atomic payment
      funding, wallet-payment-only payout, unchanged captured deposit, insufficient wallet-payment funds
      despite a deposit, exact replay, conflicting replay, and amount
      conservation; assert payment and payout audit rows are written once with
      their financial changes.

### Implementation

- [x] T007 [US1] Update
      `Wallet.Application/Features/Checkout/Commands/PayOrderWithWallet/PayOrderWithWalletCommandHandler.cs`
      to create wallet-payment escrow atomically with the existing buyer debit,
      transaction, and audit record under the order lock; preserve existing
      payment replay/conflict behavior. For a pre-existing order payment
      without escrow, seed from its matching `PurchaseDebit` only when the
      funding wallet and amount are identifiable and no settlement or refund
      transaction has already resolved that order reference; otherwise, do
      not synthesize escrow or move funds. Add the payment audit action to
      `Wallet.Domain/Enums/AuditAction.cs` only as an append-only enum value if
      no existing action is semantically correct, and persist it through
      `AddAuditLog` in the same transaction.
- [x] T008 [US1] Update
      `Wallet.Application/Features/Checkout/Commands/SettleSellerOrder/SettleSellerOrderCommandHandler.cs`
      to serialize by order ID and consume only matching remaining
      wallet-payment escrow. Reject a payout exceeding that escrow even if a
      captured deposit exists; credit only the supplied seller amount in the
      same transaction. Add an append-only settlement audit action in
      `Wallet.Domain/Enums/AuditAction.cs` if needed and write it with
      `AddAuditLog` in the same transaction. Preserve the existing legacy
      child-operation path where no matching order payment exists.

## Phase 4: User Story 2 - Refund only the order payment (Priority: P1)

**Goal**: Refund unspent buyer-paid digital Wallet funds without consuming or changing
captured deposit escrow.

**Independent Test**: Complete a partial Wallet-funded seller payout, refund
only the remaining wallet-payment escrow, then refund the separately captured deposit.
Verify each funding source is credited at most once and the deposit remains
fully refundable after seller settlement.

### Tests

- [x] T009 [US2] Extend
      `Wallet.Tests/AuctionOrderEscrowPostgresTests.cs` with wallet-payment-only refund,
      unchanged/full captured-deposit refund after a partial payout, changed
      retry, concurrent payout/refund overdraw, and transaction rollback cases;
      assert refund audit rows commit atomically and are absent after rollback.

### Implementation

- [x] T010 [US2] Update
      `Wallet.Application/Features/Checkout/Commands/RefundOrderPayment/RefundOrderPaymentCommandHandler.cs`
      to debit only matching remaining order-payment escrow before crediting
      the buyer; serialize with payment and settlement using the existing
      order lock. Preserve the existing transaction reference, idempotency,
      error contract, and legacy child-operation behavior. Do not modify
      `AuctionDepositEscrow`. Add an append-only refund audit action in
      `Wallet.Domain/Enums/AuditAction.cs` if needed and write it with
      `AddAuditLog` in the same transaction.

## Phase 5: User Story 3 - Preserve existing service contracts (Priority: P1)

**Goal**: Keep existing routes, DTOs, release gates, and regular marketplace
child-operation behavior unchanged.

**Independent Test**: Exercise current Wallet contract regressions and confirm
no route or JSON shape changes, no release gate changes, and unchanged
unmapped child-operation behavior.

### Tests

- [x] T011 [US3] Add or update regression coverage in
      `Wallet.Tests/WalletContractRegressionTests.cs` and
      `Wallet.Tests/ReleaseFeatureTests.cs` for unchanged payment, settlement,
      refund, and hold routes/request shapes, feature gates, and regular
      marketplace child-operation compatibility.

### Documentation

- [x] T012 [US3] Update
      `docs/api/wallet-payments.md` and
      `docs/api/holds/auction-deposits.md` to describe backed wallet-payment funds,
      wallet-payment-only seller payout, independent/full deposit refunds, and unchanged
      release gates without changing the public contract.

## Final Phase: Polish and Cross-Cutting Verification

- [x] T013 Reconcile
      `specs/005-order-escrow-settlement/spec.md`,
      `specs/005-order-escrow-settlement/data-model.md`,
      `specs/005-order-escrow-settlement/contracts/wallet-order-escrow.md`,
      `specs/005-order-escrow-settlement/quickstart.md`, and
      `docs/api/checkout/order-escrow-settlement.md` with the implementation;
      update the feature checklist and resolve any plan/spec gaps.
- [ ] T014 Run the focused
      `AuctionOrderEscrowPostgresTests`, then
      `./scripts/verify-wallet-architecture.sh`,
      `dotnet build Wallet.sln -c Release`, and
      `dotnet test Wallet.sln -c Release`; use
      `WALLET_STRIPE_TEST_DATABASE` only with a disposable PostgreSQL database.
- [x] T015 Run
      `node .gitnexus/run.cjs detect-changes --scope all --repo .`, resolve
      unexpected affected flows, and inspect `git status --short` for unrelated
      files or secrets; do not commit unless separately requested.

> **T014 pending verification:** PostgreSQL integration tests are authored but
> were skipped because `WALLET_STRIPE_TEST_DATABASE` is not configured in this
> environment. The Release build and full non-database suite passed.

## Dependencies

```text
T001 and T002 (independent preflight)
  -> T003 -> T004 -> T005
  -> T006 -> T007 -> T008
  -> T009 -> T010
  -> T011 -> T012
  -> T013 -> T014 -> T015
```

User-story order: **US1 -> US2 -> US3**. The stories share the escrow schema
and order-scoped locking, so the foundational persistence work must complete
first. Captured-deposit operations remain independent of order-payment escrow.
US3 contract checks depend on the implemented behavior to confirm
compatibility.

## Parallel Execution

- Setup: T001 and T002 can run in parallel; they touch separate files/resources.
- Foundation: T003, T004, and T005 are sequential because configuration and
  migration depend on the entity and DbContext registration.
- Stories: test tasks precede their implementation tasks. Payment, settlement,
  and refund share order-scoped financial state, so avoid parallel changes to
  those handlers. Hold-capture/refund handlers are not changed.

## Implementation Strategy

Complete the shared additive escrow schema first, then deliver **US1 as the
MVP**: buyer payment funds wallet-payment escrow and same-ID seller payout consumes only
that escrow without creating money or using the captured deposit. Implement US2
next to preserve independent wallet-payment refunds, then verify US3 compatibility.
Finish with focused/full verification and graph change analysis. Do not edit
the attached order-service repository or enable production release gates.

## Phase 6: Convergence

- [ ] T016 Run all seven tests in
      `Wallet.Tests/AuctionOrderEscrowPostgresTests.cs` against a disposable
      PostgreSQL database configured through `WALLET_STRIPE_TEST_DATABASE`;
      resolve any failures and then mark T014 complete per T014 (partial).
