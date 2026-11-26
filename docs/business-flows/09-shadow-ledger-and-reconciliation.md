# Shadow ledger and reconciliation (`I1`–`I12`)

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Reconciliation catalog](../RECONCILIATION_CATALOG.md) | [Sequence diagram](09-shadow-ledger-and-reconciliation.diagram.md)

**Stories:**
- As Finance/Platform Engineering, I want every wallet balance modification guarded and recorded atomically into an append-only double-entry posting queue without breaking ongoing transactions.
- As an admin, I want to toggle or backfill the shadow ledger, inspect balanced journal entries/trial balances, run cross-service reconciliation invariants `I1`–`I12`, and triage findings cleanly.

All admin shadow-ledger and reconciliation endpoints reside under `/api/v1/admin/wallets/` and require **`admin` / `administrator`** role (`EnsureAdmin` returns `403 FORBIDDEN` to `staff`).

---

## 1. Real-time shadow ledger architecture

### Chokepoint in `WalletDbContext.SaveChangesAsync` ([WalletDbContext](../../Wallet.Infrastructure/Persistence/WalletDbContext.cs))
Every EF Core `SaveChangesAsync` executes `CaptureLedgerEventsAsync`:
1. Inspects `ChangeTracker` for any `WalletAccount` whose `Balance` changed (`OriginalValues["Balance"] != CurrentValues["Balance"]`). Pure `LockedBalance` changes (`CreateHold`, `ReleaseHold`, `RequestWithdrawal`, `CancelWithdrawal`, `Reject`) and zero-balance-delta operations (`DirectGatewayPayment`) do **not** trigger shadow ledger events.
2. Reads singleton `LedgerSettings(Id = 1)` via `ReadLedgerSettingsAsync`: inside an active relational transaction, acquires **`SELECT ... FOR SHARE`** until commit so `PUT ledger/shadow` (`FOR UPDATE`) cannot toggle mid-transaction. *(Handlers acquire wallet locks before calling `SaveChangesAsync`, whereas `SetShadowLedger` never locks wallets — preventing deadlocks.)*
3. **When `LedgerSettings.Enabled == true`:**
   - **Mandatory transaction guard (all environments):** Requires at least one `WalletTransaction` in the **same `SaveChangesAsync`** for that `WalletId` that is `Added` (or `Modified` transitioning to `Status == Success`). Any unbacked `Balance` edit throws `InvalidOperationException` and rolls back the transaction.
   - **Epoch opening:** If `wallet.LedgerEpoch != settings.ShadowEpoch`, sets `wallet.LedgerEpoch = settings.ShadowEpoch`, resets `wallet.LedgerSeq = 1`, and writes an `IsEpochOpening = true` `LedgerPostingPending` row (`Type = "EpochOpening"`, `OpeningBalance = before`, `WalletBalanceAfter = before`).
   - **Pending event row:** Increments `wallet.LedgerSeq`, records `LedgerPostingPending` with `WalletTransactionId = transactions[0].Id`, `RelatedTransactionIds` (comma-separated tx IDs), `Type` (`+`-joined distinct types), `Delta = after - before`, and `WalletBalanceAfter = after`.
4. **Two-step save convention:** Handlers performing two distinct accounting movements on the same wallet inside one transaction ([SettleOrderPayout](../../Wallet.Application/Features/Settlements/Commands/SettleOrderPayout/SettleOrderPayoutCommandHandler.cs) for `EscrowRelease` + `PlatformSubsidyCredit`; [PayRefundFundCase](../../Wallet.Application/Features/RefundFund/Commands/PayRefundFundCase/PayRefundFundCaseCommandHandler.cs) for `RefundFundPayout` + `RefundFundRecovery`) flush an intermediate `SaveChangesAsync()` inside the transaction so `CaptureLedgerEventsAsync` records separate queue rows (and avoids net-zero-delta skipping when recovery equals payout).

