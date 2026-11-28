# Feature Specification: Cancel pending direct payment

**Feature Branch**: Current branch

**Created**: 2026-11-17

**Status**: Draft

**Input**: Prevent a cancelled order's pending direct payment from being paid;
refund a payment that wins the cancellation race and defer an uncertain outcome.

## User Scenarios & Testing

### User Story 1 - Cancel an existing pending payment (Priority: P1)

As the payment owner, I want my existing direct-payment attempt cancelled so
that a previously issued checkout cannot charge me after cancellation.

**Independent Test**: Create an unpaid attempt, request cancellation, and
confirm provider finality before reporting a completed cancellation.

1. **Given** an existing unpaid checkout, **when** its owner requests
   cancellation, **then** the system must not report completion until the
   provider confirms it cannot succeed.
2. **Given** payment completes during cancellation, **when** the provider
   confirms payment, **then** refund the payment to its original method and
   report completion only after the refund succeeds.
3. **Given** the provider outcome is uncertain, **when** cancellation is
   requested or retried, **then** leave cancellation pending and recover later.

### User Story 2 - Retry safely (Priority: P1)

As a caller, I need a stable cancellation result after timeouts or retries.

**Independent Test**: Repeat and concurrently submit an identical cancellation
while provider and webhook results arrive; verify one financial outcome.

1. **Given** the same order and owner, **when** cancellation is retried,
   **then** it returns the current or final cancellation state without
   duplicating refunds.
2. **Given** an attempt still being created, **when** cancellation begins,
   **then** the system must coordinate with creation and not prematurely claim
   the provider session is unpayable.

## API Contract

- Proposed route approved by user:
  `POST /api/v1/wallets/checkout/direct/{orderId}/cancel`.
- Caller: authenticated payment owner; do not allow another user to cancel.
- A missing or non-owned direct payment is rejected; cancellation before any
  payment record exists is outside this endpoint's protection.
- An equivalent repeat returns current cancellation state; unresolved provider
  or refund outcomes must not be presented as complete.
- Exact response fields, status codes, cancellation state persistence and
  retry protocol are to be finalized before implementation.
- Existing routes and response contracts remain unchanged unless separately
  approved.

## Edge Cases

- Cancellation races session creation, provider completion or webhook delivery.
- Provider creates a session or refund but its response is lost.
- Duplicate and out-of-order webhooks and concurrent retries occur.
- Refund is pending, failed, or its outcome is unknown.
- Caller is not the owner, order has no record, or persistence fails.
- Process restarts after an external action but before local persistence.
- Cancellation is requested before Wallet has any payment record: reject it;
  preventing later checkout requires separate caller-side integration.

## Functional Requirements

- **FR-001**: Only the payment owner may request cancellation of an existing
  direct payment.
- **FR-002**: The system MUST durably record cancellation intent and prevent
  creation or reuse of a payable session for that existing payment while
  cancellation is unresolved.
- **FR-003**: The system MUST distinguish pending cancellation from a
  confirmed unpayable session or a successfully refunded payment.
- **FR-004**: A completed payment that races cancellation MUST be refunded
  once to the original payment method; uncertain outcomes remain pending.
- **FR-005**: Retries and recovery after process failure MUST not duplicate
  charges or refunds.
- **FR-006**: Existing top-up and ordinary direct-payment behavior MUST be
  preserved when cancellation is not requested.
- **FR-007**: Preserve applicable WalletService architecture and compatibility
  constraints, with the approved coordinated addition of a CQRS use case.

## Key Entities

- **Direct payment attempt**: Owner, order reference and provider correlation.
- **Cancellation**: Durable intent and observed completion or pending state.
- **Refund**: Provider-correlated repayment and verified final outcome.

## Success Criteria

- **SC-001**: All automated cancellation scenarios, including payment races,
  retry and restart recovery, have a single consistent financial outcome.
- **SC-002**: No uncertain provider outcome is reported as a completed
  cancellation in the tested scenarios.
- **SC-003**: Existing direct-payment and top-up regression tests pass along
  with the architecture verifier and Release build and tests.

## Assumptions

- Scope is WalletService only. Cross-service cancellation and end-to-end
  behavior require separate implementation and verification.
- No record means no cancellation authorization; the caller must separately
  prevent a subsequent checkout for an already-cancelled order.
- Existing uncommitted order-escrow changes must be preserved.
