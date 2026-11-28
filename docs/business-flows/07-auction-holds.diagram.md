# Auction Deposit Holds, Capture & Refund Race Protection — Sequence Diagram

[Detailed walkthrough](07-auction-holds.md) | [Business overview](../business-use-cases.md) | [Wallet orders & escrow](04-wallet-orders.diagram.md)

Idempotent Auction hold operations authenticated via `[RequireAuctionServiceKey]` on `/api/v1/wallets/holds/...` and `~/api/v1/internal/wallets/holds/...`, highlighting shared lock ordering between `RefundCapturedHold` and `SettleOrderPayout(AuctionPayout)`.

```mermaid
sequenceDiagram
    actor Auction as Auction Service
    participant API as WalletsController / Hold Handlers
    participant DB as PostgreSQL (Holds / AuctionDepositEscrow / Wallets)
    participant Notify as NotificationService HTTP

    Note over Auction,Notify: == 1. Reserve Deposit Hold (CreateHold) ==
    Auction->>API: POST ~/internal/wallets/holds (X-Internal-Service-Key, Idempotency-Key, referenceId, amount)
    API->>DB: BeginTx -> LockAuctionKey(key) -> LockAuctionKey(referenceType+referenceId)
    alt Idempotency-Key or Active Reference Replay
        DB-->>API: Existing AuctionHoldOperation / AuctionHold
        API-->>Auction: 200 Existing Hold (or 409 HOLD_REFERENCE_CONFLICT on mismatch)
    else Fresh Hold
        API->>DB: LockWallet(ownerUserId) -> verify Active VND & AvailableBalance >= amount
        DB->>DB: wallet.LockFunds(amount) + insert AuctionHold(Held) + HoldOperation + Audit(AuctionHoldCreate) & commit
        API-->>Auction: 201 Created (Status = Held)
    end

    Note over Auction,Notify: == 2. Release Hold (Outbid / Cancelled - Frozen Wallet Allowed) ==
    Auction->>API: POST ~/internal/wallets/holds/{holdId}/release (Idempotency-Key)
    API->>DB: BeginTx -> LockAuctionKey(key) -> LockAuctionHold(holdId) -> LockWallet
    alt Already Captured or Refunded
        API-->>Auction: 409 HOLD_ALREADY_CAPTURED
    else Held or Matching Replay
        DB->>DB: wallet.ReleaseLockedFunds(amount) -> Hold=Released + Audit(AuctionHoldRelease) & commit
        API-->>Auction: 200 Released
    end

    Note over Auction,Notify: == 3. Capture Hold (FORFEIT vs ORDER_PAYMENT) ==
    Auction->>API: POST ~/internal/wallets/holds/{holdId}/capture (Idempotency-Key, purpose, orderId?, transfers)
    alt Purpose = ORDER_PAYMENT and ReleaseFeatures AuctionOrderCaptureEnabled = false
        API-->>Auction: 503 FEATURE_DISABLED (FORFEIT bypasses this gate)
    else Allowed Purpose
        API->>DB: LockAuctionKey(key) -> (if ORDER_PAYMENT) LockOrderPayment(OrderId) -> LockAuctionHold -> Lock Wallets Asc
        DB->>DB: CompleteLockedWithdrawal(hold.Amount) + insert AuctionHoldCapture (Balanced=Frozen allowed)
        alt Purpose = FORFEIT
            DB->>DB: Credit recipient seller wallet (AuctionForfeitCredit) & record platform fee split
        else Purpose = ORDER_PAYMENT
            DB->>DB: Create AuctionDepositEscrow (Original=Remaining=hold.Amount, FIFO pool for OrderId)
        end
        DB-->>API: Commit
        API-->>Auction: 200 Captured
    end

    Note over Auction,Notify: == 4. Refund Captured Deposit vs AuctionPayout Concurrency ==
    Auction->>API: POST ~/internal/wallets/holds/{holdId}/refund-captured (Idempotency-Key, orderId)
    API->>DB: Lock key -> LockOrderPayment(OrderId) -> LockHold -> LockDepositEscrow(HoldId) -> LockBuyerWallet
    Note over API,DB: Shared OrderId + DepositEscrow lock prevents race with SettleOrderPayout(AuctionPayout)
    alt AuctionPayout Already Drained DepositEscrow
        DB-->>API: escrow.RemainingAmount < hold.Amount
        API-->>Auction: 409 ESCROW_INSUFFICIENT
    else Full Deposit Escrow Still Intact
        DB->>DB: buyer.Deposit(hold.Amount) (AuctionDepositRefund), DepositEscrow.Remaining=0, Hold=Refunded
        DB-->>API: Commit
        API-->>Auction: 200 Refunded
    end

    Note over Auction,Notify: == 5. Post-Commit Hold Notification ==
    opt Every Committed or Replayed Hold Mutation
        API->>Notify: TryDispatchAuctionDepositEventAsync (sourceEventId hold-holdId-Status)
        Notify-->>API: Best-effort non-blocking HTTP response
    end
```
