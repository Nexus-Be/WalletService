# Administration, operational controls, and audit sync

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Shadow ledger & reconciliation](09-shadow-ledger-and-reconciliation.md) | [Sequence diagram](08-administration.diagram.md)

All administrative HTTP routes live under `/api/v1/admin/wallets` in [AdminWalletsController](../../Wallet.Api/Controllers/Public/AdminWalletsController.cs) (32 actions), plus 2 internal audit-sync routes in [WalletsController](../../Wallet.Api/Controllers/Public/WalletsController.cs).

## Role authorization matrix

Class-level `[RequireRole("admin", "staff")]` ([RequireRoleAttribute](../../Wallet.Api/Filters/RequireRoleAttribute.cs)) admits callers with role `admin`, `administrator`, or `staff`. `RequireRoleAttribute` grants access only when `IsAdmin()` OR the caller holds one of the explicitly allowed roles—`staff` cannot bypass `admin`-only or `seller` routes. In `AdminWalletsController`, **23 actions additionally enforce `EnsureAdmin` (`admin` / `administrator` only)**:

| Role Scope | Operations |
|---|---|
| **`admin` or `staff`** (9 actions) | `GET ""` (`ListWallets`), `GET {userId:guid}` (`GetUserWallet`, `AutoCreate = false`), `POST {userId:guid}/freeze`, `POST {userId:guid}/unfreeze` *(system wallets protected)*, `GET holds`, `GET {userId:guid}/holds`, `GET withdrawals`, `GET withdrawals/{id:guid}/history` *(bank details redacted)*, `PATCH withdrawals/{id:guid}` *(`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail` only; `ResolveFailed` returns `403 FORBIDDEN` to staff)*. |
| **`admin` / `administrator` ONLY** (23 actions) | `PATCH withdrawals/{id}` with `action=ResolveFailed`; `POST {userId}/adjustments`; `GET ledger`; `GET reconciliation`; `GET ledger/status`; `GET ledger/journal`; `GET ledger/trial-balance`; `PUT ledger/shadow`; `POST ledger/backfill`; `POST/GET reconciliation/runs` (+ `/{id}`); `GET/PATCH reconciliation/findings` (+ `/{id}`); `GET/POST subsidy-budgets` (+ `/approve`, `/close`); `GET refund-fund`; `POST refund-fund/deposits`; `GET stripe/reconciliation-required`; `POST stripe/attempts/{id}/resolve`; `POST stripe/reconcile-pending`. |

---

## Wallets, freezes, and balance adjustments

1. **Search & single-wallet inspection:**
   - `GET /api/v1/admin/wallets?page=1&pageSize=20&status=&userId=&search=&currency=`: paginated filter (`AsNoTracking`, ordered `UpdatedAt DESC, CreatedAt DESC, Id ASC`).
   - `GET /api/v1/admin/wallets/{userId:guid}`: reads wallet with `AutoCreate = false` (`404 WALLET_NOT_FOUND` if missing; never creates a wallet on inspection).
2. **Freeze / Unfreeze ([WalletFreezeHelper](../../Wallet.Application/Features/Wallets/WalletFreezeHelper.cs)):**
   - Admin/staff routes (`POST /api/v1/admin/wallets/{userId:guid}/freeze` & `/unfreeze`) and Commerce internal routes (`POST ~/api/v1/internal/wallets/freeze-purchase` & `/unfreeze-purchase`, `[RequireInternalServiceKey]`) share `WalletFreezeHelper`.
   - **System wallet protection:** Attempting to freeze `RefundFundAccount.UserId` (`f0f0f0f0-0000-0000-0000-000000000001`) throws `403 SYSTEM_WALLET_PROTECTED`.
   - Freeze uses `LockOrEnsureWalletByUserIdAsync` (auto-creates wallet as `FROZEN` if absent); Unfreeze uses `LockWalletByUserIdAsync` (`404 WALLET_NOT_FOUND` if absent). Both are idempotent when already in target state and log `AuditAction.WalletFrozen` / `AuditAction.WalletUnfrozen` only on state change.