### Asynchronous Journal Posting ([JournalPostingWorker](../../Wallet.Api/JournalPostingWorker.cs), [JournalPoster](../../Wallet.Application/Common/Ledger/JournalPoster.cs), [LedgerPostingRules](../../Wallet.Application/Common/Ledger/LedgerPostingRules.cs))
- **Cadence:** Every **2 seconds**; ensures the `14` `LedgerAccounts` exist on startup, then drains up to `200` `LedgerPostingPending` rows per tick (`Status == Pending` or `Status == Failed && Attempts < 5`) ordered `(WalletId, LedgerEpoch, LedgerSeq)`.
- **Balanced double-entry rules:** Each pending row commits into `JournalEntries` (`PendingId` unique filtered index prevents duplicates, `Source = "Live"`) and two `JournalLines` (`Debit == Credit`, control account `WALLET` on one side and counter-account on the other):
  - Counter accounts (`CounterAccount(type)`): `TopupVnpay`/`TopupStripe`/`DirectGatewayPayment` -> `EXTERNAL_GATEWAY`; order payment/escrow/release/refund/adjustments -> `ORDER_ESCROW`; `AuctionHoldCapture`/`AuctionDepositRefund` -> `AUCTION_ESCROW`; `AuctionForfeitCredit` -> `AUCTION_FORFEIT`; `PlatformFeeCredit` -> `PLATFORM_REVENUE`; `PlatformSubsidyCredit` -> `PLATFORM_SUBSIDY`; `WithdrawalDebit`/`WithdrawalReversal` -> `BANK_WITHDRAWAL`; `Adjustment*` -> `ADJUSTMENTS`; `Transfer*` -> `TRANSFER_CLEARING`; `RefundFundDeposit` -> `REFUND_FUND_FUNDING`; `RefundFundPayout`/`RefundFundRefund`/`SellerRecoveryDebit`/`RefundFundRecovery` -> `REFUND_FUND_CLEARING`; fallback -> `SUSPENSE`; epoch opening -> `OPENING_BALANCE`.
- **Isolated failure handling:** Posting errors mark the pending row `Failed` (up to `5` attempts) without rolling back wallet balances.

### Admin Shadow-Ledger Controls

| Endpoint | Handler | Behavior & Constraints |
|---|---|---|
| `PUT /api/v1/admin/wallets/ledger/shadow` | [SetShadowLedgerCommandHandler](../../Wallet.Application/Features/Ledger/Commands/SetShadowLedger/SetShadowLedgerCommandHandler.cs) | Optimistic lock via `expectedVersion` (`409 LEDGER_SETTINGS_VERSION_CONFLICT`). Switching on raises `ShadowEpoch` and `Version`, logs `AuditAction.LedgerShadowSwitch`. Emergency off keeps `ShadowEpoch`; idempotent repeat returns `changed: false`. |
| `POST /api/v1/admin/wallets/ledger/backfill` | [BackfillJournalCommandHandler](../../Wallet.Application/Features/Ledger/Commands/BackfillJournal/BackfillJournalCommandHandler.cs) | Optional `{ walletId }`. Writes `"Backfill"` `JournalEntries` only for wallets with a provable transaction chain starting at `BalanceBefore == 0` with no gaps (`walletsBackfilled`, `walletsSkipped`, `entriesCreated`), skipping already-posted txs. |
| `GET /api/v1/admin/wallets/ledger/status` | [GetLedgerStatusQueryHandler](../../Wallet.Application/Features/Ledger/Queries/GetLedgerStatus/GetLedgerStatusQueryHandler.cs) | Switch state, `shadowEpoch`, `version`, per-followed-wallet `I10` verdict (`Matched`/`Mismatched`/`Unchecked`), history completeness, and queue lag. |
| `GET /api/v1/admin/wallets/ledger/journal` | [ListJournalQueryHandler](../../Wallet.Application/Features/Ledger/Queries/ListJournal/ListJournalQueryHandler.cs) | Paginated `JournalLines` filterable by `transactionId` and `walletId`. |
| `GET /api/v1/admin/wallets/ledger/trial-balance` | [GetTrialBalanceQueryHandler](../../Wallet.Application/Features/Ledger/Queries/GetTrialBalance/GetTrialBalanceQueryHandler.cs) | Sums `Debit` and `Credit` per `AccountCode` and asserts `TotalDebit == TotalCredit` (`Balanced`). |

---

## 2. Reconciliation Engine (`ReconciliationEvaluator`, `I1`–`I12`)

