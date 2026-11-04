# Wallet Service Production Readiness Checklist

## Executive Summary

This repository shows a solid internal-service foundation for a wallet platform, with a clean CQRS/MediatR structure, domain-level balance rules, and a passing test suite. However, it is not yet production-ready for direct public exposure because the identity/auth boundary is still trust-based on forwarded headers, some features are intentionally incomplete, and key financial flows require stricter production safeguards.

The service is best positioned as a trusted internal API behind an authenticated gateway or BFF until the items below are completed.

## Current Assessment

Status: Needs hardening before broad production rollout

- Strengths
  - Clean Domain / Application / Infrastructure / API layering
  - MediatR pattern used consistently
  - Core wallet balance rules enforced in the domain model
  - 80 tests passing
  - Clear documentation of API contracts and deployment boundaries

- Risks
  - Identity trust depends on `x-user-id` and `x-user-roles` headers
  - Wallet can create and review pending withdrawal requests, but does not verify seller status or execute/reconcile bank payouts
  - Payment callback verification and replay protection must be formally hardened
  - Financial operations need stronger idempotency and concurrency safeguards

## P0 — Must fix before production

- [ ] Enforce a real authentication boundary at the service
  - The README explicitly states forwarded identity headers are only safe behind a trusted gateway.
  - The service should reject direct spoofing and not rely on client-supplied identity values.
  - Configure proper JWT or equivalent service-level authentication at the API boundary.

- [ ] Remove or harden spoofable identity assumptions
  - `x-user-id`, `x-user-roles`, and related headers are currently treated as trusted when present.
  - This is acceptable only behind a verified upstream gateway; it is not safe for a public-facing deployment.

- [ ] Complete or hide the user withdrawal flow
  - Wallet creates pending requests and reserves funds; staff can approve or reject them.
  - Seller verification, bank payout execution, payout failure/retry handling, and reconciliation need a separately agreed contract and tests.

- [ ] Verify payment callback security end-to-end
  - VNPay IPN validates HMAC-SHA512, configured merchant code, VND currency, and integer minor-unit amount for both top-ups and direct orders. Pending records are row-locked and terminal callbacks are not settled twice.
  - Complete signed-provider sandbox verification, PostgreSQL concurrent-replay testing, and financial reconciliation before marking this item complete.

- [ ] Add transaction idempotency and duplicate protection
  - Direct VNPay checkout now persists a unique order/payment intent and reuses the provider reference for matching pending retries.
  - Repeated top-up and transfer requests still need operation-level idempotency; checkout idempotency does not cover all financial operations.
  - This is essential for financial correctness.

- [ ] Add explicit auditability for all money movement
  - Every balance-changing action should be traceable to a request, actor, and correlation ID.
  - Business-state transitions should be recorded in the same transaction boundary where practical.

## P1 — High priority before broader rollout

- [ ] Implement and test the complete withdrawal lifecycle
  - User request creation and admin approval/rejection are implemented with wallet fund reservation.
  - Seller verification, bank payout execution, and payout failure/retry handling are not implemented.

- [ ] Add comprehensive request validation
  - Validate all public inputs consistently.
  - Standardize on the repo’s problem-details/error model.
  - Ensure every command/query has meaningful validation and error mapping.

- [ ] Add concurrency and race-condition protection
  - Simultaneous transfers and debits must not create negative balances or inconsistent ledger rows.
  - Review wallet mutations for lock semantics or optimistic concurrency protections.

- [ ] Harden API security posture
  - Rate limiting
  - CORS policy
  - request-size limits
  - secret management via environment/secret manager only
  - log sanitization to avoid leaking sensitive values

- [ ] Improve operational observability
  - Health metrics
  - wallet balance drift alerts
  - failed payment monitoring
  - request tracing with correlation IDs

## P2 — Important for larger-scale adoption

- [ ] Add end-to-end integration tests for real flows
  - VNPay IPN success/failure paths
  - transfer race scenarios
  - withdrawal approval/rejection
  - direct checkout state transitions

- [ ] Define disaster recovery and DB operations
  - backup strategy
  - migration review and rollback plan
  - restore drills

- [ ] Document lifecycle states clearly
  - pending / approved / rejected / failed states
  - idempotency semantics
  - wallet balance semantics

- [ ] Add feature gating
  - Keep incomplete or experimental endpoints behind flags until they are fully validated.

## Features to Prioritize to Reach Production Readiness

### 1. Secure identity and auth boundary
This is the highest-priority item. Without a trustworthy auth layer, the service should not be exposed directly.

### 2. Complete the withdrawal feature
This is the most visible incomplete business capability and likely blocks end-to-end user wallet usage.

### 3. Harden payment processing and callback security
Protect against duplicate or forged callbacks, and ensure the top-up state machine is resilient.

### 4. Add stronger financial integrity controls
Protect against negative balances, double-crediting, duplicate workflows, and race conditions.

### 5. Improve visibility and incident response
Operational monitoring is necessary for any production money movement service.

### 6. Finalize external integration verification
Payments and gateway integration must be validated end-to-end before broad rollout.

## Recommended Rollout Stance

### Suitable for
- Internal services behind a trusted authenticated gateway
- Controlled, low-risk deployments with strong upstream auth

### Not yet suitable for
- Direct public exposure to untrusted clients
- Broad production rollout without a hardened auth boundary and completed wallet flows

## Suggested Implementation Order

1. Authentication + trusted identity boundary
2. Withdrawal workflow completion
3. Payment callback verification and idempotency
4. Concurrency safety and financial invariants
5. Monitoring and alerting
6. Full end-to-end validation and feature gating

## Bottom Line

The repository has strong engineering fundamentals and a solid internal-service architecture, but it still needs production hardening before it should be treated as a public-facing financial service. The most important work is securing the boundary, finishing the withdrawal lifecycle, and closing the gaps around payment security and financial consistency.
