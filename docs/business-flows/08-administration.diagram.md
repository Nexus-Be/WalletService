# Administration, Role Boundaries, Subsidies & Audit Sync — Sequence Diagram

[Detailed walkthrough](08-administration.md) | [Business overview](../business-use-cases.md) | [Withdrawals](06-withdrawals.diagram.md)

Role separation between `staff` (9 read/freeze/standard-withdrawal actions) and `admin` (23 admin-only financial, recovery, subsidy, refund-fund, and ledger operations), system-wallet freeze protection (`RefundFundAccount`), 4-eyes subsidy budget activation, and internal audit log sync.

```mermaid
sequenceDiagram
    actor Staff as Staff Operator
    actor Admin as Admin / Administrator
    actor S2S as Internal Sync / Commerce Caller
    participant API as AdminWalletsController / WalletsController
    participant DB as PostgreSQL (Wallets / Budgets / StripeAttempts / AuditLogs)

    Note over Staff,DB: == 1. Role Boundary Enforcement (RequireRole admin/staff + EnsureAdmin) ==
    Staff->>API: Call Admin-Only Action (e.g. AdjustBalance / ResolveFailed / Subsidy / RefundFund / Ledger / Stripe)
    API->>API: EnsureAdmin checks IsAdmin() (admin or administrator)
    API-->>Staff: 403 FORBIDDEN

    Note over Staff,DB: == 2. Wallet Freeze / Unfreeze & System Wallet Guard ==
    alt Admin/Staff Calls POST /admin/wallets/{userId}/freeze or S2S Internal Freeze
        API->>DB: WalletFreezeHelper checks target userId == RefundFundAccount.UserId (f0f0f0f0...0001)
        alt Target Is Protected Refund Fund Wallet
            API-->>Staff: 403 SYSTEM_WALLET_PROTECTED
        else Regular User Wallet
            DB->>DB: LockOrEnsureWallet -> set Status = FROZEN + insert AuditAction.WalletFrozen & commit
            API-->>Staff: 200 Frozen (idempotent if already frozen)
        end
    end

    Note over Staff,DB: == 3. Admin-Only Manual Adjustment & Stripe Recovery ==
    Admin->>API: POST /api/v1/admin/wallets/{userId}/adjustments (Credit/Debit, amount, reason, Idempotency-Key?)
    API->>DB: LockWallet -> verify key replay/conflict -> Credit or Debit AvailableBalance (frozen wallets allowed)
    DB->>DB: Insert AdjustmentCredit/Debit + AuditAction.BalanceAdjustment & commit
    API-->>Admin: 200 BalanceAdjustmentResponse (or 409 IDEMPOTENCY_KEY_REUSED)

    Admin->>API: POST /api/v1/admin/wallets/stripe/attempts/{attemptId}/resolve (Failed or Expired, reason)
    API->>DB: Lock Parent -> Attempt -> enforce ReconciliationRequired -> clear BlocksReplacement & mark Failed/Expired
    API-->>Admin: 200 Resolved (AuditAction.StripeAttemptResolve)

    Note over Staff,DB: == 4. Four-Eyes Platform Subsidy Budget Lifecycle ==
    Admin->>API: POST /api/v1/admin/wallets/subsidy-budgets (totalLimit, dailyLimit, perOrderCap, evidenceRef)
    API->>DB: Enforce PerOrderCap <= DailyLimit <= TotalLimit -> insert PendingApproval budget
    API-->>Admin: 201 Created (PendingApproval)

    Admin->>API: POST /api/v1/admin/wallets/subsidy-budgets/{id}/approve (Second Admin)
    API->>DB: BeginTx -> Lock advisory(subsidy-budget-activation) -> Lock Budget
    alt Same Admin as Creator or Another Active Budget Already Exists
        API-->>Admin: 403 SUBSIDY_APPROVER_MUST_DIFFER or 409 SUBSIDY_ACTIVE_BUDGET_EXISTS
    else Four-Eyes Verified & No Active Budget Conflict
        DB->>DB: Set Status = Active, ApprovedByAdminId = Approver & commit (replays identically for same approver)
        API-->>Admin: 200 Active
    end

    opt Close Active Budget & Inspect Refund Fund
        Admin->>API: POST .../subsidy-budgets/{id}/close and GET /admin/wallets/refund-fund
        API->>DB: Close budget to new holds (existing Held reservations remain usable) & read Vietnam-day fund stats
        API-->>Admin: 200 Budget Closed / RefundFund status
    end

    Note over Staff,DB: == 5. Internal Audit Log Incremental Sync ==
    S2S->>API: GET ~/api/v1/internal/audit-logs/unsynced?limit=200 (RequireInternalServiceKey)
    API->>DB: Query oldest-first IsSynced = false (limit 1..500) + total unsyncedCount
    API-->>S2S: 200 Audit log batch
    S2S->>API: POST ~/api/v1/internal/audit-logs/mark-synced (up to 500 GUIDs)
    API->>DB: Idempotently update matching AuditLogs IsSynced = true
    API-->>S2S: 200 Updated count
```
