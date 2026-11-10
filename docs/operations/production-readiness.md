# Wallet Service Production Readiness Checklist

Last reviewed: 2026-11-10

## Decision

**Not approved for direct public exposure or broad production use.** The current service is suitable for development and controlled integration testing. A production decision requires evidence for every applicable P0 gate below and explicit owner sign-off.

This checklist records the current implementation and deployment boundaries; it does not prescribe replacing the project's intended gateway-forwarded identity model. The API has no configured JWT authentication, and identity/role headers are trusted by the application. Any deployment using this model must be isolated behind a gateway that authenticates callers, removes client-supplied identity headers, and writes trusted values. Never expose the API directly to untrusted networks.

## Current implementation to account for

- .NET 8 REST API with classic MediatR/CQRS, PostgreSQL, and a domain rule preventing negative available balances.
- `GET /health` returns `Healthy` unconditionally; it does not verify database connectivity or readiness.
- The API applies EF Core migrations at startup for relational databases. Migration/seeding exceptions are logged as warnings and startup continues.
- User withdrawals reserve available funds. Admin approval consumes the reservation and records a debit; rejection releases the reservation. Neither path sends a bank payout. Seller verification, payout execution, and retry handling are outside the current implementation. Admin ledger and reconciliation report routes exist, but they only compare wallet balances with the internal ledger; reconciliation against bank or payment-provider records is not implemented.
- VNPay top-ups and direct checkout rely on signed IPN processing. Direct checkout reuses a matching pending request for the same order; this is not idempotency for transfers or top-up creation.
- The checked-in sample configuration and Compose defaults are for local/development use (including VNPay sandbox settings). Do not use them as a production deployment configuration.

## P0 — Required before any production financial traffic

### Identity and network boundary

- [ ] Record the deployed trust boundary and verify the gateway authenticates every caller.
- [ ] Verify the gateway strips inbound `x-user-id`, `x-user-roles`, `x-user-email`, and `x-user-permissions` headers, then injects authoritative values.
- [ ] Restrict network access so clients cannot bypass the gateway and reach WalletService directly.
- [ ] Verify admin/staff routes cannot be reached with caller-controlled role headers.

### Product and money movement scope

- [ ] Decide whether withdrawals are in scope for launch. If they are, define and test the authorized payout procedure, payout result/failure handling, and reconciliation before approving withdrawal requests as a production cash-out flow.
- [ ] If bank payout is not part of launch, disable or clearly withhold the withdrawal capability from production clients; do not describe an approved request as a completed bank transfer.
- [ ] Review the admin reconciliation report (`GET /api/v1/admin/wallets/reconciliation`) for balance/ledger differences, reconcile against payment-provider and bank records outside the service, and define ownership and frequency for investigating differences.
- [ ] Verify every balance-changing flow is transactional, auditable, and protected against concurrent execution; record actor, operation/reference, and trace/correlation identifier where available.

### Payments and retry safety

- [ ] Verify production VNPay merchant credentials, payment URL, HTTPS return URL, callback routing, and provider account configuration. No sandbox credentials or URLs may be used in production.
- [ ] Exercise valid, invalid-signature, amount/currency mismatch, duplicate, out-of-order, and concurrent IPN scenarios against PostgreSQL using provider-approved test procedures.
- [ ] Verify callback retries cannot credit or settle a transaction more than once, and document alerting and manual reconciliation for unresolved payments.
- [ ] Define client retry behavior and duplicate protection for every financial operation. Direct-checkout order matching does not cover repeated top-up creation or transfers.

### Database and release operations

- [ ] Replace the unconditional health endpoint with deployment-appropriate liveness/readiness checks; readiness must fail when required dependencies such as PostgreSQL are unavailable.
- [ ] Decide whether automatic schema migration at API startup is acceptable for the deployment. If not, apply reviewed migrations as a controlled release step. In either case, migration failure must block traffic or fail deployment rather than leave a partially ready service.
- [ ] Review each production migration, backup the database before schema changes, and document a tested rollback/recovery procedure.
- [ ] Demonstrate a database restore drill and define backup retention, access controls, and recovery objectives.

### Production configuration and secrets

- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` explicitly and use a production-specific deployment manifest rather than the development Compose defaults.
- [ ] Load database credentials, VNPay secrets, and other sensitive configuration from the deployment secret manager; verify they are absent from source, images, logs, and diagnostics.
- [ ] Configure production database TLS and network restrictions; do not carry local `SSL Mode=Disable` settings into production.
- [ ] Verify allowed hosts, HTTPS termination, proxy behavior, request limits, and Swagger exposure for the actual deployment topology.

## P1 — Required before expanding rollout

### Security and abuse controls

- [ ] Define request-rate and abuse limits appropriate to payment, transfer, and callback traffic.
- [ ] Review CORS, payload-size limits, and outbound/network egress rules for the deployed topology.
- [ ] Confirm logs redact credentials, payment data, bank account data, and other sensitive values; restrict access and retention.
- [ ] Review authorization for user, admin, and staff operations, including ownership checks and negative/unauthorized cases.

### Reliability and observability

- [ ] Export and verify traces, metrics, and structured logs in the production telemetry environment; ensure trace IDs can be followed across gateway and service.
- [ ] Alert on elevated API errors/latency, database availability, payment failures or pending-payment age, rejected callbacks, and reconciliation drift.
- [ ] Define on-call ownership, incident escalation, and safe procedures for pausing payment or withdrawal traffic.
- [ ] Test restart, database outage, provider timeout, and recovery behavior without duplicate financial effects.

### Verification evidence

- [ ] Run `./scripts/verify-wallet-architecture.sh` and confirm `OK: Wallet CQRS architecture is intact`.
- [ ] Run `dotnet build Wallet.sln -c Release`.
- [ ] Run `dotnet test Wallet.sln -c Release`; retain the output for the release record rather than relying on a stale test count.
- [ ] Run integration tests against PostgreSQL for transfer concurrency, balance invariants, withdrawal reservation/decision, VNPay callback settlement, and direct-checkout retry behavior.
- [ ] Complete gateway-to-WalletService and payment-provider end-to-end tests in the production-like environment.

## Release sign-off

Do not mark this checklist complete based only on a successful build or unit-test run. Attach the deployment configuration review, test evidence, database recovery evidence, and named approvals from the service owner, gateway/security owner, payment owner, and database/operations owner.

| Area | Owner | Evidence / link | Decision / date |
|---|---|---|---|
| Gateway identity and network boundary | | | |
| Payments and reconciliation | | | |
| Withdrawal scope and payout operations | | | |
| Database migration and recovery | | | |
| Monitoring and incident response | | | |
| Release verification | | | |
