# Wallet Service

WalletService is a .NET 8 REST/JSON service (see [production readiness](docs/operations/production-readiness.md) regarding `.NET 8.0` EOL on **2026-11-10**) for wallet balances, order and auction escrows, settlements, subsidies, post-payout refunds, P2P transfers, Stripe top-ups/direct checkouts, bank withdrawals, seller bank accounts, shadow-ledger accounting, and cross-service reconciliation. It uses PostgreSQL, EF Core, and classic CQRS with MediatR (100% RESTful HTTP; no gRPC, RabbitMQ, or MassTransit): **2 public controllers** (`WalletsController` and `AdminWalletsController`, with internal `~/api/v1/internal/...` routes hosted on `WalletsController`), **71 handlers** (**42** commands, **29** queries), and **3 hosted background workers** (`StripeTopupReconciliationWorker`, `WithdrawalNotificationRetryWorker`, and `JournalPostingWorker`).

The API listens on port `8008`; local PostgreSQL uses port `5440` (`wallet_db`).

## Implemented features

| Area | Behavior |
|---|---|
| Wallet, history & transfers | Owner `GET /me` auto-creates an `ACTIVE` VND wallet (`AutoCreate = false` on admin/internal lookups -> `404 WALLET_NOT_FOUND`), `availableBalance = balance - lockedBalance` (`CK_Wallets_BalanceLocks`), freeze/unfreeze guards (system `RefundFundAccount` protected by `403 SYSTEM_WALLET_PROTECTED`), paginated transaction history + gateway detail enrichment, and deadlock-free ascending-`UserId` P2P transfers with optional `Idempotency-Key` SHA-256 verification. |
| Stripe top-ups & direct checkout | Two-phase DB-first Stripe Checkout top-ups (`10,000`–`99,999,999` VND) and direct order checkouts (`1`–`99,999,999` VND, mutually exclusive with wallet `PurchaseDebit`), signed webhooks (`Stripe-Signature`) + live Stripe verification, session `resume`, buyer `cancel`, internal `expire`, 1-minute `StripeTopupReconciliationWorker` (covering both `Topup` and `DirectOrder` attempts and promoting `>10m` `CreationPending` rows to `ReconciliationRequired`), and admin manual attempt resolution. Active VNPAY payment endpoints are retired. |
| Order escrow, Settlement V2, subsidies & refunds | Wallet-funded `OrderEscrow` (`PayOrderWithWallet`, `+`/`-` `AdjustOrderPayment`, untouched `CancelOrderEscrow`, and historical `OrderEscrowAccounting` reconstruction), `AuctionDepositEscrow` FIFO pool, four-eyes `PlatformSubsidyBudget` & `PlatformSubsidyReservation`, atomic Settlement V2 `SettleOrderPayout` (`OrderPayout` / `AuctionPayout` with two-step shadow-ledger saves and `>= 10 µs` timestamps), legacy V1 `SettleSellerOrder`, and pre-payout `RefundOrderPayment` (mandatory `ParentOrderId` and single-refund-per-sub-order enforcement, gated by `ReleaseFeatures:OrderRefundEnabled` OFF by default everywhere). |
| Post-payout Refund Fund (T30 Phase 1) | Protected system wallet (`RefundFundAccount`), admin funding/status (`DepositRefundFund`, `GetRefundFundStatus`), and internal `PayRefundFundCase` (`RefundFund:Enabled` OFF by default everywhere) with dual-admin `SecondApprovalThreshold`, Vietnam UTC+7 `PerCaseCap`/`DailyCap` soft stops (`NeedsFinance`), ascending wallet locks, frozen-buyer support, and best-effort seller `AvailableBalance` recovery (`SellerRecoveryDebit` / `RefundFundRecovery`). |
| Auction deposits | Reserve, inspect, release, capture (`FORFEIT` ungated vs `ORDER_PAYMENT` gated by `AuctionOrderCaptureEnabled` creating `AuctionDepositEscrow`), and `RefundCapturedHold` (shared lock order prevents double-spend against `AuctionPayout`), authenticated via `[RequireAuctionServiceKey]` with post-commit `auction-deposits` notifications. Auction holds never expire automatically. |
| Withdrawals & bank accounts | Saved seller `BankAccounts` CRUD/default (`[RequireRole("seller")]`, `staff` denied, partial unique default index `IX_BankAccounts_UserId_Default`), idempotent `RequestWithdrawal` (`WithdrawalsEnabled` gate), seller cancellation, 6-step admin/staff workflow (`Approve`, `Reject`, `StartProcessing`, `Complete` with unique `TransferReference`, `Fail`, `admin`-only `ResolveFailed`), and `withdrawal-events` notifications backed by the 30-second `WithdrawalNotificationRetryWorker` (up to `8` attempts with exponential backoff up to `60m`). WalletService does not initiate external bank payouts. |
| Shadow ledger & reconciliation (`I1`–`I12`) | Real-time `WalletDbContext.SaveChangesAsync` chokepoint (`FOR SHARE` on `LedgerSettings`, rejecting unbacked balance edits when enabled), 2-second `JournalPostingWorker` writing balanced `WALLET` and counter-account `JournalLines`, historical backfill, trial balance, and snapshot cross-service `ReconciliationEvaluator` (`RepeatableRead` + `30s` settle buffer across Wallet, Commerce, and Auction) with deduplicated finding triage. |
| Administration & internal S2S | Strict role separation in `AdminWalletsController` (`admin`/`staff` on 9 operational actions; `admin`/`administrator`-only via `EnsureAdmin` on 23 financial, recovery, subsidy, refund-fund, and ledger actions), plus `~/api/v1/internal/wallets/...` and `~/api/v1/internal/audit-logs/*` protected by `[RequireInternalServiceKey]`. |

