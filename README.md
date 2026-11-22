# Wallet Service

WalletService is a .NET 8 REST/JSON service (see [production readiness](docs/operations/production-readiness.md) regarding .NET 8 EOL on **2026-11-10**) for wallet balances, payments, transfers, withdrawals, seller bank accounts, and auction deposits. It uses PostgreSQL, EF Core, and classic CQRS with MediatR; there are **two public controllers** (`WalletsController` and `AdminWalletsController`, with internal `~/api/v1/internal/wallets/...` routes hosted on `WalletsController`) and **68 handlers** (`40` commands, `28` queries).
The API normally listens on port `8008`; local PostgreSQL uses port `5440`.

## Branch provenance

`feature/handover-wallet-hardening` was branched from `dev` (`5dde562`) on top of the `wallet-release-readiness` baseline (`origin/dev` `4ce76aa`, combining Auction holds and Stripe recovery) to implement `WalletService.md` (Phases 0–3 hardening, lifecycle, bank accounts, freeze guards, and admin tooling) and `WALLET_NOTIFICATION_HANDOFF.md` (`INotificationClient` post-commit REST dispatch and `[RequireInternalServiceKey]` enforcement).

## Implemented features

| Area | Behavior |
|---|---|
| Wallet and transfers | Owned balance (`availableBalance = balance - lockedBalance`), wallet freeze/unfreeze (`WALLET_FROZEN` guards), paginated transaction history, single-transaction detail lookup, and atomic transfers. |
| Stripe payments | Stripe Checkout top-up and direct order payment with signed webhooks, startup credential/URL validation (`StripeSettings.EnsureValid`), idempotent settlement, `AvailableBalance` enforcement on wallet order pay, and internal `AdjustOrderPayment` escrow deltas. VNPay payment endpoints are retired. |
| Stripe recovery & lifecycle | Owner-scoped paginated top-up history (optional `status`) and status; `resume` unpaid top-up or direct Checkout sessions; buyer `cancel` and internal `expire` for unpaid direct payments; 1-minute `StripeTopupReconciliationWorker` reconciling both `Topup` and `DirectOrder`; admin `ReconciliationRequired` list and manual resolve endpoints. |
| Auction deposits | Reserve, inspect, release, capture, and refund wallet-funded holds behind `[RequireAuctionServiceKey]`; supports `BUYER_WITHDRAWN`, live status on replay, and post-commit `auction-deposits` notifications. |
| Withdrawals & bank accounts | Saved seller `BankAccounts` CRUD/default selection, idempotent `RequestWithdrawal` (`Idempotency-Key` + SHA-256 fingerprint), paginated history/detail, seller cancellation, 6-step admin/staff workflow (`Approve`, `Reject`, `StartProcessing`, `Complete` with unique `TransferReference`, `Fail`, `admin`-only `ResolveFailed`), and post-commit `withdrawal-events` notifications. No external bank payout is initiated. |
| Administration & internal S2S | `admin`-only balance adjustments, ledger, snapshot reconciliation, and Stripe attempt resolution; admin/staff wallet listing/detail, freeze/unfreeze, withdrawal history, and hold inspection; `~/api/v1/internal/wallets/...` routes guarded by `[RequireInternalServiceKey]`. |

`heldBalance` and `lockedBalance` both include auction holds **and**
withdrawal reservations. `availableBalance = balance - lockedBalance`, enforced
in PostgreSQL by `CK_Wallets_BalanceLocks`. Auction holds do not expire
automatically.

### Boundaries and outstanding integration

- **Not approved for production or live-money deployment.** Suitable for development and controlled staging/integration testing only. See [production readiness](docs/operations/production-readiness.md) and [root checklist](PRODUCTION_READINESS.md).
- `.NET 8.0` LTS reached end-of-support on **2026-11-10**; upgrade to `.NET 10 LTS` is required before production.
- Deposit escrow is separate from Commerce settlement and refund flows, and `sub-orders/{subOrderId}/refund` (`IsSubOrder = true`) retains compatibility without original `PurchaseDebit` validation. CommerceService cutover onto `~/api/v1/internal/wallets/*` and funding-backed settlement contracts remain open.
- New withdrawals, auction `ORDER_PAYMENT` capture, and legacy Commerce settlement/refunds are disabled by default outside Development via `ReleaseFeatures:*Enabled`.
- Outbound `NotificationClient` (`NOTIFICATION_SERVICE_URL`) dispatches post-commit REST events without a durable outbox or automatic retry queue (`NotificationDispatchFailed` audit logs record failures); NotificationService end-to-end staging delivery remains to be verified.
- Stripe accepts test-mode keys (`sk_test_`) and `livemode == false` webhooks only; `POST /api/v1/wallets/transfers` lacks `Idempotency-Key` retry protection; `CreationPending` Stripe attempts without a stored session ID cannot be resolved via `ResolveStripeAttempt`.
- User identity and roles come from claims or trusted gateway-forwarded headers (`UserContextMiddleware`); the host does not validate JWTs. Protect port `8008` behind the gateway, block `/api/v1/internal/*` at public ingress, and pass `X-Internal-Service-Key` for Auction and internal S2S endpoints.

## Local setup

Requirements: .NET 8 SDK, Docker and a PostgreSQL connection string. Docker
Compose uses the external `nexus-network`.

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

Configure `Stripe:SuccessUrl` and `Stripe:CancelUrl` for the frontend; use
HTTPS outside development. Never commit credentials. Swagger is available at
`/swagger` in Development (or when enabled), and `/health` is an unconditional
liveness response, not a database readiness check. The host attempts migrations
on startup (and logs warnings on failure); apply and verify migration
`20261119020448_HandoverWalletHardening` carefully on shared databases.

## Verification

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

PostgreSQL integration tests (`36` tests) require `WALLET_STRIPE_TEST_DATABASE`
to point to an existing **disposable** database; without it those tests are
skipped. Without `WALLET_STRIPE_TEST_DATABASE`, the latest unit/in-memory run
passed **455** tests with **0** failures and **36** skipped (`491` total). The
architecture check enforces **68 handlers** (`40` commands, `28` queries) and
**two controllers**.

## Documentation

- [API index](docs/api/README.md) and [wallet payments](docs/api/wallet-payments.md)
- [Cross-service handover response](docs/integrations/handover-response.md)
- [Auction deposit contract](docs/api/holds/auction-deposits.md)
- [Auction integration and reconciliation request](docs/api/holds/auction-integration-request.md)
- [Stripe top-up recovery](docs/api/stripe/topup-recovery.md) and
  [Stripe payment contracts](docs/api/stripe/payments.md)
- [Production readiness checklist](docs/operations/production-readiness.md) and
  [legacy/branch backlog](PRODUCTION_READINESS.md)
- [Feature specification and plan](specs/004-auction-holds-stripe/spec.md)
- [Persistence model](specs/04_DATA_PERSISTENCE.md)

The repository is organized into `Wallet.Domain`, `Wallet.Application`,
`Wallet.Infrastructure`, `Wallet.Api`, `Wallet.Tests`, `docs`, `specs`, and
`scripts`. API, provider and gateway integration details are documented within
this repository.
