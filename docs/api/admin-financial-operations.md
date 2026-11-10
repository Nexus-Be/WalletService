# Wallet Service — Withdrawal processing and administrator reports

> **Feature / Use cases:** Process withdrawal, list wallet ledger, reconcile wallet balances  
> **Status:** IN_PROGRESS  
> **Approved:** 2026-11-10

This document describes WalletService's existing staff withdrawal decision
route with its additive transfer-reference field and two read-only administrator
reports. The report is over WalletService transaction records; it is not a
double-entry general ledger or an external bank reconciliation.

## 1. Routes and authorization

| Method | Route | Authorization | CQRS use case |
|---|---|---|---|
| `PATCH` | `/api/v1/admin/wallets/withdrawals/{id}` | Admin or staff | `ProcessWithdrawalCommand` |
| `GET` | `/api/v1/admin/wallets/ledger` | Admin only | `ListWalletLedgerQuery` |
| `GET` | `/api/v1/admin/wallets/reconciliation` | Admin only | `GetWalletReconciliationQuery` |

All actions remain in
`Wallet.Api/Controllers/Public/AdminWalletsController.cs`. No internal API,
new controller, or service-to-service route is introduced.

## 2. Withdrawal decision

### Request

```json
{
  "action": "Approve",
  "adminNote": null,
  "referenceCode": "BANK-TRANSFER-2026-001"
}
```

| Field | Type | Required | Constraint |
|---|---|---:|---|
| `action` | string | Yes | `Approve` or `Reject` |
| `adminNote` | string | No | Used as the rejection reason in the audit record |
| `referenceCode` | string | For approval | Non-blank and at most 100 characters; optional for rejection |

`Approve` confirms that staff has already completed the manual bank transfer;
the endpoint does not initiate a payout. The transfer reference is stored in
the existing `WithdrawalRequest.GatewayReferenceId` field. Approval consumes
the reserved balance; rejection releases it. The row-lock and transaction
patterns ensure a decision cannot apply a second balance change on retry.

### Response

The existing response is extended with nullable `referenceCode`; route and
status values are unchanged.

```json
{
  "withdrawalId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Approved",
  "processedAt": "2026-11-10T01:00:00Z",
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "referenceCode": "BANK-TRANSFER-2026-001"
}
```

The response reference is `null` for a rejection. Transaction notes mask the
bank account number; the full account number is not part of the ledger report.

## 3. Administrator transaction report

`GET /api/v1/admin/wallets/ledger`

### Query parameters

| Parameter | Type | Default / bounds | Behavior |
|---|---|---|---|
| `page` | integer | `1`, minimum `1` | 1-based page index |
| `pageSize` | integer | `10`, range `1`-`100` | Maximum rows per page |
| `walletId` | UUID | Omitted | Restricts records to one wallet |
| `type` | string | Omitted | A defined `WalletTransactionType` name |
| `status` | string | Omitted | `Pending`, `Success`, `Failed`, or `Cancelled` |
| `fromDate` | date/time | Omitted | Inclusive lower timestamp bound |
| `toDate` | date/time | Omitted | Inclusive upper timestamp bound; must not precede `fromDate` |

Results are ordered by `CreatedAt DESC`, then transaction ID descending.
Filtering, counting, ordering, and pagination run in the database.

### Response

Returns a pure `PagedData<AdminWalletLedgerEntry>`; no service-side envelope is
added. Entries contain transaction ID, wallet ID, type, amount, currency,
balance before/after, status, optional reference ID, and creation timestamp.
They do not contain notes or bank destination fields.

```json
{
  "items": [
    {
      "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "walletId": "2fa85f64-5717-4562-b3fc-2c963f66afa6",
      "type": "WithdrawalDebit",
      "amount": 100000,
      "currency": "Vnd",
      "balanceBefore": 500000,
      "balanceAfter": 400000,
      "status": "Success",
      "referenceId": "4fa85f64-5717-4562-b3fc-2c963f66afa6",
      "createdAt": "2026-11-10T01:00:00Z"
    }
  ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 10,
    "totalCount": 1,
    "totalPages": 1,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

## 4. Wallet reconciliation report

`GET /api/v1/admin/wallets/reconciliation`

Accepts `page` (default `1`, minimum `1`), `pageSize` (default `10`, range
`1`-`100`), and optional `walletId`. Wallets are ordered by ID for stable
database pagination.

For each wallet, the report compares `WalletAccount.Balance` with the
`BalanceAfter` value of its latest successful transaction, ordered by
`CreatedAt DESC` and transaction ID descending. `difference` is current balance
minus the latest successful transaction balance. `LockedBalance` does not
change the comparison because the stored transaction snapshot represents total
wallet balance.

`result` is `Balanced`, `Discrepancy`, or `InsufficientHistory`. A wallet with
no successful transaction snapshot is always `InsufficientHistory`, including
a wallet with a zero balance. Reconciliation is read-only and does not repair
or rewrite transactions, balances, or audit rows.

## 5. Error responses

Errors use the existing `application/problem+json` shape.

| HTTP status | Error code | Condition |
|---|---|---|
| `400` | `VAL_INVALID_INPUT` | Missing/blank/oversized approval reference, invalid report filter, date range, or pagination |
| `400` | `BUSINESS_RULE_VIOLATION` | Withdrawal has already been processed |
| `401` | `UNAUTHORIZED` | Caller identity is missing |
| `403` | `FORBIDDEN` | Caller lacks the required role; staff cannot access the two reports |
| `404` | `NOT_FOUND` | Withdrawal ID does not exist |

## 6. Enum boundary

Enum values are persisted as integers and returned as PascalCase strings.

| Enum | Integer | API string |
|---|---:|---|
| `TransactionStatus` | `0` | `Pending` |
| `TransactionStatus` | `1` | `Success` |
| `TransactionStatus` | `2` | `Failed` |
| `TransactionStatus` | `3` | `Cancelled` |
| `WalletTransactionType` | `5` | `WithdrawalDebit` |
| `WithdrawalStatus` | existing values | `Pending`, `Approved`, `Rejected` |

## 7. Persistence and verification

No schema migration is planned: `GatewayReferenceId` already exists with a
100-character persistence limit. The report handlers use `AsNoTracking`,
database-side `CountAsync`, and `Skip`/`Take`. Do not add an index or migration
without evidence from PostgreSQL query plans.

Required checks:

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```