3. **Manual Balance Adjustment (`POST /api/v1/admin/wallets/{userId:guid}/adjustments`, admin-only):**
   - [AdjustWalletBalanceCommandHandler](../../Wallet.Application/Features/AdminWallets/Commands/AdjustWalletBalance/AdjustWalletBalanceCommandHandler.cs) accepts `adjustmentType` (`Credit` | `Debit`), `amount` (`0.01`–`500,000,000`), `reason` (up to 500 chars), and optional `Idempotency-Key` header (1–100 ASCII `[A-Za-z0-9-_]`).
   - Locks wallet (`404 WALLET_NOT_FOUND`). When keyed, verifies SHA-256 fingerprint over `(userId, type, amount, reason)` against prior adjustment transactions (`409 IDEMPOTENCY_KEY_REUSED` on mismatch; replays prior `BalanceAdjustmentResponse` on match).
   - `Debit` enforces `AvailableBalance >= amount` (`400 INSUFFICIENT_BALANCE`). Does **not** require `ACTIVE` status so admins can adjust frozen wallets. Records `AdjustmentCredit`/`AdjustmentDebit` and `AuditAction.BalanceAdjustment`.

---

## Legacy transaction ledger & snapshot reconciliation (admin-only)

- **`GET /api/v1/admin/wallets/ledger` ([ListWalletLedgerQueryHandler](../../Wallet.Application/Features/AdminWallets/Queries/ListWalletLedger/ListWalletLedgerQueryHandler.cs)):** Paginated (`pageSize` 1–100, default `10`) filter by `walletId`, `userId`, `referenceId`, `type`, `status`, `fromDate`/`toDate` (UTC).
- **`GET /api/v1/admin/wallets/reconciliation` ([GetWalletReconciliationQueryHandler](../../Wallet.Application/Features/AdminWallets/Queries/GetWalletReconciliation/GetWalletReconciliationQueryHandler.cs)):** Read-only per-wallet snapshot comparison (`Balanced`, `Discrepancy`, `InsufficientHistory`).
  - Compares `Wallet.Balance` against latest `Success` non-`DirectGatewayPayment` transaction's `BalanceAfter`.
  - **Scope caveat:** `GetWalletReconciliation` compares `LockedBalance` against `Held` auction holds plus **`Pending`-only** withdrawal requests, whereas full invariant `I9` in [ReconciliationEvaluator](09-shadow-ledger-and-reconciliation.md) sums `Pending`, `Approved`, and `Processing` withdrawals. Use `POST reconciliation/runs` (`I1`–`I12`) for authoritative cross-service & lock reconciliation.

---

## Stripe manual recovery, platform subsidy budgets & refund fund (admin-only)

1. **Stripe recovery:**
   - `GET /api/v1/admin/wallets/stripe/reconciliation-required`: paginated list of `StripePaymentAttempt` rows in `ReconciliationRequired`, filterable by `purpose` (`Topup` | `DirectOrder`).
   - `POST /api/v1/admin/wallets/stripe/attempts/{attemptId:guid}/resolve` ([ResolveStripeAttemptCommandHandler](../../Wallet.Application/Features/AdminWallets/Commands/ResolveStripeAttempt/ResolveStripeAttemptCommandHandler.cs)): requires `Resolution` (`Failed` | `Expired`) and `Reason` (3–500 chars); locks parent -> attempt (`409 INVALID_ATTEMPT_STATE` if not `ReconciliationRequired`), clears `BlocksReplacement`, marks `Pending` parent (`WalletTopup` / `DirectPaymentOrder` + pending `DirectGatewayPayment` tx) as `Failed`/`Expired`, logs `AuditAction.StripeAttemptResolve`.
   - `POST /api/v1/admin/wallets/stripe/reconcile-pending`: manually triggers `ReconcilePendingTopupsCommand` and returns `{ processedCount }`.
