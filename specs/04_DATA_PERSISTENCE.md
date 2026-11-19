# 04 — WalletService data persistence

Updated: 2026-11-19. The source of truth is
`Wallet.Infrastructure/Persistence/WalletDbContext.cs`, entity configurations
and migrations under `Wallet.Infrastructure/Persistence/Migrations/`.
PostgreSQL uses quoted PascalCase table names, such as `"Wallets"`; these
are distinct from any unquoted snake_case legacy tables. Do not remove or
rename legacy tables without reviewing and backing up their data.

## Entities and write paths

| Entity / table | Purpose |
|---|---|
| `WalletAccount` / `Wallets` | Unique wallet per user; total balance and shared locked balance. |
| `WalletTransaction` / `WalletTransactions` | Wallet balance changes and direct-payment ledger records. |
| `WalletTopup` / `WalletTopups` | Stripe top-up lifecycle; nullable VNPay reference fields retain historical data. |
| `DirectPaymentOrder` / `DirectPaymentOrders` | Direct checkout identity and payment state; unique `OrderId`. |
| `StripePaymentAttempt` / `StripePaymentAttempts` | Provider session, request identity and settlement state. |
| `StripeWebhookEvent` / `StripeWebhookEvents` | Durable provider event and processing records. |
| `WithdrawalRequest` / `WithdrawalRequests` | Withdrawal reservation, 6-step workflow (`Pending`, `Approved`, `Processing`, `Completed`, `Rejected`, `Failed`, `Cancelled`), idempotency key/fingerprint, and optional `BankAccountId`; no automatic bank payout. |
| `AuctionHold` / `AuctionHolds` | Owner, reference, amount, metadata and deposit state. |
| `AuctionHoldOperation` / `AuctionHoldOperations` | Idempotency key, fingerprint and original mutation response. |
| `AuctionHoldCapture` / `AuctionHoldCaptures` | One capture purpose and optional order per hold. |
| `AuctionCaptureTransfer` / `AuctionCaptureTransfers` | Captured seller, platform and deposit-escrow allocations. |
| `AuctionDepositEscrow` / `AuctionDepositEscrows` | Deposit amount and remaining balance per hold and order. |
| `AuditLog` / `AuditLogs` | Audit entries for sensitive operations (including `NotificationDispatchFailed` payloads). |
| `BankAccount` / `BankAccounts` | Seller saved bank accounts with filtered unique `IsDefault = TRUE` index per user. |
| `VnpayIpnLog` / `VnpayIpnLogs` | Historical VNPay IPN data; VNPay payment endpoints are inactive. |

Use the configurations and migrations for exact column names, indexes,
filters and foreign keys. Enum values are stored as integers unless a
specific configuration states otherwise. Amounts use `decimal(18,2)`;
auction deposits and Stripe payments require whole VND.

## Balance and hold invariants

- `0 <= LockedBalance <= Balance` and `Balance >= 0` are enforced by
  `CK_Wallets_LockedBalance` and `CK_Wallets_BalanceLocks`. Locked balance includes withdrawal reservations
  and auction holds; available balance is the difference.
- Holds have a globally unique `(ReferenceType, ReferenceId)`, positive
  whole-VND amount and immutable identity. Hold states are `Held`, `Captured`,
  `Released` and `Refunded`.
- Hold operation keys are unique. Idempotency records, hold resolution,
  wallet transactions and escrow/transfer records commit with the balance
  updates in the same transaction.
- Capture is all-or-nothing. Seller credits are wallet transactions; platform
  credits are retained in the capture transfer ledger. Order-payment escrow
  is deposit-specific and is not connected to Commerce settlement.
- Buyer and recipient wallets are locked in ascending user ID order for
  capture. Hold and wallet row locks serialize conflicting financial updates.

See [auction deposit contracts](../docs/api/holds/auction-deposits.md) and
[Stripe contracts](../docs/api/stripe/payments.md) for API and operational
details. The existing reconciliation report compares a wallet's balance with
its latest successful transaction snapshot; it is not external bank
reconciliation or a complete double-entry ledger.

## Migrations and verification

Migrations are tracked in this repository. The API attempts to apply them at
startup; exceptions are logged and startup can continue. For shared databases,
review the migration, assess existing data and take a backup before applying
changes. The application previously used VNPay and legacy records should not
be deleted merely because the active payment provider changed.

Run the architecture, build and test commands in
[the verification checklist](05_DEV_VERIFICATION_CHECKLIST.md). Set
`WALLET_STRIPE_TEST_DATABASE` only to a disposable PostgreSQL database when
running PostgreSQL integration tests.
