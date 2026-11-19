# Wallet Service Production Readiness Checklist

Last reviewed: 2026-11-19 (branch `feature/handover-wallet-hardening`)

## Decision

**Not approved for production or live-money deployment.** Suitable for development and controlled staging/integration testing only. A production go-live decision requires closing every P0 blocker below (including the expired .NET 8.0 runtime, Stripe test-mode restriction, database readiness/migration hardening, secret-history purge, PostgreSQL integration test execution, and cross-service cutovers) with explicit owner sign-off.

This checklist records the current implementation and deployment boundaries; it does not prescribe replacing the project's intended gateway-forwarded identity model. The API has no configured JWT authentication, and identity/role headers (`x-user-id`, `x-user-roles`, `x-user-email`, `x-user-permissions`) are trusted by `UserContextMiddleware`. Any deployment using this model must be isolated behind a gateway that authenticates callers, strips client-supplied identity headers, writes authoritative values, and never exposes `/api/v1/internal/*`. Never expose port `8008` directly to untrusted networks.

## Current implementation to account for

- .NET 8 REST API (runtime reached end-of-support on **2026-11-10**; upgrade to .NET 10 LTS pending) with classic MediatR/CQRS (`46` handlers: `28` commands, `18` queries across `WalletsController` and `AdminWalletsController`), PostgreSQL, and a database check constraint (`CK_Wallets_BalanceLocks`) plus domain rules preventing negative balances (`Balance >= 0`, `LockedBalance >= 0`, `LockedBalance <= Balance`).
- `GET /health` returns `{ "status": "Healthy", "service": "wallet-service" }` unconditionally; it does not verify database connectivity or readiness.
- `Program.cs` calls `db.Database.Migrate()` on startup for relational databases and catches/logs warnings on error, allowing startup to continue in a degraded state. Additive migration `20261119020448_HandoverWalletHardening` is generated and verified clean against the EF model, pending execution against live PostgreSQL.
- Withdrawals implement the 6-step workflow (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, `ResolveFailed`), saved seller `BankAccounts` CRUD/default selection, `RequestWithdrawal` `Idempotency-Key` + SHA-256 fingerprint protection, `WALLET_FROZEN` guards, seller cancellation, unique `TransferReference` enforcement on `Complete`, and `admin`-only enforcement on `ResolveFailed` (`staff` is forbidden). WalletService still does not initiate external bank payouts or verify seller KYC; `Complete` records manual transfer confirmation (`TransferConfirmed`, `TransferReference`).
- Stripe Checkout handles top-ups and direct checkout with signed webhooks, startup configuration validation (`StripeSettings.EnsureValid`), HTTPS return-URL rules outside Development (`StripeSettings.RequireReturnUrl`), and a 1-minute background worker (`StripeTopupReconciliationWorker`, plus `POST /api/v1/admin/wallets/stripe/reconcile-pending`) reconciling both `Topup` and `DirectOrder` attempts. Direct checkout supports buyer `resume` and `cancel` plus internal `expire`; admin endpoints list/resolve `ReconciliationRequired` attempts (`Failed`/`Expired`). Remaining gaps: `CreationPending` attempts without a stored session ID cannot be resolved via `ResolveStripeAttempt`, `CancelDirectPayment` returns `409 PAYMENT_ALREADY_COMPLETED` if payment raced ahead rather than calling Stripe Refunds, and `POST /api/v1/wallets/transfers` still has no `Idempotency-Key` protection.
- Freeze, order escrow, and internal service routes: `FreezeWallet` / `UnfreezeWallet` (admin/staff and `~/api/v1/internal/wallets/freeze-purchase` / `unfreeze-purchase`) block `PayOrderWithWallet`, `Transfer` (sender & recipient), `RequestWithdrawal`, `AdjustOrderPayment`, and `CreateHold` with `403 WALLET_FROZEN`. `PayOrderWithWallet` checks `wallet.AvailableBalance` (excluding holds/withdrawals). `AdjustOrderPayment` supports idempotent post-order debit/refund deltas against order escrow. All `~/api/v1/internal/wallets/...` routes and the 4 legacy Commerce routes require `[RequireInternalServiceKey]` (`X-Internal-Service-Key` validated via constant-time SHA-256 comparison against `INTERNAL_SERVICE_SECRET` / `InternalSecurity:ServiceKey`; Auction hold routes use `[RequireAuctionServiceKey]`). Legacy `InternalServiceAuthMiddleware` remains disabled in `Program.cs`. `sub-orders/{subOrderId}/refund` (`IsSubOrder = true`) still skips original `PurchaseDebit` lookup and escrow validation.
- Inter-service notifications: `NotificationClient` (`INotificationClient`, `NOTIFICATION_SERVICE_URL` + optional `Services:Notification:ServiceKey`, 10s timeout) dispatches post-commit REST events for `withdrawal-events` and `auction-deposits` with deterministic `sourceEventId` and `traceId`, recording failures in audit logs (`NotificationDispatchFailed`) without blocking or rolling back committed ledger state. There is no durable outbox or retry queue yet.
- Release feature gates (`ReleaseFeatureAttribute`) disable `Withdrawals` (`POST /api/v1/wallets/withdrawals`), `AuctionOrderCapture` (`holds/{holdId}/capture`), and `LegacyCommerceCredits` (`sub-orders/{id}/settle`, `orders/{id}/refund`, `sub-orders/{id}/refund`) by default outside Development unless explicitly overridden via `ReleaseFeatures:*Enabled`. See the [API contract](../api/wallet-payments.md) and [cross-service handover](../integrations/handover-response.md).
- Stripe integration rejects live keys (`!sk_test_`) and live webhook events (`livemode != false`). Live-money rollout is blocked until live-mode support, credentials, and provider operational procedures are approved and verified.
- Checked-in sample configuration and `docker-compose.yml` defaults (`ASPNETCORE_ENVIRONMENT=Development`) are for local development only; do not use them for production.