2. **Platform subsidy budgets ([Create](../../Wallet.Application/Features/Subsidies/Commands/CreateSubsidyBudget/CreateSubsidyBudgetCommandHandler.cs), [Approve](../../Wallet.Application/Features/Subsidies/Commands/ApproveSubsidyBudget/ApproveSubsidyBudgetCommandHandler.cs), [Close](../../Wallet.Application/Features/Subsidies/Commands/CloseSubsidyBudget/CloseSubsidyBudgetCommandHandler.cs), [List](../../Wallet.Application/Features/Subsidies/Queries/ListSubsidyBudgets/ListSubsidyBudgetsQueryHandler.cs)):**
   - `GET subsidy-budgets`: lists budgets with `reservedAmount`, `usedAmount`, `availableAmount`, `status`.
   - `POST subsidy-budgets`: proposes budget (`name`, `totalLimit`, `dailyLimit`, `perOrderCap`, `evidenceRef`) enforcing `PerOrderCap <= DailyLimit <= TotalLimit` (`400 SUBSIDY_LIMITS_INVALID`) in `PendingApproval`.
   - `POST subsidy-budgets/{budgetId:guid}/approve`: locks `"subsidy-budget-activation"` -> budget. Enforces four-eyes (`ApprovedByAdminId != CreatedByAdminId`, else `403 SUBSIDY_APPROVER_MUST_DIFFER`) and at most **one** `Active` budget (`409 SUBSIDY_ACTIVE_BUDGET_EXISTS`). Idempotent replay for the same approver.
   - `POST subsidy-budgets/{budgetId:guid}/close`: closes an `Active` budget to new holds (existing `Held` reservations can still be used or released).
3. **Refund fund operations ([GetRefundFundStatus](../../Wallet.Application/Features/RefundFund/Queries/GetRefundFundStatus/GetRefundFundStatusQueryHandler.cs), [DepositRefundFund](../../Wallet.Application/Features/RefundFund/Commands/DepositRefundFund/DepositRefundFundCommandHandler.cs)):**
   - `GET refund-fund`: returns `{ enabled, balance, perCaseCap, dailyCap, secondApprovalThreshold, paidToday, remainingToday }` (using Vietnam UTC+7 day boundary in UTC).
   - `POST refund-fund/deposits`: body `{ amount, evidenceRef }` (no `Idempotency-Key` header — avoid blind duplicate deposits); ensures/locks `RefundFundAccount.UserId`, verifies active, credits wallet (`RefundFundDeposit` tx + `AuditAction.BalanceAdjustment`).

---

## Withdrawals, Shadow Ledger & Reconciliation, and Internal Audit Sync

- **Withdrawal administration:** Documented end-to-end in [06 Withdrawals](06-withdrawals.md#admin--staff-processing-state-machine).
- **Shadow ledger & reconciliation (`ledger/*`, `reconciliation/*`):** Documented end-to-end in [09 Shadow ledger and reconciliation](09-shadow-ledger-and-reconciliation.md).
- **Internal Audit Log Sync ([WalletsController](../../Wallet.Api/Controllers/Public/WalletsController.cs#L585-L598), `[RequireInternalServiceKey]`):**
  - `GET ~/api/v1/internal/audit-logs/unsynced?limit=200` ([GetUnsyncedAuditLogsQueryHandler](../../Wallet.Application/Features/Audit/Queries/GetUnsyncedAuditLogs/GetUnsyncedAuditLogsQueryHandler.cs)): clamps `limit` to `1..500` (default `200`), returns oldest-first unsynced `AuditLogs` plus total `unsyncedCount`.
  - `POST ~/api/v1/internal/audit-logs/mark-synced` ([MarkAuditLogsSyncedCommandHandler](../../Wallet.Application/Features/Audit/Commands/MarkAuditLogsSynced/MarkAuditLogsSyncedCommandHandler.cs)): takes `{ ids }` (capped at `500` deduplicated GUIDs), marks matching unsynced logs `IsSynced = true` idempotently, and returns `{ updated }`.
