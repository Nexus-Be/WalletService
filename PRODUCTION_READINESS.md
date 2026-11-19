# WalletService — Production Readiness Checklist

> Current branch note (2026-11-19, `feature/handover-wallet-hardening`):
> **Not approved for production or live-money deployment** (development and
> controlled staging/integration testing only). Stripe (`sk_test_`) is the active
> payment provider, Phases 0–3 of Wallet hardening and post-commit REST
> notification dispatch (`INotificationClient`) are implemented, and the active
> architecture invariant is **46 handlers (28 commands, 18 queries)** across
> **2 public controllers** (`WalletsController`, `AdminWalletsController`) and
> **0 internal controllers** (`~/api/v1/internal/wallets/...` routes reside on
> `WalletsController` behind `[RequireInternalServiceKey]` /
> `[RequireAuctionServiceKey]`). Key remaining blockers: `.NET 8.0` reached
> end-of-support on **2026-11-10**, `36` PostgreSQL integration tests remain
> skipped (`WALLET_STRIPE_TEST_DATABASE` unset), migration
> `20261119020448_HandoverWalletHardening` is unapplied to live PostgreSQL,
> Stripe remains test-mode-only, `/health` is unconditional, and git-history
> secret purging plus Commerce/Auction/Notification/Gateway staging acceptance
> remain open. Historical entries below retain their stated dates; see
> [current readiness](docs/operations/production-readiness.md) and
> [cross-service handover](docs/integrations/handover-response.md).

> Review date: 2026-11-09. Baseline on `dev`+this branch: `dotnet build -c Release` 0 errors, 146 tests passing, no known vulnerable NuGet packages. See also the team's higher-level [docs/operations/production-readiness.md](docs/operations/production-readiness.md).
> Verdict: **not ready for production** — items 1–4 are launch blockers.
> We will work through the items one by one; tick the box and note the commit when an item is done.

Legend: `[ ]` open · `[~]` in progress · `[x]` done · `[-]` won't do (accepted risk)

> Scope note (2026-11-09): this is a college project that will not be deployed. Items are kept as a reference; we'll re-triage each one as we reach it.

---

## P0 — Launch blockers

### [~] 1. Rotate and purge leaked secrets
- `.env.server.example` is tracked and contains a Neon connection string with what looks like a real password (in git history: `ea3987f`, `17f719e`).
- The same `INTERNAL_SERVICE_SECRET` value is committed in `.env.example`, `.env.local.example`, `.env.server.example`, `Wallet.Api/appsettings.example.json`, `Wallet.Api/appsettings.Development.example.json`, and is the default in `docker-compose.yml` (`${INTERNAL_SERVICE_SECRET:-<value>}`).
- To do:
  - [ ] Rotate the Neon DB password (owner action) and the internal service key.
  - [x] Replace all values in tracked example files with placeholders.
  - [x] Remove the compose default so startup fails when the variable is unset.
  - [ ] Purge history (`git filter-repo`) and force-push; ask collaborators to re-clone.
  - [~] Add secret scanning (gitleaks) to CI — working-tree scan added and gates the image build; switch to full-history scan after the purge.