## P0 — Required before any production financial traffic

### Identity, network boundary, and secrets hygiene

- [x] Enforce `[RequireInternalServiceKey]` (`X-Internal-Service-Key` constant-time SHA-256 check) on `~/api/v1/internal/wallets/*` and the 4 legacy Commerce routes, plus `[RequireAuctionServiceKey]` on Auction hold routes (`ApiExplorerSettings(IgnoreApi = true)` on internal routes).
- [x] Restrict `AdminWalletsController` manual adjustments (`AdjustBalance`), ledger/reconciliation reads, Stripe reconciliation resolution, and `ResolveFailed` withdrawal actions to the `admin` role (`403` for `staff`).
- [ ] Record the deployed trust boundary and verify the gateway authenticates every caller, blocks `/api/v1/internal/*` from public ingress, and strips inbound `x-user-id`, `x-user-roles`, `x-user-email`, and `x-user-permissions` headers before writing trusted values.
- [ ] Restrict network access so clients cannot bypass the gateway and reach WalletService directly on port `8008`.
- [ ] Verify admin/staff routes cannot be reached with caller-controlled role headers.
- [ ] Rotate the Neon DB password/secrets previously committed in git history (`ea3987f`, `17f719e`), purge git history (`git filter-repo`), and switch CI secret scanning from `gitleaks --no-git` to full-history checks.

### Product, platform lifecycle, and money movement scope

- [ ] Upgrade target framework from **.NET 8.0** (end of support **2026-11-10**, overdue) to **.NET 10 LTS** and update EF Core, Npgsql, OpenTelemetry, Swashbuckle, and MediatR (reviewing licensing).
- [x] Enforce database check constraint `CK_Wallets_BalanceLocks` (`Balance >= 0 AND LockedBalance >= 0 AND LockedBalance <= Balance`), `AvailableBalance` validation on `PayOrderWithWallet`, `WALLET_FROZEN` guards across wallet debits/withdrawals/transfers/adjustments/holds, and default `Withdrawals` release gating outside Development.
- [ ] Confirm intended release gate settings (`WithdrawalsEnabled`, `AuctionOrderCaptureEnabled`, `LegacyCommerceCreditsEnabled`) before launch; note WalletService does not perform bank transfers or seller KYC verification.
- [ ] Reconcile wallet balances against the transaction ledger and Stripe records operationally, and define ownership, frequency, and alert thresholds for investigating discrepancies or `NotificationDispatchFailed` audit logs.
- [ ] Complete CommerceService cutover onto `~/api/v1/internal/wallets/*`, agree funding-backed Commerce settlement/refund contracts (including aligning `sub-orders/{subOrderId}/refund` to validate an original `PurchaseDebit`), and align platform forfeiture payout ownership.
- [ ] Confirm AuctionService hold integration, live deposit escrow handling, and duplicate-prevention expectations (including `BUYER_WITHDRAWN` / post-commit `auction-deposits` notifications) in staging.

### Payments, notifications, and retry safety

