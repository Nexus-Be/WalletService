# Feature Specification: Order Escrow Settlement

**Feature Branch**: `005-order-escrow-settlement`

**Created**: 2026-11-17

**Status**: Implementation complete; PostgreSQL integration verification pending

**Input**: User direction: Refactor WalletService only, treating the existing order-service behavior as authoritative; Wallet owns transaction execution, idempotency, and escrow accounting, not order business rules.

## Clarifications

### Session 2026-11-17

- Q: Should seller settlement be allowed to spend captured-deposit escrow? → A: No. Seller settlement may consume only wallet-payment escrow; captured deposits remain separate and fully refundable, and settlement fails when wallet-payment escrow alone is insufficient.
- Q: Is the AuctionOrder payment collected as physical cash/COD? → A: No. This flow debits the buyer's digital Wallet balance; physical cash/COD collection and reconciliation are outside Wallet scope.

## User Scenarios & Testing

### User Story 1 - Settle a funded auction order (Priority: P1)

As an order-service caller, I need a seller payout to consume only digital Wallet funds collected for the same order, so Wallet does not create money or consume a separately refundable deposit.

**Why this priority**: A payout must be backed by the buyer's digital Wallet balance debited for that order. Captured deposits remain independent and fully refundable; the order service remains responsible for deciding when to pay and how much.

**Independent Test**: Pay an order from the buyer's digital Wallet, optionally capture a deposit for the same order, then request the seller payout. Verify the seller receives exactly the requested amount from wallet-payment escrow, the deposit escrow is unchanged, and underfunded wallet-payment escrow causes rejection even if a deposit exists.

**Acceptance Scenarios**:

1. **Given** a successful Wallet-funded order payment and an optional captured deposit associated with the same order ID, **When** a seller payout is requested for that ID and remaining wallet-payment escrow covers it, **Then** Wallet credits only the requested amount and atomically reduces only the matching wallet-payment escrow.
2. **Given** remaining wallet-payment escrow does not cover the requested payout, **When** a payout is requested, **Then** Wallet rejects it without changing any wallet balance, ledger entry, or captured-deposit escrow, even when the deposit could cover the difference.
3. **Given** an identical payout is retried, **When** the same order, seller, and amount are submitted again, **Then** Wallet returns the original result without another credit; a changed retry conflicts.

### User Story 2 - Refund only the order payment (Priority: P1)

As an order-service caller, I need to refund the amount paid for an order without consuming a separately captured deposit, so the deposit can be resolved independently.

**Why this priority**: The order payment and captured deposit are separate funding sources and must not be refunded twice.

**Independent Test**: Fund an order from the buyer's Wallet balance and capture a separate deposit, complete a seller payout covered by wallet-payment escrow, refund the remaining order-paid portion, then refund the captured deposit through the existing hold operation. Verify the deposit remains independently and fully refundable and all operations are idempotent.

**Acceptance Scenarios**:

1. **Given** a Wallet-funded order with a captured deposit, **When** the order payment is refunded, **Then** the refund is limited to unspent wallet-payment escrow and leaves the full captured-deposit escrow unchanged.
2. **Given** an identical refund is retried, **When** the same order, buyer, and amount are submitted, **Then** Wallet returns the original result without a second credit; a changed retry conflicts.
3. **Given** the seller payout has exhausted the remaining order escrow, **When** a refund is requested, **Then** Wallet rejects it without crediting the buyer.

### User Story 3 - Preserve existing service contracts (Priority: P1)

As a caller of the existing Wallet order operations, I need the current routes and JSON shapes to remain stable while Wallet adds backed escrow accounting.

**Why this priority**: Existing callers own order status transitions, deadlines, refund decisions, and payout amounts; changing those responsibilities or the transport contract is outside this Wallet-only change.

**Independent Test**: Exercise the existing payment, seller-settlement, and order-refund routes with their current request shapes and verify the response contract remains unchanged.

**Acceptance Scenarios**:

1. **Given** an AuctionOrder that uses the same ID for payment, settlement, and refund, **When** the current Wallet routes are called, **Then** the operations use that ID to reconcile the order's funded escrow.
2. **Given** a regular marketplace payment whose master-order ID differs from child sub-order IDs, **When** the legacy child settlement or refund route is called, **Then** this feature does not alter its existing behavior.
3. **Given** any deployment environment, **When** this feature is deployed, **Then** existing release-feature gates remain unchanged and no production operation is enabled by this change.

## API Contract

No route, request, response, status-code, or authentication contract changes are in scope.

