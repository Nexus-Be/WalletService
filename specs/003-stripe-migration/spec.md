# Feature Specification: Stripe payment migration

**Feature Branch**: `dev`

**Created**: 2026-11-12

**Status**: Draft

**Input**: Replace VNPay with Stripe-hosted Checkout for wallet top-ups and
direct order payments, including cards and delayed payment methods.

## User Scenarios & Testing

### User Story 1 - Fund a wallet (Priority: P1)

A signed-in buyer starts a VND top-up and is redirected to Stripe. The wallet
is credited only after the provider confirms a successful payment.

**Independent Test**: Complete a test-mode top-up and deliver the confirmation
more than once; verify the wallet is credited once.

**Acceptance Scenarios**:

1. **Given** a valid whole-VND amount, **When** the user starts a top-up,
   **Then** the service records a pending attempt and returns a hosted payment
   URL.
2. **Given** a pending top-up, **When** a valid paid confirmation arrives,
   **Then** the wallet balance increases once and the operation is audited.
3. **Given** a completed Checkout Session with an unpaid delayed method,
   **When** the completion notification arrives, **Then** the top-up remains
   pending until the provider confirms success or failure.
4. **Given** a top-up, **When** another user requests its status, **Then**
   the service does not disclose it.

### User Story 2 - Pay an order directly (Priority: P1)

A signed-in buyer can pay an order through Stripe without changing wallet
balance, and retry after a failed or expired payment attempt.

**Independent Test**: Create a direct order payment, simulate failure and a
new attempt, then confirm a successful payment and verify the wallet balance
remains unchanged.

**Acceptance Scenarios**:

1. **Given** an unpaid order, **When** a buyer starts checkout, **Then** the
   service records a pending attempt and returns a hosted payment URL.
2. **Given** an active attempt, **When** an identical request is retried,
   **Then** it returns the same active attempt without a duplicate charge.
3. **Given** an expired or failed attempt, **When** the provider confirms that
   the attempt can no longer succeed, **Then** the buyer may create a new
   attempt for the unpaid order.
4. **Given** a successful direct payment, **When** its confirmation is
   processed, **Then** the order payment is marked successful and the wallet
   balance is unchanged.

### User Story 3 - Recover safely from delivery and provider failures (Priority: P1)

Payment results remain correct despite retries, delayed events, race conditions,
provider timeouts, and interrupted persistence.

**Independent Test**: Deliver duplicate and out-of-order provider events
concurrently and simulate interruption during session creation.

**Acceptance Scenarios**:

1. **Given** a provider confirmation has already been processed, **When** it
   is delivered again or an equivalent event arrives, **Then** no duplicate
   credit or settlement occurs.
2. **Given** session creation succeeded but local persistence failed,
   **When** a retry or verified event arrives, **Then** the same payment is
   reconciled without creating a second charge.
3. **Given** an invalid provider signature or mismatched amount, currency or
   payment reference, **When** the notification arrives, **Then** no financial
   state is changed.

## API Contract

The user approved replacing `POST /api/v1/wallets/topups/vnpay` with
`POST /api/v1/wallets/topups/stripe`, adding owner-protected
`GET /api/v1/wallets/topups/{id}`, and replacing the VNPay IPN and return
routes with anonymous signed `POST /api/v1/wallets/stripe/webhooks`.
`POST /api/v1/wallets/checkout/direct` keeps its route but removes
VNPay-specific inputs. The existing owner-protected direct-payment status
route remains. Request and response fields, exact replay status and headers,
error codes, and status transitions require finalization in the pre-code
contract documents. The browser redirect alone is not evidence of payment.

## Edge Cases

- Duplicate or concurrent initiation and notifications; same order with changed
  parameters; failed/expired retry followed by a late success.
- A session becomes payable before its ID is stored, a provider request times
  out, or an idempotency key expires before an attempt is reconciled.
- Delayed payment completion, failure, expiry, out-of-order events, invalid
  signatures and missing or mismatched references.
- Invalid fractional VND, overflow, provider amount limits, wrong owner,
  failed persistence and a missing wallet.
- Refunds, disputes and chargebacks need an explicitly approved live-use
  policy; automatic handling is out of scope for the initial test-mode work.

## Functional Requirements

- **FR-001**: The service MUST create hosted Stripe payments for top-ups and
  direct orders, with VND represented as whole units.
- **FR-002**: The service MUST durably correlate each payment attempt and
  safely retry creation without duplicate charges.
- **FR-003**: The service MUST authenticate and authorize initiation and
  status access using the existing ownership rules.
- **FR-004**: The service MUST verify provider callbacks before processing and
  credit or settle only a confirmed paid attempt.
- **FR-005**: The service MUST handle cards and enabled delayed payment
  methods, including delayed success, failure and expiry.
- **FR-006**: Payment processing MUST be idempotent and preserve atomic
  balance, ledger and audit updates under concurrent or repeated events.
- **FR-007**: A failed or expired direct attempt MUST allow a new attempt for
  the same unpaid order only after the provider confirms that the earlier
  attempt can no longer succeed. Unexpected additional successful charges
  MUST be recorded and escalated without settling the order again.
- **FR-008**: The service MUST remove VNPay runtime flows and configuration
  after safe cutover; destructive database changes require separate approval.
- **FR-009**: The service MUST preserve the frozen CQRS architecture, existing
  ownership rules and the non-negative available-balance invariant.
- **FR-010**: Test-mode setup instructions MUST explain account configuration,
  webhook setup, secret handling and an end-to-end verification procedure.
- **FR-011**: Live launch MUST remain gated on account eligibility, VND
  support, payment methods, approval for the wallet-funding model, and a
  refund/dispute/reversal policy.

## Key Entities

- **Payment attempt**: A durable top-up or direct-order attempt, its amount,
  lifecycle, retry identity and provider references.
- **Wallet top-up**: A user's requested wallet funding and its resulting state.
- **Direct payment order**: An order's direct payment state and attempts.
- **Provider event**: A verified notification and its processing outcome.
- **Wallet transaction and audit**: The financial and audit records of a
  successful operation.

## Success Criteria

- **SC-001**: In test scenarios, every successful top-up credits the intended
  wallet exactly once, including duplicate and concurrent notifications.
- **SC-002**: In test scenarios, direct payments do not alter wallet balances,
  and only one attempt can settle an order successfully.
- **SC-003**: Delayed and failed payments remain uncredited until provider
  success; no browser redirect alone changes financial state.
- **SC-004**: All required architecture checks, Release build and automated
  tests pass; existing unrelated wallet operations retain their behavior.
- **SC-005**: A developer can follow the setup guide to create and verify a
  Stripe test-mode payment and webhook without placing secrets in source.

## Assumptions

- Stripe-hosted Checkout is used; both cards and delayed methods are in scope.
- Existing VNPay history is not considered worth preserving, but no database
  reset, destructive migration or deletion is authorized by this assumption.
- Automated Stripe refunds, disputes and reversals are excluded from initial
  test-mode implementation and require a separate live-use decision.
- Stripe account eligibility, VND presentment and permission for transferable
  wallet funding are unconfirmed and must be checked before live launch.