`heldBalance` and `lockedBalance` both represent total reserved balance across active `Held` auction holds and `Pending`/`Approved`/`Processing` withdrawal requests.

### Boundaries and deployment caveats

- **Not approved for production or live-money deployment.** Suitable for development and controlled staging/integration testing only. See [production readiness](docs/operations/production-readiness.md) and [root checklist](PRODUCTION_READINESS.md).
- `.NET 8.0` LTS reached end-of-support on **2026-11-10**; upgrade to `.NET 10 LTS` before any production rollout.
- Stripe integration accepts test-mode credentials (`sk_test_`) and non-live webhooks (`livemode == false`) only.
- **Release & runtime gates:** `WithdrawalsEnabled`, `AuctionOrderCaptureEnabled` (`ORDER_PAYMENT`), `LegacyCommerceCreditsEnabled`, `LegacyCommerceSettlementEnabled`, and `OrderSettlementV2Enabled` default open in `Development` and disabled elsewhere unless explicitly enabled. `ReleaseFeatures:OrderRefundEnabled`, `RefundFund:Enabled`, and the database toggle `LedgerSettings.Enabled` (`PUT /api/v1/admin/wallets/ledger/shadow`) start **disabled in all environments**.
- **Notification retry scope:** `WithdrawalNotificationRetryWorker` automatically retries `withdrawal-events` (`NotificationQueued` -> `NotificationDelivered` / `NotificationAbandoned`); `auction-deposits` events dispatch post-commit best-effort (`NotificationDispatchFailed` audit log on error) without a dedicated retry worker.
- **Identity & network boundary:** User identity and roles come from claims or trusted gateway-forwarded headers (`UserContextMiddleware`, no local JWT validation). Keep port `8008` behind an authenticating gateway that strips caller identity headers, blocks `/api/v1/internal/*` from public ingress, and supplies `X-Internal-Service-Key` for Auction and internal S2S routes.

## Local setup

Requirements: `.NET 8 SDK`, Docker, and a PostgreSQL connection string (`docker-compose.db.yml` attaches to the external `nexus-network` on port `5440`).

