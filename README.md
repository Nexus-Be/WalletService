# Wallet Service

WalletService is a .NET 8 REST/JSON service for wallet balances, payments,
transfers, withdrawals and auction deposits. It uses PostgreSQL, EF Core and
classic CQRS with MediatR; there are two public controllers and 26 handlers.
The API normally listens on port `8008`; local PostgreSQL uses port `5440`.

## Branch provenance

This branch was created from the `dev` worktree at commit
`81f7d11ef0e43533511a266b1e56e25324c934a5` (`modified handover documents`).
It also incorporates and adapts Stripe top-up recovery work from
`origin/feature/stripe-topup-resume-and-reconciliation` at
`de7abf0eefa630df13f086db0c31dcb7192dd1f2`, as well as the approved
uncommitted Phase 2 financial-operation work. The Stripe feature branch is
an input, not a parent commit of this branch.

## Implemented features

| Area | Behavior |
|---|---|
| Wallet and transfers | Owned balance and paginated transaction history; atomic transfers; available balance excludes reserved funds. |
| Stripe payments | Stripe Checkout top-up and direct order payment with signed webhooks and idempotent settlement. VNPay payment endpoints are no longer active; historical data remains. |
| Stripe top-up recovery | Owner-scoped, paginated history and status; resume the same open unpaid Checkout session; correlated return URLs and a background reconciliation worker. |
| Auction deposits | Reserve, inspect, release, capture and refund wallet-funded holds. Capture can distribute forfeitures to seller wallets and a platform ledger, or create order-specific deposit escrow. Operations are idempotent and transactional. |
| Withdrawals | Reserve available balance when requested; admin approval consumes the reservation with a manual transfer reference, and rejection releases it. No bank payout is initiated. |
| Administration | Balance adjustments, paginated wallet ledger and wallet-balance reconciliation reports. |

`heldBalance` and `lockedBalance` both include auction holds **and**
withdrawal reservations. `availableBalance = balance - lockedBalance`.
Auction holds do not expire automatically.

### Boundaries and outstanding integration

- Deposit escrow is separate from Commerce settlement and refund flows; it is
  not automatically distributed when an order completes. A separate Commerce
  contract is required. Auction retries hold operations; Wallet sends no
  callback.
- Platform forfeiture credits are recorded in the auction transfer ledger;
  no platform balance or payout API is provided.
- Live Stripe test-mode, webhook forwarding and gateway end-to-end acceptance
  have not been verified. A Stripe attempt with an uncertain session creation
  and no stored session ID requires operational reconciliation.
- User identity and roles come from claims or trusted gateway-forwarded
  headers; the host does not validate JWTs itself. Protect the service network
  and ensure the gateway overwrites client-supplied identity headers. Auction
  service routes separately require `X-Internal-Service-Key`.
- The service is not approved for production deployment. See
  [production readiness](docs/operations/production-readiness.md).

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
dotnet user-secrets set "InternalSecurity:AuctionServiceKey" "<AUCTION_SERVICE_KEY>" --project Wallet.Api
dotnet run --project Wallet.Api
```

Configure `Stripe:SuccessUrl` and `Stripe:CancelUrl` for the frontend; use
HTTPS outside development. Never commit credentials. Swagger is available at
`/swagger` in Development (or when enabled), and `/health` is a liveness
response, not a database readiness check. The host attempts migrations on
startup; review and back up shared databases before applying schema changes.

## Verification

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

PostgreSQL integration tests require `WALLET_STRIPE_TEST_DATABASE` to point
to an existing **disposable** database; without it those tests are skipped. The latest
isolated PostgreSQL run passed 419 tests with no skips. The architecture
check enforces 26 handlers (17 commands, 9 queries) and two controllers.

## Documentation

- [API index](docs/api/README.md) and [wallet payments](docs/api/wallet-payments.md)
- [Auction deposit contract](docs/api/holds/auction-deposits.md)
- [Stripe top-up recovery](docs/api/stripe/topup-recovery.md) and
  [Stripe payment contracts](docs/api/stripe/payments.md)
- [Feature specification and plan](specs/004-auction-holds-stripe/spec.md)
- [Persistence model](specs/04_DATA_PERSISTENCE.md)

The repository is organized into `Wallet.Domain`, `Wallet.Application`,
`Wallet.Infrastructure`, `Wallet.Api`, `Wallet.Tests`, `docs`, `specs`, and
`scripts`. API, provider and gateway integration details are documented within
this repository.
