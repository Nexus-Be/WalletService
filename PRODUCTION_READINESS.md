# WalletService — Production Readiness Checklist

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
- Known and accepted: `HttpContextExtensions.cs` trusts these headers from any caller (verified: `x-user-roles: admin` can credit a wallet), and `POST /checkout/direct` has no `[RequireRole]`.
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

### [ ] 6. Database safety
- [ ] Remove hardcoded `Trust Server Certificate=true` (`Wallet.Infrastructure/DependencyInjection.cs`); make it configurable.
- [ ] Stop auto-migrating on app start (`Program.cs`): races across replicas and swallows errors. Run migrations as a separate deploy step; fail fast.
- [ ] Add DB constraints: `Balance >= 0`, `LockedBalance <= Balance`. (A unique `DirectPaymentOrders.OrderId` index already exists on `dev`.)
- [ ] Resolve the doc contradiction: specs say schema is managed outside the repo, yet migrations exist.
- [ ] Define and test backup/restore.

### [ ] 7. Configuration hygiene
- [ ] Default `ASPNETCORE_ENVIRONMENT` to `Production` in compose (currently `Development`, which enables Swagger).
- [ ] Set `AllowedHosts`; decide on HTTPS/HSTS and forwarded-headers handling.
- [ ] Validate options at startup (`ValidateOnStart`) for `VnPay` and `PaymentGateways`.
- [ ] Use a secret manager instead of `.env` files.

### [ ] 8. Abuse controls
- [ ] Rate limiting on topups, checkout, transfers, IPN.
- [ ] Cap/expire pending rows.
- [ ] Allowlist `ReturnUrl` hosts (any HTTPS URL is accepted today).
- [ ] Idempotency-Key support for transfers and topups (helper exists, unused).

### [ ] 9. Container and CI/CD
- [ ] Dockerfile: non-root user, `HEALTHCHECK`, pinned base image digests.
- [ ] `.github/workflows/ci-cd.yml`: run build, `dotnet test`, `scripts/verify-wallet-architecture.sh`, `dotnet list package --vulnerable`, image scan (Trivy), gitleaks **before** pushing `latest`.
- [ ] Add a deploy step / environment protection.

---

## P2 — Operability

### [ ] 10. Health checks
- [ ] `/health/live` (process) and `/health/ready` (DB connectivity, required config). `/health` is currently unconditional.

### [ ] 11. Observability
- [ ] Add metrics (business counters for transactions/topups by status, ASP.NET + EF metrics) — only traces exist today.
- [ ] Structured logging with correlation ID; ensure no PII/payment params are logged.
- [ ] Alerts: pending-too-long topups/direct payments, IPN signature failures, 5xx rate.

### [ ] 12. Error handling
- [ ] `GlobalExceptionHandlerMiddleware` returns `ArgumentException` messages to clients — use an explicit validation exception.
- [ ] Map `DbUpdateConcurrencyException` and lock timeouts to 409/503.
- [ ] Remove the bare `catch` in the IPN controller action.
- [ ] Set EF command timeout and lock timeout; revisit retry using `ExecutionStrategy` for transactional paths.

### [ ] 13. Money and ledger integrity
- [ ] Reconciliation job: sum of ledger entries equals wallet balance.
- [ ] Daily reconciliation against VNPay.
- [ ] Decide whether `ListTransactions` should accept numeric enum strings (`"1"`).

### [ ] 14. Platform lifecycle
- [ ] .NET 8 support ends **2026-11-10** — plan migration to .NET 10 LTS.
- [ ] Bump EF Core, OpenTelemetry, Swashbuckle, MediatR (check MediatR 13+ licensing first).

---

## P3 — Quality and documentation

### [ ] 15. Tests
- [ ] Postgres integration tests (Testcontainers): row locking, rollback, concurrent double-submit, migrations. The in-memory provider ignores transactions.
- [ ] `WebApplicationFactory` tests for auth, middleware, JSON contracts.
- [ ] Load test on hot paths.

### [ ] 16. Tooling and docs
- [x] `scripts/verify-wallet-architecture.sh` no longer fails on the missing `Controllers/Internal` folder (fixed on `dev`).
- [ ] Replace the stale `Wallet.Api/Wallet.Api.http` (`weatherforecast` template) with real requests.
- [ ] Update README/specs counts (they say 3 controllers and 34/53 tests; code has 2 controllers and 137 tests).
- [ ] Write a runbook: deploy, rollback, secret rotation, VNPay incident handling.
- [ ] Document that all config must come from the environment (`.dockerignore` excludes `appsettings*.json`).

---

## Notes

- Repo rules require a Plan (objective, files, GitNexus impact analysis, steps, verification) before each change, and `./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release`, `dotnet test Wallet.sln -c Release` before commit.
- The deployed container on port 8008 (image built 2026-11-05) does not match this repo's current source; redeploy before any live verification.
