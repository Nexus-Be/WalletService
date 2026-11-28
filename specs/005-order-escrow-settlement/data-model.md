# Data Model: Order Escrow Settlement

**Feature**: [Order Escrow Settlement](spec.md)

## New Entity: `OrderEscrow`

One record represents the buyer-paid digital Wallet component of an order payment.

| Field | Type | Constraints / Meaning |
|---|---|---|
| `Id` | `Guid` | Primary key; inherits the existing base entity fields. |
| `OrderId` | `Guid` | Required and unique; stable reference used by payment, payout, and refund. |
| `WalletId` | `Guid` | Required FK to the wallet that funded the payment; delete behavior `Restrict`. |
| `OriginalAmount` | `decimal(18,2)` | Required positive buyer-paid amount; immutable after creation. |
| `RemainingAmount` | `decimal(18,2)` | Required amount still available; constrained to `0 <= RemainingAmount <= OriginalAmount`. |
| `CreatedAt`, `UpdatedAt`, `IsDeleted`, `DeletedAt` | Existing base fields | Standard Wallet entity and query-filter behavior. |

The order ID has a unique index. Financial history remains in immutable
`WalletTransaction` entries; audit entries are appended in the same
transaction as each balance and escrow mutation.

## Existing Entity: `AuctionDepositEscrow`

No schema change is proposed. Its existing `OrderId`, `OriginalAmount`, and
`RemainingAmount` continue to represent the separately captured deposit.
Seller settlement and order-payment refunds never change this entity. The
existing hold-refund operation remains able to return the full original
deposit amount after seller settlement.

## Ledger and State Transitions

| Operation | Wallet-payment escrow | Deposit escrow | Wallet balance |
|---|---:|---:|---|
| Order payment | Create with `OriginalAmount = RemainingAmount = amount` | No change | Debit buyer by `amount` |
| Seller payout | Decrease by the caller-supplied amount if fully available | No change | Credit seller by caller-supplied amount |
| Order refund | Decrease only by the refunded wallet-payment amount | No change | Credit buyer by caller-supplied amount |
| Captured-deposit refund | No change | Existing hold operation restores eligible deposit | Credit buyer by existing hold behavior |

Every row change and corresponding transaction/audit entry commits or rolls
back together. Existing order and hold identifiers remain the idempotency
identities. One seller payout and one refund may both consume an order's
remaining wallet-payment escrow, allowing a refund of the unspent remainder
after a partial payout. Exact retries return their stored transaction; changed
retries and requests exceeding the remaining escrow fail without moving funds.

## Compatibility and Migration

- The migration is additive and introduces only `OrderEscrows`, its
  constraints, and indexes.
- Existing successful order payments without an escrow row may be initialized
  from their matching `PurchaseDebit` only when no payout/refund transaction
  has already resolved that reference.
- A seller payout greater than remaining wallet-payment escrow is rejected even
  when a captured deposit exists; the deposit remains separate and fully
  refundable.
- A marketplace child operation with no matching order-payment reference
  remains on its current compatibility path; this feature does not infer a
  parent order ID.
- Existing `AuctionDepositEscrow` rows are not rewritten or deleted.