| Method and route | Existing request identity | Existing operation |
|---|---|---|
| `POST /api/v1/wallets/orders/{orderId}/payment` | Authenticated buyer; body contains `amount` | Debits the buyer's digital Wallet balance and records wallet-payment funding under `orderId`. |
| `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` | Body contains `sellerId` and `amount` | Pays the caller-specified seller amount from matching wallet-payment escrow when the operation ID is the funded order ID; captured deposits are never used. |
| `POST /api/v1/wallets/orders/{orderId}/refund` | Body contains `userId`, `amount`, and optional `reason` | Refunds wallet-payment escrow only; captured deposit escrow remains independent. |
| `POST /api/v1/wallets/holds/{holdId}/capture` | Existing Auction service authentication and idempotency header | `ORDER_PAYMENT` capture associates deposit escrow with the supplied order ID. Existing feature gate remains unchanged. |
| `POST /api/v1/wallets/holds/{holdId}/refund-captured` | Existing Auction service authentication and idempotency header | Refunds the full captured deposit independently from seller settlement and order-payment refunds. |

Operations remain idempotent by their existing stable order or hold references. Identical retries return the original result; conflicting retries must not move funds. The existing production gates and legacy order-operation compatibility behavior remain unchanged.

## Edge Cases

- Concurrent seller payout and order-payment refund for one order must serialize and produce one valid outcome. Captured-deposit resolution remains independent and cannot affect order-payment escrow.
- An unavailable seller wallet, insufficient escrow, database failure, or conflicting replay must leave all balances and escrow values unchanged.
- Seller settlement and order-payment refund must never use or modify deposit escrow.
- A partial seller payout and one refund may both succeed sequentially when each amount is covered by the remaining escrow. A second payout or refund is idempotent only for an exact retry; a changed retry or any operation exceeding the remaining escrow must not move funds.
- The unspent remainder after a payout remains in escrow; Wallet does not infer a platform-fee, shipping, or order-lifecycle decision.
- Physical cash/COD collection and reconciliation are outside this Wallet flow.
- Regular marketplace child operations whose IDs cannot be associated with the parent payment remain outside this change.

## Functional Requirements

- **FR-001**: Wallet MUST debit the buyer's digital Wallet balance and record wallet-payment funding atomically under the existing order ID.
- **FR-002**: Wallet MUST credit only the caller-specified seller amount and reduce matching wallet-payment escrow by the same amount in one database transaction.
- **FR-003**: Wallet MUST NOT use or modify captured-deposit escrow to fund a seller payout.
- **FR-004**: Wallet MUST reject a payout that exceeds remaining wallet-payment escrow without modifying balances, ledger state, or deposit escrow.
- **FR-005**: Wallet MUST refund only unspent wallet-payment funds for an order refund and MUST leave separately captured deposit escrow unchanged.
- **FR-006**: Wallet MUST make payment, payout, and refund retries idempotent by the existing stable IDs and reject conflicting reuse.
- **FR-007**: Wallet MUST serialize concurrent payment, seller payout, and order-refund mutations to the same wallet-payment escrow and commit balance, escrow, transaction, and audit changes atomically. Deposit operations remain independent.
- **FR-008**: Wallet MUST NOT calculate order totals, fees, deposits, payout eligibility, seller deadlines, or refund reasons; these values and decisions remain supplied by the caller.
- **FR-009**: Wallet MUST preserve existing routes, request/response shapes, regular marketplace child-operation behavior, the frozen CQRS architecture, and release-feature gates.
- **FR-010**: Automated tests MUST verify wallet-payment escrow conservation, independent deposit refunds, idempotency, concurrency, rollback, and unchanged route contracts.

## Key Entities

- **Order payment transaction**: The successful buyer debit associated with the order ID and its amount.
- **Order escrow**: The unspent portion of the digital Wallet payment available for a seller payout or order-payment refund.
- **Auction deposit escrow**: The existing hold-capture balance associated with the same order ID, independently refundable through the existing hold operation.
- **Wallet transaction**: The immutable balance-change record for buyer debits, seller credits, and buyer refunds.

## Success Criteria

- **SC-001**: Across successful payment, payout, and refund operations, credited and remaining wallet-payment funds never exceed the amount originally paid from the buyer's Wallet balance; seller payout never changes captured-deposit escrow.
- **SC-002**: Replaying any completed operation with the same payload produces no additional balance movement; a conflicting replay produces no balance movement.
- **SC-003**: Seller payout and order-payment refund do not change captured-deposit escrow; an untouched captured deposit remains refundable for its full original amount.
- **SC-004**: The existing routes and JSON response shapes remain unchanged, and the repository architecture verifier, Release build, and full test suite pass.

## Assumptions

- The order ID is stable across payment, payout, and refund for the auction-order flow.
- The caller supplies authoritative payout/refund amounts and lifecycle decisions; Wallet only verifies and executes funded ledger movements.
- Seller payouts and order-payment refunds use only digital Wallet payment escrow. Captured-deposit escrow remains separate and fully refundable through the existing hold operation.
- Physical cash/COD collection and reconciliation are outside Wallet scope.
- The legacy general marketplace parent-to-child escrow relationship is not changed because its current calls do not provide the parent payment ID to child operations.
- Existing feature gates remain as configured; this feature does not enable them in any environment.