### [-] 2. Real authentication in front of the service — WON'T DO (accepted risk)
- Decision (2026-11-09): this is a college project that will not be deployed. The gateway-forwarded `x-user-id` / `x-user-roles` header model is the intended authentication method and stays as is.
- Known and accepted: `UserContextMiddleware.cs` / `HttpContextExtensions.cs` trust these headers from any caller reaching port `8008`. Note (2026-11-19): `AdjustBalance` and withdrawal `ResolveFailed` now enforce `admin` (`staff` is forbidden with `403`), and all `~/api/v1/internal/wallets/*` and 4 legacy Commerce routes now enforce `[RequireInternalServiceKey]` (`X-Internal-Service-Key`).
- If this project is ever deployed, revisit: validate a JWT or restrict header trust to the gateway, and don't publish port 8008.
- Consequence for other items: anything justified mainly by "anyone can reach the service" (e.g. item 3's severity) is downgraded.

### [x] 3. Remove demo seed data from migrations — DONE (2026-11-09)
- `Wallet.Infrastructure/Persistence/Seeds/WalletSeedData.cs` was applied by `InitialCreate`: wallets with balances (system wallet 1,000,000,000 VND; admin/staff/seller/buyer wallets), transactions, withdrawals, IPN logs — all with predictable GUIDs.
- Combined with the accepted auth model (item 2), anyone who can reach the service can spend this money; fine for local demos, but the seed should still be Development-only.
- Done:
  - [x] Seeding moved out of the EF model into an idempotent runtime seeder, applied at startup only when `ASPNETCORE_ENVIRONMENT=Development` (`Program.cs`).
  - [x] `InitialCreate` and `AddDirectPaymentOrderIdempotency` regenerated without seed data, keeping both migration IDs, so existing databases see no pending migration and keep their rows (schema identical; only `InsertData`/`HasData`/seed `UpdateData` removed).
  - [x] Tests (`SeedDataTests`): model has no seed data, migration has no `InsertData`, fresh DB has zero wallets, seeder is idempotent.
  - [x] Verified on real Postgres: fresh Production DB has 0 wallets/transactions; Development DB gets the 5 demo wallets.

### [x] 4. Complete the money flows — DONE on `dev` + status endpoint (2026-11-09)
- On review, `origin/dev` already implemented most of this (VNPay-only direct checkout with `DirectPaymentOrder` + request fingerprint + unique `OrderId` index, IPN settlement of direct payments, user withdrawal requests with fund locking). The parallel implementation done on `main` was dropped in favour of dev's. Contract: `docs/api/checkout/direct.md`.
- Decisions: **expiry of pending payments is owned by Commerce Service, not Wallet.**
- Added on this branch:
  - [x] `GET /api/v1/wallets/checkout/direct/{orderId}` (`GetDirectPaymentStatusQuery`; 15th use case) so callers can poll Pending → Success/Failed. Doc: `docs/api/checkout/direct-status.md`; frontend note: `FRONTEND_API_CHANGES.md`.
  - [x] Freeze invariants updated to 15 handlers (11 Command, 4 Query) in `AGENTS.md`, `.agents/`, specs, README and `scripts/verify-wallet-architecture.sh`.
  - [x] Extra tests for transfers, admin adjustments, withdrawal processing and IPN edge cases (`WalletOperationsEdgeCaseTests`) and the status endpoint (`DirectPaymentStatusTests`).
- Follow-ups (not done): IPN response shape/GET (item 5), no endpoint for a user to list their withdrawals, `VnpayIpnLogs` still not written (item 5).

### Wallet Phase 2 financial operations — verification in progress on `feature/wallet-phase2-stripe` (2026-11-12)
- [x] Reimplement withdrawal transfer-reference handling and add the admin ledger and wallet-reconciliation reports.
- [x] Update the active architecture invariant to 17 handlers (11 commands, 6 queries) and 2 public controllers; historical entries above retain their original counts.

### Handover hardening & notification integration — implemented on `feature/handover-wallet-hardening` (2026-11-19)
- [x] Security & balance invariants: enforced `[RequireInternalServiceKey]` (SHA-256 constant-time header check) on `~/api/v1/internal/wallets/*` and legacy Commerce routes, `admin`-only checks on `AdjustBalance` and withdrawal `ResolveFailed`, `WALLET_FROZEN` guards on debits/transfers/withdrawals/adjustments/holds, `AvailableBalance` validation on `PayOrderWithWallet`, and `CK_Wallets_BalanceLocks` + filtered unique indexes in migration `20261119020448_HandoverWalletHardening`.
- [x] Lifecycle & features: added 6-step withdrawal workflow (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, `ResolveFailed`), saved seller `BankAccounts` CRUD/default fallback, `RequestWithdrawal` `Idempotency-Key` + fingerprint, seller withdrawal cancel, `GetTransactionById`, `FreezeWallet`/`UnfreezeWallet`, direct-checkout `resume`/`cancel`/`expire`, multi-purpose (`Topup` + `DirectOrder`) Stripe reconciliation, admin reconciliation/resolve endpoints, `AdjustOrderPayment`, and post-commit `INotificationClient` events (`withdrawal-events`, `auction-deposits`).
- [x] Updated architecture invariant to **46 handlers (28 commands, 18 queries)** across **2 controllers** (`./scripts/verify-wallet-architecture.sh` passed; `455` unit/in-memory tests passed, `36` PostgreSQL integration tests skipped).

---

## P1 — Before go-live

### [x] 5. VNPay IPN contract — DONE (2026-11-09)
- Verified against VNPay's official docs (sandbox.vnpayment.vn → "Cài đặt Code IPN URL"): the IPN is a **GET** with query parameters, needs SSL, and the merchant must answer JSON `{"RspCode","Message"}`; `00`/`02` end the flow, `01`/`04`/`97`/`99` (or timeout) make VNPay retry up to 10× at 5-minute intervals.
- Done:
  - [x] `GET /api/v1/wallets/vnpay/ipn` added (POST form/JSON still accepted); repeated parameter names (also case variants) rejected with `99`.
  - [x] Response serializes as `{"RspCode":"00","Message":"Confirm Success"}`; duplicate is `{"RspCode":"02","Message":"Order already confirmed"}` (C# DTO unchanged, wire names via `JsonPropertyName`).
  - [x] A payment is successful only if `vnp_ResponseCode == 00` and, when present, `vnp_TransactionStatus == 00` (VNPay's own sample rule), for top-ups and direct payments.
  - [x] Every callback that reaches the handler is persisted in `VnpayIpnLogs` (jsonb payload, bounded size, processing status), written outside the settlement transaction so rejected callbacks are kept.
  - [x] 21 new tests (`VnPayIpnContractTests`), including a really HMAC-signed GET through the controller and MediatR pipeline; verified on real Postgres (raw body, GET/POST, replay, tampering, jsonb log rows).
- Operations: register the IPN URL with VNPay as the public **HTTPS** `GET` URL (`https://<host>/api/v1/wallets/vnpay/ipn`); a local `https://localhost` URL cannot receive it.
- Not done: IP allowlist of VNPay (no stable published list; signature + merchant code are the authenticity controls), rate limiting of the anonymous endpoint (item 8; unlogged-before-handler rejections cannot fill the log table, but handler-reaching junk can), concurrent-replay test on PostgreSQL.

### [~] 6. Database safety
- [x] Remove hardcoded `Trust Server Certificate=true` (`Wallet.Infrastructure/DependencyInjection.cs` now parses PostgreSQL URIs via `NpgsqlConnectionStringBuilder` with `SslMode = SslMode.Require`).
- [ ] Stop swallowing exceptions / auto-migrating blindly on app start (`Program.cs`): apply reviewed migrations (including `20261119020448_HandoverWalletHardening`) as a controlled deploy step and fail fast on migration error.
- [x] Add DB constraints: `CK_Wallets_BalanceLocks` (`Balance >= 0 AND LockedBalance >= 0 AND LockedBalance <= Balance`), unique `DirectPaymentOrders.OrderId` index, and filtered unique indexes on withdrawals, transactions, and bank accounts (migration `20261119020448_HandoverWalletHardening` — verify on live PostgreSQL).
- [ ] Resolve the doc contradiction: `specs/` historically noted external schema governance in places while active EF migrations live in `Wallet.Infrastructure/Persistence/Migrations/`.
- [ ] Define and test backup/restore.

### [~] 7. Configuration hygiene
- [ ] Default `ASPNETCORE_ENVIRONMENT` to `Production` in compose (currently `${ASPNETCORE_ENVIRONMENT:-Development}`, which enables Swagger and dev seeding).
- [ ] Set `AllowedHosts`; decide on HTTPS/HSTS and forwarded-headers handling.
- [x] Validate Stripe options at startup (`StripeSettings.EnsureValid` in `Program.cs`, replacing removed VNPay gateway config).
- [ ] Use a secret manager instead of `.env` files.

### [~] 8. Abuse controls & idempotency
- [ ] Rate limiting on top-ups, checkout, transfers, withdrawals, and webhooks.
- [~] Pending Stripe row lifecycle: direct-checkout `resume`/`cancel`/`expire`, admin `ReconciliationRequired` resolution, and 1-minute `StripeTopupReconciliationWorker` for `Topup` + `DirectOrder` implemented; `CreationPending` rows without session ID and `CancelDirectPayment` paid-race auto-refund remain open.
- [~] Allowlist Stripe return URLs (`StripeSettings.RequireReturnUrl` enforces HTTPS, or `http://localhost:3000` in Development, for `Stripe:SuccessUrl` / `Stripe:CancelUrl`).
- [~] `Idempotency-Key` support: implemented on `RequestWithdrawal`, `TopupStripe`, Auction `holds/*`, and `AdjustOrderPayment`; still **missing** on `POST /api/v1/wallets/transfers`.

### [ ] 9. Container and CI/CD
- [ ] Dockerfile: non-root user, `HEALTHCHECK`, pinned base image digests (currently unpinned `aspnet:8.0` / `sdk:8.0` running as root).
- [ ] `.github/workflows/ci-cd.yml`: run `dotnet build -c Release`, `dotnet test -c Release`, `scripts/verify-wallet-architecture.sh`, `dotnet list package --vulnerable`, image scan (Trivy), and full-history gitleaks **before** pushing images.
- [ ] Add a deploy step / environment protection.

---

## P2 — Operability

### [ ] 10. Health checks
- [ ] `/health/live` (process) and `/health/ready` (DB connectivity, required config). `/health` currently returns `Healthy` unconditionally (`Program.cs`).

### [ ] 11. Observability
- [ ] Add OpenTelemetry metrics (business counters for transactions/top-ups/direct payments/notification failures, ASP.NET + EF metrics) — `ObservabilityExtensions.cs` registers tracing only.
- [ ] Structured logging with correlation ID; audit/verify no PII or bank secrets leak in logs.
- [ ] Alerts: pending-too-long Stripe attempts, webhook signature failures, `NotificationDispatchFailed` rate, reconciliation drift, and 5xx rate.

### [ ] 12. Error handling
- [ ] `GlobalExceptionHandlerMiddleware` returns `ArgumentException` messages to clients (`400 VAL_INVALID_ARGUMENT`) — replace with explicit domain/validation exceptions where unsafe.
- [ ] Map `DbUpdateConcurrencyException` and PostgreSQL lock timeouts to `409`/`503` (currently fall through to `500`).
- [x] Legacy VNPay IPN controller action and bare catch removed when VNPay endpoints were retired.
- [ ] Set EF command timeout and lock timeout; revisit retry/outbox strategy (including post-commit `NotificationClient` retries).

### [~] 13. Money and ledger integrity
- [~] Snapshot reconciliation query implemented (`GET /api/v1/admin/wallets/reconciliation`), plus Stripe `ReconciliationRequired` queue and worker; full double-entry verification job remains open.
- [ ] Operational schedule for Stripe payout/settlement reconciliation (VNPay replaced by Stripe).
- [ ] Decide whether `ListTransactionsQueryHandler` should stop accepting numeric enum strings (`"1"`) via `Enum.TryParse`.

### [ ] 14. Platform lifecycle (OVERDUE P0)
- [ ] **.NET 8.0 support ended 2026-11-10** (9 days past EOL as of 2026-11-19) — migrate to **.NET 10 LTS** immediately.
- [ ] Bump EF Core (`8.0.10`), Npgsql (`8.0.10`), OpenTelemetry (`1.18.0`), Swashbuckle (`6.6.2`), and MediatR (`12.4.1` — check MediatR 13+ licensing first).

---

## P3 — Quality and documentation

### [~] 15. Tests
- [~] Unit & in-memory suite expanded (`455` passed), plus `36` PostgreSQL integration tests implemented; however those `36` PostgreSQL tests are **skipped** without `WALLET_STRIPE_TEST_DATABASE`.
- [ ] `WebApplicationFactory` end-to-end tests for header auth, `[RequireInternalServiceKey]`, middleware, and JSON contracts, plus live PostgreSQL run of all `36` integration tests.
- [ ] Load test on hot paths.

### [~] 16. Tooling and docs
- [x] `scripts/verify-wallet-architecture.sh` fixed and updated (`46` handlers, `2` controllers, `0` internal controllers).
- [ ] Replace the stale `Wallet.Api/Wallet.Api.http` (`weatherforecast` template) with real requests.
- [x] Updated `README.md`, `PRODUCTION_READINESS.md`, `docs/operations/production-readiness.md`, and `docs/api/wallet-payments.md` to current `46`-handler / `491`-test state (`2026-11-19`); remaining legacy sub-files under `specs/` should be cross-checked if touched.
- [ ] Write an operations runbook: deploy, rollback, secret rotation, `ReleaseFeatures` kill-switches, `CreationPending` Stripe recovery, and `NotificationDispatchFailed` replay.
- [ ] Document clearly in deployment manifests that all config must come from the environment (`.dockerignore` excludes `appsettings*.json`).

---

## Notes

- Repo rules require a Plan (objective, files, GitNexus impact analysis, steps, verification) before each change, and `./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release`, `dotnet test Wallet.sln -c Release` before commit.
- The deployed container on port 8008 (image built 2026-11-05) does not match this repo's current source; redeploy before any live verification.