### Execution & Safety Model ([ReconciliationEvaluator](../../Wallet.Application/Common/Ledger/ReconciliationEvaluator.cs))
- **Consistent snapshot & buffer:** Opens a `RepeatableRead` DB transaction for wallet-side queries; applies a `30s` `SettleBuffer` (`cutoff = UtcNow - 30s`) on cross-service and time-windowed records; compares money amounts within `0.01` VND tolerance.
- **External readers ([HttpReconciliationSourceClient](../../Wallet.Infrastructure/Services/HttpReconciliationSourceClient.cs)):** Fetches orders from Commerce (`Reconciliation:CommerceBaseUrl`) and deposits from Auction (`Reconciliation:AuctionBaseUrl`) with `X-Internal-Service-Key` (`20s` timeout). If either fails, records `UnavailableSources` + `SkippedNote`, sets run `Status = Partial`, and **never fabricates findings** for unreadable data.
- **Deduplication:** Inserts `ReconciliationFindings` keyed by `DedupKey = "{InvariantCode}|{ReferenceId}|{Code}"`; pre-existing findings keep their current triage `Status` (`Open`, `Investigating`, `Resolved`, `Accepted`) and `Note` (`run.FindingCount` counts newly inserted findings).

### Invariants `I1`–`I12`

| Code | What Is Verified | Emitted Finding Codes & Notes |
|---|---|---|
| **I1** | Commerce sub-order math: `SellerReceive + (PlatformFee - SystemDiscount) + Shipping == Total` | `I1_FORMULA_MISMATCH` (`High`, `commerce-service`) |
| **I2** | Wallet-paid Commerce orders (`Paid`/`Completed`/`Delivered`) have 1 `PurchaseDebit` matching `TotalAmount` net of adjustments | `I2_MISSING_WALLET_DEBIT`, `I2_DUPLICATE_DEBIT`, `I2_AMOUNT_MISMATCH` (`High`) |
| **I3** | `OrderEscrow` exists, `OriginalAmount == debit + adjustments`, `RemainingAmount == Original - payouts(V1 + V2 OrderEscrowAmount) - refunds(parent + sub-order)` (subsidies excluded), **plus** `AuctionPayout` escrow checks | `I3_MISSING_ESCROW`, `I3_ORIGINAL_MISMATCH`, `I3_OUTFLOW_EXCEEDS_PAYMENT`, `I3_REFUND_NOT_REDUCED`, `I3_REMAINING_MISMATCH` (`High`); `I3_LEGACY_ESCROW_NOT_REDUCED` (`Low`) |
| **I4** | Completed Commerce sub-orders vs `OrderSettlements` (all 5 allocation amounts) + `EscrowRelease`/`PlatformSubsidyCredit` sum; **plus** `AuctionPayout` settlement allocations | `I4_ALLOCATION_MISMATCH`, `I4_MISSING_RELEASE`, `I4_AMOUNT_MISMATCH`, `I4_AUCTION_AMOUNT_MISMATCH` (`High`) |
| **I5** | Completed Commerce refunds match `RefundCredit \| EscrowCancellationRefund \| RefundFundRefund` 1-to-1; **plus `CheckRefundFundAsync`**: fund balance equals `sum(deposits + recoveries - payouts)` and all `RefundFundPayout` rows link valid txs | `I5_MISSING_REFUND_CREDIT`, `I5_AMOUNT_MISMATCH`, `I5_REFUND_FUND_BALANCE_MISMATCH`, `I5_REFUND_FUND_PAYOUT_MISMATCH` (`High`) |
| **I6** | Auction deposits (`Authorized`/`Captured`/`Released`) match `AuctionHold` amount and mapped state (`Released` allows `Released` or `Refunded`) | `I6_MISSING_HOLD`, `I6_AMOUNT_MISMATCH`, `I6_STATUS_MISMATCH` (`High`) |
| **I7** | Every `Success` `WalletTopup` (past `SettleBuffer`) has 1 matching `TopupStripe \| TopupVnpay` credit | `I7_MISSING_TOPUP_CREDIT` (`Medium`), `I7_DUPLICATE_TOPUP_CREDIT` (`High`), `I7_AMOUNT_MISMATCH` (`Medium`) |
| **I8** | Per wallet: balance-changing `Success` non-direct txs form a contiguous `BalanceBefore -> BalanceAfter` chain ending at `Wallet.Balance` | `I8_CHAIN_BREAK` (`Medium`), `I8_LAST_BALANCE_MISMATCH` (`High`) *(protected by `+10 µs` timestamps on 2-step same-wallet writes)* |
| **I9** | Per wallet: `LockedBalance == sum(Held AuctionHolds) + sum(Pending \| Approved \| Processing Withdrawals)` | `I9_LOCKED_MISMATCH` (`High`) |
| **I10** | (a) All `JournalEntries` satisfy `Debit == Credit`; (b) Per shadow-tracked wallet over an unbroken posted run of its active `LedgerEpoch`, opening + journal net matches `WalletBalanceAfter` | `I10_ENTRY_UNBALANCED` (`Critical`), `I10_WALLET_LEDGER_MISMATCH` (`High`) *(skipped when shadow ledger never switched on)* |
| **I11** | `LedgerPostingPending` rows still `Pending` or `Failed` after `cutoff - 1m` | `I11_POSTING_FAILED`, `I11_POSTING_OVERDUE` (`High`) |
| **I12** | Balance-changing `Success` non-direct txs well inside an active shadow-ledger window (`±2m` margin around `LedgerShadowSwitch` / opening rows) must appear in `RelatedTransactionIds` | `I12_MISSING_POSTING_ROW` (`Medium`) *(cannot detect direct out-of-band SQL edits)* |

