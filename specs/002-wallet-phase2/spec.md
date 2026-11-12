# Feature Specification: Wallet Service Phase 2 Financial Operations

**Feature Branch**: `002-wallet-phase2`

**Created**: 2026-11-10

**Status**: Draft — broader P2 work pending approval; bounded User Story 3 implementation approved

**Input**: User requested a plan for the second-sprint requirements and selected the WalletService slice of the supplied Phase 2 API catalog.

## User Scenarios & Testing

### User Story 1 - Request and process a seller withdrawal (Priority: P1)

A seller requests a withdrawal from available wallet funds. Staff reviews the request and records the result of the bank transfer or rejection.

**Why this priority**: Withdrawal is the WalletService-owned P2-03 flow and already has a partial request/reservation/decision implementation.

**Independent Test**: Exercise a valid seller request, then approve and reject separate pending requests; verify balances, request state, audit records, and provider/reference information.

**Acceptance Scenarios**:

1. **Given** a seller with sufficient VND available balance, **When** they request an amount within the specified limits, **Then** the amount is reserved and a pending withdrawal is returned.
2. **Given** an amount below 50,000 VND, above 500,000,000 VND, or greater than available funds, **When** a withdrawal is requested, **Then** the request is rejected without changing balances.
3. **Given** a pending withdrawal, **When** staff approves it, **Then** the reserved funds are consumed exactly once and the bank transfer reference and actor are recorded.
4. **Given** a pending withdrawal, **When** staff rejects it, **Then** the reservation is released exactly once and the rejection is auditable.
5. **Given** an already processed withdrawal, **When** it is processed again, **Then** no additional balance movement occurs.

### User Story 2 - Review wallet transactions and balance reconciliation (Priority: P1)

An authorized administrator reviews wallet transaction records and identifies whether each recorded wallet balance matches its latest successful transaction snapshot.

**Why this priority**: P2-04 requires finance visibility and a way to identify inconsistencies in WalletService's existing financial records.

**Independent Test**: Create representative wallet transactions, query a bounded page, and compare the latest successful transaction snapshots with current wallet balances.

**Acceptance Scenarios**:

1. **Given** posted wallet transactions, **When** an authorized administrator queries the ledger, **Then** the response includes matching entries and accurate pagination metadata.
2. **Given** a wallet whose current balance matches its latest successful transaction snapshot, **When** reconciliation is run, **Then** the result is `Balanced`.
3. **Given** a wallet whose current balance differs from its latest successful transaction snapshot, **When** reconciliation is run, **Then** the result is `Discrepancy` and the source records remain unchanged.
4. **Given** a wallet with no successful transaction snapshot, **When** reconciliation is run, **Then** the result is `InsufficientHistory`.
5. **Given** an unauthenticated or non-admin caller, **When** an admin report is queried, **Then** access is denied.

### User Story 3 - Adjust a wallet balance using the owner's user ID (Priority: P2)

An authorized administrator or staff member adjusts a wallet balance by supplying the owner's user ID, which is available to other services without first looking up a wallet-specific identifier.

**Why this priority**: This removes the need for callers to discover WalletService's internal wallet ID before requesting an administrative balance adjustment.

**Independent Test**: Submit an adjustment for a user ID with an existing wallet and verify that the same balance, transaction, audit, and response behavior is preserved.

**Acceptance Scenarios**:

1. **Given** a user with an existing wallet, **When** an authorized administrator submits a valid credit or debit adjustment using that user's ID, **Then** the user's wallet is adjusted and the existing response is returned.
2. **Given** a user ID with no wallet, **When** an authorized administrator submits an adjustment, **Then** the request returns the existing wallet-not-found error without changing financial records.
3. **Given** an invalid user ID or an unauthorized caller, **When** an adjustment is submitted, **Then** the request is rejected without changing financial records.

## API Contract

The broader Phase 2 plan covers these WalletService operations:

- P2-03 withdrawal creation and staff processing.
- P2-04 administrator reporting and reconciliation over existing wallet transaction records.

For the currently approved implementation slice, only the bounded balance-adjustment request below is authorized. P2-03 and P2-04 remain planning items and require separate implementation approval.

The existing service already exposes `POST /api/v1/wallets/withdrawals` and `PATCH /api/v1/admin/wallets/withdrawals/{id}`.

- **Decision — architecture and API compatibility**: Plan a coordinated, explicitly approved amendment where needed. Retain the two controllers, CQRS/MediatR, and no-facade constraints. Reconcile the current handler count and governing documents, and update the verifier and API documentation together before implementation. This planning decision is not implementation authorization.

- **Decision — service-to-service security**: Defer P2-05 purchase-freeze and Auction escrow endpoints from this sprint until a separate internal-authentication and API-contract decision is approved. Do not expose unauthenticated internal routes.
- **Decision — ledger semantics**: Implement an administrator report and reconciliation view over existing `WalletTransaction` records only. Do not represent it as a double-entry general ledger or bank-statement reconciliation.

