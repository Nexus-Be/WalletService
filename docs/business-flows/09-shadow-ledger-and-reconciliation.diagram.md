# Shadow Ledger & Cross-Service Reconciliation (I1-I12) — Sequence Diagram

[Detailed walkthrough](09-shadow-ledger-and-reconciliation.md) | [Business overview](../business-use-cases.md) | [Reconciliation catalog](../RECONCILIATION_CATALOG.md)

Real-time `WalletDbContext.SaveChangesAsync` shadow-ledger chokepoint, 2-second balanced double-entry `JournalPostingWorker`, backfill/trial-balance controls, and snapshot reconciliation across `WalletService`, Commerce, and Auction (`I1`–`I12`).

```mermaid
sequenceDiagram
    actor Admin as Admin / Administrator
    participant API as AdminWalletsController / Money Handlers
    participant DbCtx as WalletDbContext (SaveChangesAsync)
    participant DB as PostgreSQL (Ledger / Journal / Recon)
    participant Worker as JournalPostingWorker (2s)
    participant Ext as Commerce & Auction APIs

    Note over Admin,Ext: == 1. Shadow Ledger Switch (PUT /api/v1/admin/wallets/ledger/shadow) ==
    Admin->>API: PUT /admin/wallets/ledger/shadow (enabled, expectedVersion)
    API->>DB: BeginTx -> Lock LedgerSettings(Id=1) FOR UPDATE (no wallet locks)
    alt Version Mismatch
        DB-->>API: Version conflict
        API-->>Admin: 409 LEDGER_SETTINGS_VERSION_CONFLICT
    else Version Matches
        DB->>DB: Toggle Enabled, bump ShadowEpoch (on enable) & Version + Audit(LedgerShadowSwitch)
        DB-->>API: Commit
        API-->>Admin: 200 ShadowLedgerState
    end

    Note over Admin,Ext: == 2. Real-Time Chokepoint on Every Balance Mutation ==
    API->>DbCtx: Wallet.Balance mutated inside active tx -> SaveChangesAsync()
    DbCtx->>DB: ReadLedgerSettingsAsync() takes FOR SHARE on LedgerSettings(Id=1) until commit
    alt LedgerSettings Enabled and Missing Added or Newly-Success WalletTransaction
        DbCtx-->>API: Throw InvalidOperationException & roll back tx (zero unbacked balance edits)
        API-->>Admin: 500 INTERNAL_SERVER_ERROR
    else Valid Mutation (Ledger Enabled)
        opt First Balance Change in New ShadowEpoch
            DbCtx->>DB: Set Wallet.LedgerEpoch = ShadowEpoch, LedgerSeq = 1, insert EpochOpening row
        end
        DbCtx->>DB: Increment LedgerSeq & insert LedgerPostingPending(Pending) atomically with Balance edit
        DB-->>API: Commit together
    end

    Note over Admin,Ext: == 3. Async Balanced Journal Posting (2-Second Worker) ==
    loop Every 2 Seconds (Drains Up to 200 Pending Rows)
        Worker->>DB: Read Pending or Failed (< 5 attempts) ordered (WalletId, LedgerEpoch, LedgerSeq)
        Worker->>Worker: JournalPoster.BuildEntry builds JournalEntry + balanced WALLET and Counter lines
        alt Posting Succeeds
            Worker->>DB: Persist JournalEntry (unique PendingId) + mark Pending Row = Posted
        else Posting Error
            Worker->>DB: Mark Pending Row = Failed, increment Attempts (max 5, balance unchanged)
        end
    end

    Note over Admin,Ext: == 4. Historical Backfill & Trial Balance ==
    Admin->>API: POST /admin/wallets/ledger/backfill (optional walletId)
    API->>DB: Backfill only wallets with unbroken chain from BalanceBefore = 0
    API-->>Admin: 200 Backfill summary (walletsBackfilled, walletsSkipped, entriesCreated)

    Admin->>API: GET /admin/wallets/ledger/trial-balance and GET /admin/wallets/ledger/status
    API->>DB: Sum Debit vs Credit per AccountCode & compute per-wallet I10 status
    API-->>Admin: 200 Trial balance (TotalDebit == TotalCredit) & ledger status

    Note over Admin,Ext: == 5. Cross-Service Reconciliation Engine (I1-I12) ==
    Admin->>API: POST /admin/wallets/reconciliation/runs (invariants I1..I12, fromDate, toDate)
    API->>DB: Open RepeatableRead snapshot tx + apply 30s SettleBuffer
    API->>Ext: HttpReconciliationSourceClient fetches Commerce orders & Auction deposits (20s timeout)
    alt Commerce or Auction Unreachable
        Ext-->>API: HTTP failure / timeout
        API->>DB: Record UnavailableSources + SkippedNote, set Status = Partial (no fabricated findings)
    else Sources Reachable
        Ext-->>API: Read-only Commerce & Auction snapshots
        API->>API: Evaluate I1..I12 (incl. I3/I4 AuctionPayout escrows & I5 CheckRefundFundAsync formula)
        API->>DB: Deduplicate findings by DedupKey = Invariant|ReferenceId|Code (keep Open/Resolved triage)
    end
    DB-->>API: Commit ReconciliationRun & Audit(ReconciliationRun)
    API-->>Admin: 200 ReconciliationRun summary (new FindingCount, Status)

    opt Admin Finding Triage
        Admin->>API: PATCH /admin/wallets/reconciliation/findings/{id} (Open/Investigating/Resolved/Accepted + note)
        API->>DB: Update status + Audit(ReconciliationFindingUpdate)
        API-->>Admin: 200 Updated finding (balances never altered)
    end
```
