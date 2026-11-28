# Quickstart: Wallet Service Phase 2 Financial Operations

This guide is the validation plan for implementation after the feature plan and architecture amendment are approved. It does not create implementation code.

## Prerequisites

- .NET 8 SDK
- PostgreSQL test database configured using repository-local test settings
- VNPay sandbox secrets only if running unrelated payment regression tests
- No production credentials or bank account data in test fixtures

## Baseline checks

Run from the repository root before implementation:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

Expected baseline architecture output: `OK: Wallet CQRS architecture is intact (15 handlers, 2 controllers, MediatR wired).`

## Validation scenarios

1. **Admin balance adjustment by user ID**: submit a credit and debit for a wallet owner's user ID; verify the existing balance, transaction, audit, response, and insufficient-balance behavior. Verify that a user ID without a wallet returns the existing not-found error.
2. **Withdrawal approval**: create a pending request, approve with a valid transfer reference, and verify locked funds are consumed, the reference and audit actor are stored, and status is `Approved`.
3. **Withdrawal rejection/retry**: reject a separate request and verify funds return to available balance; repeat either decision and verify no second balance change.
4. **Approval input validation**: omit or exceed the reference length on approval and verify the standard validation response and unchanged financial state.
5. **Ledger report**: query a stable test dataset with wallet/type/date filters and multiple pages; compare returned items and metadata with the database rows.
6. **Sensitive-data exclusion**: verify report results and transaction notes do not expose an unmasked bank account number.
7. **Reconciliation**: verify `Balanced`, `Discrepancy`, and `InsufficientHistory` results for matching snapshots, mismatched wallet balances, and wallets without successful transaction history.
8. **Authorization**: verify admin-only query actions reject unauthenticated and non-admin callers.
9. **Read-only behavior**: verify ledger and reconciliation requests do not change balances, transactions, withdrawal states, or audit rows.

## Final verification

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

Inspect `/swagger/v1/swagger.json` for the new routes and query parameters. Run integration scenarios against PostgreSQL and inspect query plans before adding indexes or migrations.

Do not deploy the deferred purchase-freeze or auction endpoints until their separate service-authentication and API contracts are approved.