Use the existing withdrawal routes and status values. Add the transfer reference to the existing approval request/response contract and only record approval after staff confirms the manual bank transfer. Add `GET /api/v1/admin/wallets/ledger` and `GET /api/v1/admin/wallets/reconciliation`; the latter compares each wallet's recorded balance with its latest successful transaction snapshot and reports insufficient history separately. Neither endpoint represents external bank reconciliation or double-entry accounting. Return pure DTOs, use English sentence-case messages and SCREAMING_SNAKE_CASE error codes, serialize enums as PascalCase, and paginate list/read endpoints at the database.

The deferred operations are `/api/v1/internal/wallets/freeze-purchase`, `/unfreeze-purchase`, `/purchase-status`, and the Auction deposit/refund/deduct/forfeit operations. They require a separate security and cross-service contract plan.

### Additional bounded request — balance adjustment by user ID

- Keep `POST /api/v1/admin/wallets/{userId:guid}/adjustments`, changing the route parameter meaning from `walletId` to `userId` without changing the URL segment shape.
- Continue to require `admin` or `staff`; the user ID identifies the target wallet and does not change caller authorization.
- Keep the JSON body (`amount`, `adjustmentType`, and `reason`) and response shape unchanged. The response may continue to include the wallet ID.
- Resolve and lock the wallet by its owner `userId`. A user with no wallet receives the existing wallet-not-found error.
- Preserve the current balance checks, transaction and audit writes, and transaction boundary. No new use case, handler, entity, table, or migration is required.
- Keep withdrawal processing keyed by withdrawal-request ID; it is not part of this input change.

## Edge Cases

- Concurrent withdrawal requests or decisions compete for the same available or reserved funds.
- Staff submits a bank reference, retries an approval, or approves after a bank-transfer timeout or failure.
- A withdrawal is rejected after funds have been reserved.
- A transaction succeeds in the database but its caller times out and retries.
- A ledger query uses invalid date ranges, extreme pagination, or records spanning multiple currencies.
- A per-wallet reconciliation finds missing history or a balance inconsistent with the latest successful transaction snapshot.
- A user ID has no wallet, or an attempted debit exceeds the wallet's available balance.

## Functional Requirements

- **FR-001**: The system MUST reserve only the requesting seller's available VND balance for a valid withdrawal and reject requests outside the specified amount bounds.
- **FR-002**: The system MUST record withdrawal approval or rejection, processing actor, processing time, and any bank transfer reference, and MUST prevent duplicate balance effects on retries.
- **FR-003**: The system MUST provide an authorized administrator a bounded, paginated report of existing wallet transaction records and a per-wallet balance reconciliation; the response MUST NOT claim double-entry or external bank reconciliation.
- **FR-004**: Every balance-changing operation MUST preserve non-negative available balance and commit its balance, transaction, and audit changes atomically where applicable.
- **FR-005**: Every withdrawal decision MUST prevent duplicate financial effects when retried.
- **FR-006**: The system MUST enforce caller ownership and role authorization for user and administrator operations.
- **FR-007**: The system MUST retain the two-controller, CQRS/MediatR, and no-facade architecture; any change to frozen counts or contracts MUST be explicitly approved and reflected in the verifier and governing documentation together.
- **FR-008**: An authorized administrator or staff member MUST be able to target a balance adjustment by the wallet owner's user ID, without supplying a wallet ID. The adjustment's existing authorization, validation, balance, transaction, audit, and response behavior MUST remain unchanged.

## Key Entities

- **Wallet account**: User-owned VND balance and reserved balance.
- **Withdrawal request**: Amount, bank destination, pending/terminal state, processing actor/time, and external transfer reference.
- **Wallet transaction**: Existing per-wallet record used by the report and balance reconciliation.
- **Audit record**: Actor, operation, related entity/reference, state transition, and trace context for sensitive mutations.

## Success Criteria

- **SC-001**: Every approved withdrawal transition changes reserved/available funds exactly once; retries and duplicate decisions cause no additional movement.
- **SC-002**: Every ledger report page matches the filtered source transaction records, and reconciliation identifies seeded balance-to-transaction inconsistencies without changing source records.
- **SC-003**: All authorization, validation, concurrency, retry, and failure scenarios have automated tests; the architecture verifier, Release build, and full test suite pass after approved implementation.
- **SC-004**: A valid adjustment request using a wallet owner's user ID targets that owner's wallet, and no adjustment request requires a wallet ID.

## Assumptions

- This feature is limited to WalletService; coordinating changes in User, Commerce, Auction, Fulfillment, or Notification services are out of scope for this repository plan.
- Existing withdrawal request creation, reservation, approval, rejection, and basic auditing are implemented; planning should extend verified gaps rather than recreate them.
- An approved withdrawal is not evidence that a bank payout occurred; settlement evidence and failure handling must be specified.
- P2-04 is limited to reporting and reconciling records stored by WalletService; it does not establish a general ledger or reconcile an external bank statement.
- P2-05 purchase freeze and Auction escrow operations are deferred until their internal service authentication and cross-service contracts are approved.
- No migration or endpoint is implemented until the required architecture and API approvals are explicit.
- The balance-adjustment identifier change is a bounded addition to this plan; withdrawal decisions continue to use withdrawal-request IDs.
