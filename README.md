# WalletService

WalletService is the wallet bounded context in the Nexus microservices
application. It owns wallet accounts, balances, ledger transactions, top-ups,
withdrawal requests, and wallet-backed checkout/refund processing. It exposes
an ASP.NET Core gRPC API and HTTP endpoints, and integrates with other services
through RabbitMQ events.

> **Branch summary (`dev-func`):** This branch adds wallet workflows beyond the
> original gRPC surface: VNPay top-ups and signed IPN processing, seller
> withdrawal requests with approval/rejection, administrative balance
> adjustments, direct-payment initiation, checkout debits and order refunds via
> events, transaction-history filtering, PostgreSQL locking/outbox support,
> automated wallet tests, and CI/Docker build configuration. It also updates
> the wallet API contract/specifications and repository contributor guidance.

For frontend and service integration details, including exact routes, payloads,
roles, response shapes, event contracts, and known limitations, see
[`.docs/WALLET_IMPLEMENTATION_REPORT.md`](.docs/WALLET_IMPLEMENTATION_REPORT.md).

## Architecture

The solution follows a four-project Clean Architecture layout:

| Project | Responsibility |
|---|---|
| `Wallet.Domain` | Wallet entities, transaction types, domain events, and business exceptions |
| `Wallet.Application` | MediatR commands/queries, validation, DTOs, and MassTransit consumers |
| `Wallet.Infrastructure` | EF Core/PostgreSQL persistence, payment-provider integrations, and RabbitMQ configuration |
| `Wallet.Api` | ASP.NET Core host, gRPC service, HTTP wallet endpoints, and API wiring |
| `Wallet.Tests` | Domain behavior and command-validation tests |

`Wallet.Api/Contracts/shop.proto` defines the shared protobuf API. The
application layer depends on the domain; infrastructure implements application
interfaces. PostgreSQL is the source of truth for balances and the transaction
ledger. Balance-changing operations use wallet-domain methods and database
transactions; concurrent balance operations lock rows. Events are published
using MassTransit with an EF Core transactional outbox.

## Main capabilities

- Provision an active VND wallet after a user-registration event.
- Read wallet balances and paginated transaction history.
- Initiate VNPay top-ups and credit balances only after validating the signed
  server-to-server IPN.
- Transfer funds between wallets through the gRPC API.
- Debit wallet balances during checkout and refund paid orders through
  RabbitMQ events.
- Initiate configured direct-gateway payments.
- Reserve seller funds for withdrawal and let authorized staff approve or
  reject requests.
- Perform audited administrative balance adjustments.

## Runtime and local development

The projects target **.NET 8**. Local defaults used by the repository are:

| Component | Address / value |
|---|---|
| Wallet HTTP and gRPC host | `http://localhost:8008` |
| PostgreSQL | `localhost:5439`, database `wallet_db` |
| RabbitMQ | `localhost:5672`, virtual host `/` |
| API gateway in Docker Compose | `http://localhost:8080` |

### Prerequisites

- .NET 8 SDK and runtime
- Docker with Docker Compose (for the local database and service stack)
- RabbitMQ reachable by WalletService for event-driven workflows
- VNPay sandbox/production credentials for top-up workflows
- Optional HTTPS payment-gateway configuration for direct-payment initiation

### Configure

Copy `.env.example` to `.env` for local environment-variable values, then
provide a PostgreSQL connection string and RabbitMQ settings. Payment-provider
settings are documented in
[`Wallet.Api/appsettings.example.json`](Wallet.Api/appsettings.example.json):

- `VnPay:PaymentUrl`, `TmnCode`, `HashSecret`, `ReturnUrl`, and `Version`
- `PaymentGateways:{gateway}:PaymentUrl` and `SigningSecret`

Use local sandbox/test credentials for development. Supply secrets via a local
user-secrets store, environment variables, or a deployment secrets manager; do
not commit real credentials. The example and Compose credentials are for local
development only.

### Run the service

Start the local database and WalletService:

```sh
docker compose up --build
```

The Compose file starts PostgreSQL and WalletService and exposes the wallet
service on port `8008`. It expects a RabbitMQ broker to be reachable at
`host.docker.internal:5672`; start/configure a broker separately if you need
event consumers. Configure VNPay/direct-payment settings in the service
environment before exercising those workflows.

To run the API from the command line instead:

```sh
dotnet restore Wallet.sln
dotnet run --project Wallet.Api/Wallet.Api.csproj
```

Swagger is enabled only in the Development environment. The service uses
`ASPNETCORE_URLS` to choose its listening address.

### Build and test

```sh
dotnet build Wallet.sln
dotnet test Wallet.Tests/Wallet.Tests.csproj
```

The tests cover wallet balance operations and command validation. The repository
does not currently include an EF Core migration covering all wallet workflow
schema changes; verify/provision the required tables, indexes, and MassTransit
outbox/inbox tables before connecting to a fresh or production database.

## API overview

### REST

HTTP routes are implemented in `Wallet.Api/Services/WalletHttpEndpoints.cs`.
The primary frontend routes are:

| Method | Route | Use |
|---|---|---|
| `GET` | `/api/v1/wallets/me` | Read the caller's wallet, including locked and available balances |
| `GET` | `/api/v1/wallets/transactions` | List caller's transactions with type/date filters and pagination |
| `POST` | `/api/v1/wallets/topups/vnpay` | Create a pending VNPay top-up and obtain its payment URL |
| `POST` | `/api/v1/wallets/checkout/direct` | Create a pending direct-provider payment |
| `POST` | `/api/v1/wallets/withdrawals` | Seller requests withdrawal |
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` | Staff approves or rejects a withdrawal |
| `POST` | `/api/v1/admin/wallets/{walletId}/adjustments` | Admin makes an audited credit/debit adjustment |
| `POST` | `/api/v1/wallets/vnpay/ipn` | VNPay server-to-server callback |

Protected HTTP and gRPC requests rely on gateway-provided `x-user-id` and
`x-user-role` identity headers/metadata. The gateway must authenticate the
caller and strip or overwrite any client-supplied identity headers before
forwarding requests. The VNPay IPN is the provider callback and is validated
using its signature; it is not a browser return route.

### gRPC

The `WalletApi` gRPC service is defined in `Wallet.Api/Contracts/shop.proto`.
It provides wallet reads, deposit initiation, transfer, and transaction
listing. The legacy `Withdraw` RPC returns `UNIMPLEMENTED`; use the REST
withdrawal request and approval workflow instead. Money values in the protobuf
are decimal strings.

## Event-driven integrations

Wallet.Api consumes user-registration, checkout, stock-reservation, and order
cancellation events. It publishes wallet top-up, debit/payment, refund,
withdrawal, and direct-payment lifecycle events. Shared event interfaces and
payload properties are in `Wallet.Domain/Events/WalletEvents.cs`; detailed
producer/consumer behavior is documented in the
[integration guide](.docs/WALLET_IMPLEMENTATION_REPORT.md#events-for-service-integrations).

Identifiers in event payloads are UUID strings; monetary values are decimal
strings and should be formatted/parsed invariantly. Checkout debit idempotency
is keyed by order ID. Services should react to the matching success/failure
contract for the checkout flow they publish; the current legacy stock-reserved
flow and newer wallet-debit flow use distinct result event families.

## Important limitations

- The direct-gateway route creates a pending transaction and payment URL, but
  this branch does not implement that provider's settlement callback/consumer.
  Initiation is not proof of payment.
- VNPay top-ups credit the wallet only after a valid, amount-matched IPN
  succeeds. A browser redirect is not proof of settlement.
- The legacy gRPC `Withdraw` is not implemented. Withdrawal requests are
  seller-only REST operations followed by staff approval/rejection.
- The REST withdrawal request property is currently named `bankCode`, though
  the value is stored as the bank name. The configured withdrawal fee is zero.
- Top-up and withdrawal initiation requests do not accept idempotency keys;
  avoid blindly retrying after a network timeout.
- The API trusts identity headers supplied by the gateway and does not
  independently validate a frontend bearer token.
- The repository does not include a migration for all newly added workflow
  tables and outbox/inbox schema.

## Documentation and contract references

- [Wallet integration guide](.docs/WALLET_IMPLEMENTATION_REPORT.md) — current
  REST/gRPC usage, security headers, error formats, provider setup, event
  contracts, and integration gaps.
- [`specs/`](specs/README.md) — architecture, API contracts, messaging,
  persistence, and verification specifications.
- [`Wallet.Api/Contracts/shop.proto`](Wallet.Api/Contracts/shop.proto) — shared
  protobuf contract. Contract changes should be additive and coordinated with
  the gateway.
