# Bank Withdrawals & Retry Notifications — Sequence Diagram

[Detailed walkthrough](06-withdrawals.md) | [Business overview](../business-use-cases.md) | [Bank accounts](01-wallet-and-history.diagram.md)

Complete seller withdrawal request and cancellation flow, staff/admin approval and completion state machine (`ResolveFailed` restricted to `admin` only), and immediate + 30-second exponential-backoff notification delivery.

```mermaid
sequenceDiagram
    actor Seller as Seller (RequireRole seller)
    actor Operator as Staff / Admin
    participant API as WalletsController / AdminWalletsController
    participant DB as PostgreSQL (Wallets / Withdrawals / AuditLogs)
    participant Worker as WithdrawalNotificationRetryWorker (30s)
    participant Notify as NotificationService HTTP

    Note over Seller,Notify: == 1. Seller Withdrawal Request & Cancel (RequireRole seller) ==
    Seller->>API: POST /api/v1/wallets/withdrawals (Idempotency-Key, amount 50k..500M, bankAccountId or manual)
    alt ReleaseFeatures WithdrawalsEnabled = false
        API-->>Seller: 503 FEATURE_DISABLED
    else Withdrawals Gate Open
        API->>DB: BeginTx -> LockWallet -> check Active VND & bank resolution -> check keyed SHA-256 fingerprint
        alt Idempotency Replay vs Mismatch
            API-->>Seller: 200 Replay (IsReplay = true) or 409 IDEMPOTENCY_KEY_REUSED
        else Fresh Request
            DB->>DB: wallet.LockFunds(amount) (LockedBalance += amount, Balance unchanged)
            DB->>DB: Insert WithdrawalRequest(Pending, Fee=0, Net=Amount) + Audit(WithdrawalRequest) & commit
            API-->>Seller: 201 Created (Pending)
        end
    end

    opt Seller Cancels While Still Pending (Frozen Seller Allowed)
        Seller->>API: POST /api/v1/wallets/withdrawals/{id}/cancel
        API->>DB: Lock Withdrawal -> Wallet -> verify Pending -> ReleaseLockedFunds(amount) -> Cancelled
        API-->>Seller: 200 Cancelled (or 409 WITHDRAWAL_INVALID_TRANSITION)
    end

    Note over Seller,Notify: == 2. Admin / Staff Lifecycle Processing ==
    Operator->>API: PATCH /api/v1/admin/wallets/withdrawals/{id} (action Approve / Reject / StartProcessing / Complete / Fail / ResolveFailed)
    alt Approve (Pending to Approved)
        API->>DB: Enforce wallet.EnsureActive() -> CompleteLockedWithdrawal(amount) (Balance & LockedBalance -= amount)
        DB->>DB: Insert WithdrawalDebit + optional unique TransferRef + Audit(NotificationQueued) & commit
    else Reject (Pending to Rejected - Frozen Allowed)
        API->>DB: wallet.ReleaseLockedFunds(amount) -> Rejected + Audit(NotificationQueued) & commit
    else StartProcessing (Approved to Processing)
        API->>DB: Transition Approved -> Processing & commit
    else Complete (Approved/Processing to Completed)
        API->>DB: Enforce wallet.EnsureActive() + TransferConfirmed + unique TransferRef -> Completed + NotificationQueued
    else Fail (Approved to Failed) or Admin-Only ResolveFailed (Processing to Failed)
        alt Staff Calls ResolveFailed
            API-->>Operator: 403 FORBIDDEN (EnsureAdmin required)
        else Authorized Admin / Staff Fail or Admin ResolveFailed
            API->>DB: Require AdminNote (and BankVerifiedNotTransferred + Evidence for ResolveFailed)
            DB->>DB: Deposit amount back (WithdrawalReversal, frozen wallet allowed) + clear GatewayRef + NotificationQueued & commit
        end
    end
    API-->>Operator: 200 Updated AdminWithdrawalResponse

    Note over Seller,Notify: == 3. Immediate Notification & 30-Second Exponential Backoff Retry ==
    API->>Notify: POST WithdrawalNotificationEvent (sourceEventId post-commit)
    alt Immediate Delivery Succeeds
        Notify-->>API: 2xx OK
        API->>DB: Record AuditAction.NotificationDelivered
    else Delivery Fails or Timeout
        Notify-->>API: Error / Unavailable
        loop Every 30 Seconds (MaxAttempts = 8, Delay = 30s then min(60m, 2^(k-1) min))
            Worker->>DB: Fetch up to 20 due NotificationQueued events lacking Delivered/Abandoned
            Worker->>Notify: Retry POST WithdrawalNotificationEvent
            alt Retry Succeeds
                Notify-->>Worker: 2xx OK
                Worker->>DB: Insert AuditAction.NotificationDelivered
            else 8 Attempts Exhausted
                Notify-->>Worker: Error
                Worker->>DB: Insert AuditAction.NotificationAbandoned
            end
        end
    end
```
