# Quickstart: Order Escrow Settlement

## Prerequisites

- .NET 8 SDK and restored solution dependencies.
- A disposable PostgreSQL database for the database-backed tests.
- `WALLET_STRIPE_TEST_DATABASE` set to that disposable database connection
  string. Never point it at a shared or production database.

## Validate the implementation

```bash
./scripts/verify-wallet-architecture.sh
dotnet test Wallet.sln -c Release --filter FullyQualifiedName~AuctionOrderEscrowPostgresTests
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

The focused tests must verify:

1. Buyer payment and wallet-payment escrow are created atomically.
2. Seller payout consumes only wallet-payment escrow and is credited exactly once.
3. A partial seller payout followed by a refund consumes only the remaining
   wallet-payment escrow and leaves deposit escrow intact.
4. An underfunded wallet-payment escrow rejects payout even when a captured
   deposit exists; the deposit remains unchanged and fully refundable.
5. Hold deposit refund remains independently idempotent.
6. Insufficient escrow, conflicting retries, concurrent mutations, and database
   failures do not create or lose funds; audit rows commit and roll back with
   the corresponding financial changes.
7. Existing routes, request/response DTOs, feature gates, and regular
   marketplace child-operation behavior remain unchanged.
