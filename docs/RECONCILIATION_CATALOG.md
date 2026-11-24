# Reconciliation catalog and shadow ledger

The shadow ledger follows only `WalletAccount.Balance`. It never changes a balance and never changes what an existing field means: it adds columns `LedgerEpoch` and `LedgerSeq` to wallets and new tables. Locked money (auction holds, pending withdrawals) is outside the ledger and is checked by I9.

## Switch and epochs
- `LedgerSettings` (one row): `Enabled`, `ShadowEpoch`, `Version`. Default is off.
- `PUT /api/v1/admin/wallets/ledger/shadow` `{ enabled, expectedVersion }` is idempotent and is also the emergency off switch. Switching on raises `ShadowEpoch` by one; switching on when already on changes nothing; a wrong `expectedVersion` returns 409 `LEDGER_SETTINGS_VERSION_CONFLICT`.
- A wallet opens a new epoch on its first balance event after a switch-on: `LedgerSeq` restarts at 1 with an opening event carrying the balance before the event.
- Every money transaction takes a shared lock on the settings row (`FOR SHARE`) until it commits; the switch takes the exclusive lock. Lock order: settings, then wallet.
- With the ledger on, a save that changes `Balance` without a successful wallet transaction in the same save is refused in every environment.

## Posting
Each balance event writes one `LedgerPostingPending` row in the same database transaction as the money change, with `WalletBalanceAfter` captured under the wallet lock. `JournalPostingWorker` posts them in order into `JournalEntries` and `JournalLines`; a failure keeps the row (`Failed`, retried up to 5 times) and never rolls back money. `DirectGatewayPayment` does not move the balance and is not a ledger event.

Counter accounts: gateway top-ups and direct payments `EXTERNAL_GATEWAY`; order payments, escrow and refunds `ORDER_ESCROW`; auction capture and deposit refund `AUCTION_ESCROW`; forfeits `AUCTION_FORFEIT`; platform fee `PLATFORM_REVENUE`; withdrawals `BANK_WITHDRAWAL`; manual adjustments `ADJUSTMENTS`; transfers `TRANSFER_CLEARING`; anything else `SUSPENSE`. Opening balances use `OPENING_BALANCE`.

## Invariants
| Code | Checks | Finding codes |
|---|---|---|
| I1 | Sub-order: SellerReceive + (PlatformFee - SystemDiscount) + Shipping = Total | I1_FORMULA_MISMATCH |
| I2 | Wallet-paid order has exactly one PurchaseDebit equal to the order total (plus payment adjustments) | I2_MISSING_WALLET_DEBIT, I2_DUPLICATE_DEBIT, I2_AMOUNT_MISMATCH |
| I3 | Escrow exists, original = amount paid, remaining = original - payouts - refunds. Refunds are counted whether they are recorded against the parent order id or against any sub-order id of the order (a sub-order refund is paid out of the parent escrow). Payouts are the V2 `OrderEscrowAmount` (it already contains the seller part, the platform fee and the shipping allocation, so those are not added again) or the legacy `EscrowRelease`. The platform subsidy is paid from its budget and is not an escrow outflow | I3_MISSING_ESCROW, I3_ORIGINAL_MISMATCH, I3_REMAINING_MISMATCH, I3_LEGACY_ESCROW_NOT_REDUCED, I3_OUTFLOW_EXCEEDS_PAYMENT (High: payouts plus refunds taken from the escrow are more than the buyer paid), I3_REFUND_NOT_REDUCED (High: a sub-order refund was credited but the escrow still holds the money, even when the total paid out is not above the payment yet) |
| I4 | Completed sub-order has an EscrowRelease equal to SellerReceive (platform fee is not released separately in the current flow and is not checked). Under settlement V2 the check reads the Wallet's own allocation record (`OrderSettlements`) for the sub-order or auction order and its identity | I4_MISSING_RELEASE, I4_AMOUNT_MISMATCH, I4_ALLOCATION_MISMATCH, I4_AUCTION_AMOUNT_MISMATCH |
| I5 | Completed refund has a successful wallet credit of the same amount | I5_MISSING_REFUND_CREDIT, I5_AMOUNT_MISMATCH |
| I6 | Authorized, captured or released deposit matches a wallet hold (amount and state) | I6_MISSING_HOLD, I6_AMOUNT_MISMATCH, I6_STATUS_MISMATCH |
| I7 | Successful top-up has exactly one credit | I7_MISSING_TOPUP_CREDIT, I7_DUPLICATE_TOPUP_CREDIT, I7_AMOUNT_MISMATCH |
| I8 | Balance chain: each balance-changing transaction starts where the previous ended. Supporting check, it cannot prove order | I8_CHAIN_BREAK, I8_LAST_BALANCE_MISMATCH |
| I9 | LockedBalance = active holds + pending, approved and processing withdrawals | I9_LOCKED_MISMATCH |
| I10 | (a) every entry balances; (b) per wallet, over one unbroken run of one epoch, opening + journal = WalletBalanceAfter of the last event. Results: Matched, Mismatched, or Unchecked with a reason (never Matched without proof) | I10_ENTRY_UNBALANCED, I10_WALLET_LEDGER_MISMATCH |
| I11 | Queue rows still Pending or Failed after the settle buffer | I11_POSTING_OVERDUE, I11_POSTING_FAILED |
| I12 | Balance-changing successful transaction without a queue row, only far from the switch moments. Does not detect a SQL edit of `Balance` alone | I12_MISSING_POSTING_ROW |

When the ledger was never switched on, I10, I11 and I12 are skipped and the run says so. If Commerce or Auction cannot be read the run is `Partial` and names the source; it never invents findings for data it could not read. A problem found again is not stored twice (key: invariant, reference, code).

## API (admin only)
`GET ledger/status`, `GET ledger/journal`, `GET ledger/trial-balance`, `PUT ledger/shadow`, `POST ledger/backfill`, `POST reconciliation/runs`, `GET reconciliation/runs`, `GET reconciliation/runs/{id}`, `GET reconciliation/findings`, `GET reconciliation/findings/{id}`, `PATCH reconciliation/findings/{id}` (status plus required note). All under `/api/v1/admin/wallets/`.

## Approval of this catalog (owner decision)
This catalog is the proposal that the reconciliation runs are built from. It has not been approved yet. Only the project owner signs it off; engineering does not mark it approved.

| Item | Value |
|---|---|
| Status | Pending owner approval |
| Approved by | (to be filled by the owner) |
| Date | (to be filled by the owner) |
| Scope reviewed | I1-I12, finding codes, "Unchecked/Partial" rules, switch and epoch rules |
| Questions for the owner | Are the I4 rule (fee not released separately) and the I12 blind spot (a direct SQL edit of `Balance`) acceptable as written? |
