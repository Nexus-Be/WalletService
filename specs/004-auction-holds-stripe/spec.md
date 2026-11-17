# Feature Specification: Auction deposits and Stripe top-up recovery

**Feature Branch**: `dev`
**Created**: 2026-11-17
**Status**: Draft
**Input**: Auction deposit API request and Stripe top-up integration requests.

## User Scenarios & Testing

### User Story 1 - Reserve and resolve an auction deposit (P1)

Auction reserves available wallet funds, queries a reservation by ID or
reference, and releases it after an auction without double locking or release.

**Independent Test**: Exercise duplicate and concurrent reserve and release
operations against a wallet with existing withdrawal reservations.

**Acceptance Scenarios**:
1. **Given** sufficient available funds, **When** Auction creates a hold,
   **Then** the held amount is unavailable to other deductions.
2. **Given** a lost creation response, **When** Auction queries by reference,
   **Then** the existing hold is returned without creating another hold.
3. **Given** a held deposit, **When** Auction releases it, **Then** the same
   amount becomes available exactly once.

### User Story 2 - Capture and refund deposits (P2)

Auction captures a held deposit for forfeiture or order payment. An order
payment deposit can be refunded from its own order-specific escrow.

**Independent Test**: Capture and refund while retrying conflicting operations
and verifying that funds are conserved.

**Acceptance Scenarios**:
1. **Given** a held deposit, **When** valid transfers totaling the hold are
   captured, **Then** all credits and the buyer debit commit atomically.
2. **Given** an order-payment deposit held in deposit-specific escrow,
   **When** an authorized refund is requested, **Then** the buyer is credited
   exactly once and the escrow is reduced by the same amount.

### User Story 3 - Recover Stripe top-ups (P1)

Buyers can find pending top-ups, resume an existing open session and recover
payment state after closing a browser or missing a webhook.

**Independent Test**: Create parallel top-ups, resume one as its owner, and
reconcile paid, failed and expired sessions concurrently with webhooks.

**Acceptance Scenarios**:
1. **Given** multiple pending top-ups, **When** the buyer returns from Stripe,
   **Then** the URL identifies the correct transaction for an owned status query.
2. **Given** a completed Stripe payment with a missing webhook, **When** the
   reconciliation worker checks it, **Then** the wallet is credited once.

## API Contract

- Auction service endpoints: `POST /api/v1/wallets/holds`, `POST
  /api/v1/wallets/holds/{holdId}/capture`, `POST
  /api/v1/wallets/holds/{holdId}/release`, `POST
  /api/v1/wallets/holds/{holdId}/refund-captured`, `GET
  /api/v1/wallets/holds/{holdId}`, and `GET
  /api/v1/wallets/holds/by-reference/{referenceType}/{referenceId}`.
  Require the approved service key; writes require an idempotency key.
- Buyer endpoint: `GET /api/v1/wallets/me/holds`, with database pagination and
  owner isolation. `GET /api/v1/wallets/me` exposes held and available funds;
  held funds include auction and withdrawal reservations.
- Buyer endpoints: `GET /api/v1/wallets/topups` and `POST
  /api/v1/wallets/topups/{transactionId}/resume`; extend owned top-up status
  with resumability and expiry. Resume never creates another charge.
- All amounts are whole VND for auction holds and top-ups. Hold states are
  `Held`, `Captured`, `Released`, and `Refunded`. Existing Wallet error and
  pagination conventions apply; exact error details belong in the pre-code
  API specification.
- Existing top-up and direct-order behaviors remain compatible. Top-up return
  URLs include the correct transaction ID; direct-order URLs stay unchanged.

## Edge Cases

- Concurrent holds, withdrawals, transfers, checkout, admin debit, capture,
  release and refunds must never overspend or create negative available funds.
- Equivalent replays return the original result; conflicting keys, references,
  payloads or terminal-state transitions are rejected.
- Frozen wallets reject new holds but allow resolution of existing holds.
- A Stripe timeout or browser cancellation is not proof of failure; never
  create a new charge during resume or an unresolved attempt.
- Provider, database and reconciliation failures are explicit and retryable.
- Deposit-specific order escrow is separate from the existing Commerce flows;
  Commerce integration is pending a separately agreed contract.

## Functional Requirements

- **FR-001**: Reserve, query, release, capture and refund auction deposits
  idempotently and atomically with auditable balance changes.
- **FR-002**: Enforce ownership, service access, available-balance and
  per-reference uniqueness across concurrent operations.
- **FR-003**: Track deposit escrow by order without assuming existing Commerce
  settlement or refund flows consume that escrow.
- **FR-004**: Provide paginated owned top-up history, safe resume, correlated
  return URLs, and background provider reconciliation.
- **FR-005**: Preserve two thin controllers, MediatR and existing routes while
  updating approved handler-count and service-key governance.

## Key Entities

- **Hold**: owner, amount, reference, status, idempotency and resolution data.
- **Deposit escrow**: captured deposit attributed to an order, independently
  refundable and not automatically consumed by existing Commerce flows.
- **Top-up attempt**: owner, provider session, expiry and settlement state.

## Success Criteria

- **SC-001**: Concurrent and repeated operations do not spend a held unit twice.
- **SC-002**: Every top-up URL identifies its own transaction and resume never
  creates a new provider session.
- **SC-003**: Automated tests verify state transitions, conflicts, ownership,
  reconciliation, and database concurrency; architecture, build and tests pass.

## Assumptions

- Auction owns retry scheduling; Wallet does not callback to Auction.
- Holds have no automatic expiry.
- Provider integration remains Stripe test mode; deployment credentials,
  eligibility and end-to-end testing must be confirmed separately.
- No destructive migration or CatalogService change is included.