```bash
docker network inspect nexus-network >/dev/null 2>&1 || docker network create nexus-network
docker compose -f docker-compose.db.yml up -d postgres-wallet

dotnet user-secrets init --project Wallet.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<POSTGRES_CONNECTION_STRING>" --project Wallet.Api
dotnet user-secrets set "Stripe:SecretKey" "<STRIPE_TEST_SECRET_KEY>" --project Wallet.Api
dotnet user-secrets set "Stripe:WebhookSecret" "<STRIPE_WEBHOOK_SECRET>" --project Wallet.Api
dotnet user-secrets set "InternalSecurity:ServiceKey" "<INTERNAL_SERVICE_SECRET>" --project Wallet.Api
dotnet user-secrets set "InternalSecurity:AuctionServiceKey" "<AUCTION_SERVICE_KEY>" --project Wallet.Api
dotnet user-secrets set "NOTIFICATION_SERVICE_URL" "http://localhost:8007" --project Wallet.Api
dotnet user-secrets set "Services:Notification:ServiceKey" "<NOTIFICATION_SERVICE_KEY>" --project Wallet.Api
dotnet run --project Wallet.Api
```

Optional integration/feature settings: `Stripe:SuccessUrl` / `Stripe:CancelUrl` (HTTPS required outside `Development`), `Reconciliation:CommerceBaseUrl`, `Reconciliation:AuctionBaseUrl`, `RefundFund:Enabled`, and `ReleaseFeatures:*Enabled`. Never commit credentials.

Swagger is exposed at `/swagger` in `Development`. `GET /health` checks PostgreSQL connectivity via `CanConnectAsync()` and returns `200 Healthy` or `503 Unhealthy`. On startup, `Wallet.Api` attempts `db.Database.Migrate()` across the `14` EF Core migrations under `Wallet.Infrastructure/Persistence/Migrations/` (latest: `20261125130000_AddRefundFundPayouts`) and logs a warning if deferred; apply and verify migrations explicitly on shared databases.

## Verification

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

- `./scripts/verify-wallet-architecture.sh` enforces the frozen CQRS boundary: **71 handlers** (`42` commands, `29` queries) and **2 controllers** (`WalletsController`, `AdminWalletsController`) wired through MediatR.
- PostgreSQL integration tests require `WALLET_STRIPE_TEST_DATABASE` pointing to an existing **disposable** PostgreSQL database (without it, PostgreSQL-backed integration fixtures are skipped while unit/in-memory tests still run).

## Documentation

- [Docs index](docs/README.md) and [business use cases & flows (`00`–`09`)](docs/business-use-cases.md)
- [Escrow & order money flows deep dive](docs/business-flows/04-wallet-orders.md) and [sequence diagrams 4A & 4B](docs/business-flows/04-wallet-orders.diagram.md)
- [Shadow ledger & reconciliation (`I1`–`I12`)](docs/business-flows/09-shadow-ledger-and-reconciliation.md) and [sequence diagram](docs/business-flows/09-shadow-ledger-and-reconciliation.diagram.md)
- [Reconciliation invariant catalog](docs/RECONCILIATION_CATALOG.md)
- [API index](docs/api/README.md), [Settlement V2 & subsidies](docs/api/checkout/order-settlement-v2.md), and [cancel order escrow](docs/api/checkout/cancel-order-escrow.md)
- [Auction deposit contract](docs/api/holds/auction-deposits.md), [Stripe payments](docs/api/stripe/payments.md), and [Stripe top-up recovery](docs/api/stripe/topup-recovery.md)
- [Persistence model](specs/04_DATA_PERSISTENCE.md), [production readiness checklist](docs/operations/production-readiness.md), and [legacy/branch backlog](PRODUCTION_READINESS.md)

The repository is organized into `Wallet.Domain`, `Wallet.Application`, `Wallet.Infrastructure`, `Wallet.Api`, `Wallet.Tests`, `docs`, `specs`, and `scripts`. All references remain strictly internal to this repository.