### Reconciliation Run & Triage Endpoints

| Endpoint | Handler | Purpose |
|---|---|---|
| `POST /api/v1/admin/wallets/reconciliation/runs` | [RunReconciliationCommandHandler](../../Wallet.Application/Features/Ledger/Commands/RunReconciliation/RunReconciliationCommandHandler.cs) | Runs selected `invariants` (`I1`–`I12`) over `[fromDate, toDate]` (`ToDate >= FromDate`), logs `AuditAction.ReconciliationRun`, never mutates balances. |
| `GET /api/v1/admin/wallets/reconciliation/runs` & `/runs/{id:guid}` | [ListReconciliationRunsQueryHandler](../../Wallet.Application/Features/Ledger/Queries/ListReconciliationRuns/ListReconciliationRunsQueryHandler.cs), [GetReconciliationRunQueryHandler](../../Wallet.Application/Features/Ledger/Queries/GetReconciliationRun/GetReconciliationRunQueryHandler.cs) | Inspect run statuses (`Completed`/`Partial`/`Failed`), skipped notes, and counts. |
| `GET /api/v1/admin/wallets/reconciliation/findings` & `/findings/{id:guid}` | [ListReconciliationFindingsQueryHandler](../../Wallet.Application/Features/Ledger/Queries/ListReconciliationFindings/ListReconciliationFindingsQueryHandler.cs), [GetReconciliationFindingQueryHandler](../../Wallet.Application/Features/Ledger/Queries/GetReconciliationFinding/GetReconciliationFindingQueryHandler.cs) | Filter findings by `status`, `invariantCode`, `severity`, and date range. |
| `PATCH /api/v1/admin/wallets/reconciliation/findings/{id:guid}` | [UpdateReconciliationFindingCommandHandler](../../Wallet.Application/Features/Ledger/Commands/UpdateReconciliationFinding/UpdateReconciliationFindingCommandHandler.cs) | Transitions `Status` (`Open`, `Investigating`, `Resolved`, `Accepted`) with required `note` (max 2000 chars); logs `AuditAction.ReconciliationFindingUpdate`; never alters wallet balances. |

---

## 3. Summary of Hosted Background Workers

| Worker | Interval | Batch / Limits | Purpose |
|---|---|---|---|
| [StripeTopupReconciliationWorker](../../Wallet.Api/StripeTopupReconciliationWorker.cs) | **1 minute** | 30 stale creation + 30 top-ups + 30 direct orders | Promotes >10m `CreationPending` attempts to `ReconciliationRequired`; polls Stripe >1m `AwaitingPayment` sessions cleanly. |
| [WithdrawalNotificationRetryWorker](../../Wallet.Api/WithdrawalNotificationRetryWorker.cs) | **30 seconds** | 20 events, max `8` attempts, `30s` initial delay | Re-dispatches `NotificationQueued` withdrawal events with exponential backoff (max `60m`) until `NotificationDelivered` or `NotificationAbandoned`. |
| [JournalPostingWorker](../../Wallet.Api/JournalPostingWorker.cs) | **2 seconds** | 200 queue rows, max `5` attempts/row | Ensures `LedgerAccounts` seed and posts `LedgerPostingPending` into balanced `JournalEntries`/`JournalLines`. |
