# Wallet Service

> **Trạng thái cập nhật: 2026-11-02.** Service dùng REST/JSON trên .NET 8, CQRS với MediatR, EF Core và PostgreSQL. gRPC, MassTransit, consumer và outbox không còn trong implementation hiện tại. Bộ kiểm thử hiện có 61 test; chạy lại lệnh kiểm thử bên dưới để xác nhận trạng thái của checkout bạn đang review.

## Tổng quan

Wallet Service quản lý số dư và giao dịch ví, nạp tiền qua VNPay, chuyển tiền nội bộ, khởi tạo checkout qua payment gateway và các thao tác quản trị. Gateway, Commerce và các service khác không nằm trong repository này; tài liệu về tích hợp nằm trong [`specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md`](specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md).

| Thành phần | Chi tiết |
|---|---|
| Runtime | .NET 8 / ASP.NET Core |
| API | REST HTTP/JSON, mặc định `http://localhost:8008` |
| Database | PostgreSQL, database `wallet_db`; compose local ánh xạ port `5440` |
| Kiến trúc | Domain / Application / Infrastructure / API; 11 MediatR handlers và 2 controllers |
| Health | `GET /health` |
| API contract | [API đang triển khai](api/A_ACTIVE/09_WALLET_PAYMENTS_API.md) |

## Cấu trúc repository

```text
Wallet.Domain/          Entities, enums, business rules and domain exceptions
Wallet.Application/     CQRS handlers, request validation and interfaces
Wallet.Infrastructure/  EF Core, PostgreSQL, migrations and payment gateways
Wallet.Api/             REST controllers, middleware and service host
Wallet.Tests/           Unit and contract regression tests
api/A_ACTIVE/           API contracts verified against this service
api/B_NONE/             Ecosystem API proposals; not evidence of implementation
specs/                  Architecture, persistence and integration guidance
scripts/                Repository verification scripts
```

`WalletAccount` is the wallet entity. Each balance-changing operation must preserve non-negative available balance and persist its related transaction and audit record within the same database transaction where applicable.

## Local setup

Requirements: .NET 8 SDK, Docker (for local PostgreSQL), and a PostgreSQL connection string. Docker Compose uses the shared external network `nexus-network`; create or connect that network before starting the database.

```bash
# Run from the repository root.
docker network inspect nexus-network >/dev/null 2>&1 || docker network create nexus-network
docker compose -f docker-compose.db.yml up -d postgres-wallet

# Configure secrets outside source control. For example:
dotnet user-secrets init --project Wallet.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<POSTGRES_CONNECTION_STRING>" --project Wallet.Api
dotnet user-secrets set "VnPay:TmnCode" "<VNPAY_TMN_CODE>" --project Wallet.Api
dotnet user-secrets set "VnPay:HashSecret" "<VNPAY_HASH_SECRET>" --project Wallet.Api

dotnet run --project Wallet.Api
```

The API listens on port `8008` in the checked-in launch profile. Swagger is available at `/swagger` in Development (or when `Swagger:Enabled` is set); the health endpoint is `/health`. The application applies EF Core migrations at startup when the configured database is relational. Review the migration and back up any existing database before deploying to a shared environment.

For the container deployment, provide `WALLET_DB_CONNECTION`, `VNPAY_TMN_CODE`, `VNPAY_HASH_SECRET` and other required settings through the deployment's secret manager, then run `docker compose up -d --build`. Do not use sample/default credentials in a shared environment or commit real credentials.

## Verify before review

Run from the repository root:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

The architecture script checks that MediatR remains wired, all 11 handlers remain under `Wallet.Application/Features`, and the two expected controllers remain in place.

## Implemented behavior and limitations

| Capability | Current behavior |
|---|---|
| Wallet and transaction history | Read own wallet and paginated transaction history |
| VNPay top-up | Creates a pending top-up; balance is credited only after a valid successful IPN |
| Internal transfer | Debits and credits the two wallets in one database transaction |
| Direct checkout | Creates a pending external-gateway payment and returns its payment URL; it does not debit wallet balance |
| User withdrawal | The route exists, but its handler currently rejects requests; it does not create a withdrawal request or pay out to a bank |
| Admin withdrawal | Can approve/reject an existing pending request; this repository currently has no working user flow to create one |
| Admin balance adjustment | Credits/debits a wallet and records an audit log |
| Gateway integration | Gateway/Commerce are external to this repository; routing and end-to-end integration are not verified here |

### Identity and deployment boundary

Controllers read identity and roles from claims or forwarded `x-user-id` / `x-user-roles` headers. The current API host does not configure JWT authentication, and the internal-service authentication middleware is disabled. Treat forwarded identity headers as trusted only when they are set by a trusted gateway that authenticates the caller and overwrites client-supplied values. Do not expose the service directly to untrusted networks until the authentication boundary is verified and enforced.

See [`api/A_ACTIVE/09_WALLET_PAYMENTS_API.md`](api/A_ACTIVE/09_WALLET_PAYMENTS_API.md) for request/response examples and route-specific behavior.