- [x] Enforce Stripe test-mode validation (`StripeSettings.EnsureValid` at startup) and extend `StripeTopupReconciliationWorker` / `ReconcilePendingTopups` to reconcile both `Topup` and `DirectOrder` attempts, plus direct-checkout `resume` / `cancel` / `expire` and admin `ReconciliationRequired` resolution.
- [ ] Enable live-mode Stripe credentials (`sk_live_`, `livemode == true` webhooks), verify HTTPS return URLs and webhook forwarding in staging/prod, and exercise valid, invalid-signature, amount/currency mismatch, duplicate, out-of-order, and concurrent webhook scenarios against PostgreSQL.
- [ ] Add `Idempotency-Key` retry protection to `POST /api/v1/wallets/transfers` (currently unprotected on client retry).
- [ ] Define and automate operational recovery for `CreationPending` Stripe attempts without a stored session ID (excluded from `ResolveStripeAttempt`) and race conditions where `CancelDirectPayment` encounters `409 PAYMENT_ALREADY_COMPLETED` (no automatic Stripe Refund call).
- [ ] Verify `NotificationService` (`NOTIFICATION_SERVICE_URL`) end-to-end delivery and add durable retry/replay procedures for failed `withdrawal-events` and `auction-deposits` webhook dispatches (`NotificationDispatchFailed`).
- [ ] Before enabling direct checkout or post-order adjustments in production, verify Commerce validates the buyer, authoritative order amount, and order state independently.

### Database and release operations

- [ ] Replace the unconditional `/health` endpoint with deployment-appropriate `/health/live` and `/health/ready` checks; readiness must fail when PostgreSQL or required configuration is unavailable.
- [ ] Stop swallowing exceptions on startup `db.Database.Migrate()` (`Program.cs`); apply reviewed migrations (including `20261119020448_HandoverWalletHardening`) as a controlled release step before traffic starts and fail fast on error.
- [ ] Apply and verify `20261119020448_HandoverWalletHardening` against a real PostgreSQL instance and verify rollback/backup-restore drills, RPO/RTO targets, and access controls.

### Production configuration and secrets

- [x] Remove hardcoded `Trust Server Certificate=true` (`Wallet.Infrastructure/DependencyInjection.cs` now parses PostgreSQL URIs with `SslMode.Require`) and remove the default `INTERNAL_SERVICE_SECRET` fallback in `docker-compose.yml`.
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` explicitly (disables Swagger/dev seeding) and deploy using a production manifest and external secret manager rather than Compose dev defaults or `.env` files.
- [ ] Enforce production database TLS, allowed hosts (`AllowedHosts`), HTTPS/HSTS, forwarded-headers validation, and request payload-size limits.

## P1 — Required before expanding rollout

### Security, CI/CD, and abuse controls

- [ ] Harden `.github/workflows/ci-cd.yml` to run `verify-wallet-architecture.sh`, `dotnet build -c Release`, `dotnet test -c Release`, `dotnet list package --vulnerable`, and container image scanning (Trivy) as blocking gates before pushing images.
- [ ] Harden `Dockerfile` to run as a non-root user, add `HEALTHCHECK`, and pin `aspnet:8.0` / `sdk:8.0` (or `.NET 10`) base image digests.
- [ ] Define rate limiting and abuse controls on top-ups, transfers, direct checkout, withdrawals, and webhooks; review CORS, egress rules, and log/audit PII redaction (e.g., bank account masking in logs/notes).

### Reliability, error mapping, and observability

- [ ] Export OpenTelemetry metrics and configure production alerts alongside existing tracing (`ObservabilityExtensions.cs`): API 5xx/latency, DB lock timeouts, pending-payment age, rejected webhooks, reconciliation drift, and `NotificationDispatchFailed` rate.
- [ ] Update `GlobalExceptionHandlerMiddleware` so `ArgumentException` messages are not leaked verbatim, `DbUpdateConcurrencyException` / PostgreSQL lock timeouts map cleanly to `409`/`503` instead of `500`, and clarify `ListTransactions` string-vs-numeric enum parsing.
- [ ] Define on-call ownership, runbooks for toggling `ReleaseFeatures:*Enabled` during incidents, and restart/outage/timeout chaos testing without duplicate financial effects.

### Verification evidence

- [x] `./scripts/verify-wallet-architecture.sh` passed (`OK: Wallet CQRS architecture is intact`, `46` handlers: `28` commands, `18` queries, `2` controllers, MediatR wired — see Verification summary).
- [x] `dotnet build Wallet.sln -c Release` passed with `0` warnings and `0` errors.
- [~] `dotnet test Wallet.sln -c Release` passed `455` unit/in-memory tests with `0` failures and `36` skipped PostgreSQL integration tests (`WALLET_STRIPE_TEST_DATABASE` unset).
- [ ] Execute all `36` PostgreSQL integration tests against a disposable PostgreSQL instance (including migration `20261119020448_HandoverWalletHardening`, row locks, concurrency, and rollback).
- [ ] Complete gateway-to-WalletService, Stripe webhook/reconciliation, Commerce, Auction, and NotificationService end-to-end tests in staging.

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
