# View Wallet, History, and Saved Bank Accounts — Sequence Diagram

[Detailed walkthrough](01-wallet-and-history.md) | [Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

Covers owner wallet lookup with concurrency-safe auto-creation, read-only admin/internal wallet inspection (`AutoCreate = false`), paginated transaction history and gateway detail enrichment, and seller saved bank-account lifecycle (`IX_BankAccounts_UserId_Default`).

```mermaid
sequenceDiagram
    actor User as Wallet Owner / Seller
    actor S2S as Admin / Staff / Internal Caller
    participant API as WalletsController / AdminWalletsController
    participant DB as PostgreSQL (Wallets / Transactions / BankAccounts)

    Note over User,DB: == 1. Owner Wallet Lookup (AutoCreate = true) ==
    User->>API: GET /api/v1/wallets/me (RequireRole)
    API->>DB: Query Wallets by UserId (AsNoTracking)
    alt Wallet Exists
        DB-->>API: WalletAccount (Balance, LockedBalance, Status)
        API-->>User: 200 WalletResponse (heldBalance = lockedBalance)
    else Wallet Missing
        DB-->>API: Not found
        API->>DB: Dispatch EnsureWalletCommand + pg_advisory_xact_lock(wallet-create-userId)
        DB->>DB: Double-check Wallets & insert ACTIVE VND wallet + AuditAction.WalletCreate
        DB-->>API: Commit & return created WalletAccount
        API-->>User: 200 WalletResponse (Balance 0 VND, Active)
    end

    Note over User,DB: == 2. Admin / Internal Lookup (AutoCreate = false) ==
    S2S->>API: GET /admin/wallets/{userId} or ~/internal/wallets/{userId}
    API->>DB: Query Wallets by UserId (AsNoTracking, never auto-creates)
    alt Wallet Found
        DB-->>API: WalletAccount
        API-->>S2S: 200 WalletResponse
    else Wallet Missing
        DB-->>API: null
        API-->>S2S: 404 WALLET_NOT_FOUND
    end

    Note over User,DB: == 3. Transaction History & Gateway Detail ==
    User->>API: GET /api/v1/wallets/transactions?page=1&pageSize=10&type=...
    API->>API: Normalize fromDate/toDate to UTC & map enum/group filter
    API->>DB: Filter by Owner WalletId, CountAsync() + Skip/Take (CreatedAt DESC, Id DESC)
    DB-->>API: PagedData(WalletTransactionResponse)
    API-->>User: 200 Paginated transactions

    User->>API: GET /api/v1/wallets/transactions/{id}
    API->>DB: Load transaction scoped by WalletId + enrich gatewayStatus/Reference
    alt Owned by User
        DB-->>API: WalletTransaction + Topup/Direct/Attempt/Withdrawal/Hold link
        API-->>User: 200 WalletTransactionDetailResponse
    else Missing or Other User
        DB-->>API: null
        API-->>User: 404 TRANSACTION_NOT_FOUND
    end

    Note over User,DB: == 4. Seller Saved Bank Accounts (RequireRole seller - Staff Denied) ==
    User->>API: POST /api/v1/wallets/bank-accounts (CreateBankAccount)
    API->>DB: BeginTx + pg_advisory_xact_lock(bank-account-userId)
    DB->>DB: Check case-insensitive BankName + AccountNumber duplicate
    alt Duplicate Exists
        DB-->>API: Match found
        API-->>User: 409 BANK_ACCOUNT_DUPLICATE
    else Unique Account
        opt First Active Account or IsDefault = true
            DB->>DB: Clear prior IsDefault & flush SaveChanges (satisfies IX_BankAccounts_UserId_Default)
        end
        DB->>DB: Insert BankAccount + AuditAction.BankAccountChange (masked number) & commit
        DB-->>API: Saved BankAccount
        API-->>User: 201 Created (masked last 4 digits)
    end

    User->>API: DELETE /api/v1/wallets/bank-accounts/{id}
    API->>DB: BeginTx + pg_advisory_xact_lock(bank-account-userId) + check active withdrawals
    alt Pending / Approved / Processing Withdrawal References Account
        DB-->>API: Active withdrawal exists
        API-->>User: 409 BANK_ACCOUNT_IN_USE
    else Safe to Delete
        DB->>DB: Clear IsDefault (if default), soft-delete row, flush & promote newest active account
        DB->>DB: Write AuditAction.BankAccountChange & commit
        DB-->>API: Deleted
        API-->>User: 204 NoContent
    end
```
