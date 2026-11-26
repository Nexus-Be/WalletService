# Wallet Orders, Escrow Lifecycle, Settlements, Subsidies & Refunds — Sequence Diagrams

[Detailed walkthrough (Escrow Deep Dive)](04-wallet-orders.md) | [Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

Complete escrow lifecycle across all four mechanisms (`OrderEscrow`, `AuctionDepositEscrow` pool, `PlatformSubsidyBudget` / `Reservation`, and `RefundFundAccount` / `RefundFundPayout`), split into two diagrams for clarity:
- **Diagram 4A:** Order escrow funding, adjustment, cancellation, Platform Subsidy hold, Settlement V2 (`SettleOrderPayout`), and pre-payout refunds (`RefundOrderPayment`).
- **Diagram 4B:** Post-payout Dispute Refund Fund (`PayRefundFundCase` / T30 Phase 1) and best-effort seller recovery.

---

## Diagram 4A — Core Order Escrow, Adjustments, Cancellation, Settlement V2 & Pre-Payout Refunds

```mermaid
sequenceDiagram
    actor Commerce as Commerce / S2S Caller
    participant API as WalletsController / Escrow & Settlement Handlers
    participant Acc as OrderEscrowAccounting
    participant DB as PostgreSQL (Wallets / Escrows / Subsidies / Settlements)

    Note over Commerce,DB: == 1. Fund Order Escrow (PayOrderWithWallet) ==
    Commerce->>API: POST ~/internal/wallets/orders/{orderId}/payment (buyerId, amount)
    API->>DB: BeginTx -> LockOrderPaymentAsync(orderId) -> LockDirectOrder -> LockWallet(buyerId)
    API->>DB: Check mutual exclusion vs Direct Stripe Checkout / existing PurchaseDebit
    alt Direct Stripe Conflict or Mismatched Replay
        API-->>Commerce: 409 PAYMENT_ORDER_CONFLICT (or 409 ORDER_ESCROW_CANCELLED / RESOLVED)
    else Exact Replay
        API->>Acc: LoadOrReconcileAsync(orderId) -> ensure OrderEscrow materialized
        API-->>Commerce: 200 Existing PurchaseDebit response
    else Fresh Order Payment
        API->>DB: Check wallet Active (403) & AvailableBalance >= amount (400 INSUFFICIENT_BALANCE)
        DB->>DB: wallet.Deduct(amount) + PurchaseDebit(Success) + OrderEscrow(Active, Orig=Rem=amount)
        DB-->>API: Commit
        API-->>Commerce: 200 WalletOrderTransactionResponse
    end

    Note over Commerce,DB: == 2. Adjust Active Order Escrow (AdjustOrderPayment) ==
    Commerce->>API: POST ~/internal/wallets/orders/{orderId}/adjust-payment (Idempotency-Key, deltaAmount)
    API->>DB: BeginTx -> LockOrderPaymentAsync(orderId) -> check keyed idempotency
    API->>Acc: LoadOrReconcileAsync(orderId) -> LockWallet(buyerId)
    alt Delta > 0 (Additional Debit)
        DB->>DB: Enforce buyer Active & AvailableBalance >= delta -> deduct & increase Orig+Rem
        DB-->>API: OrderPaymentAdjustmentDebit + Audit(OrderPaymentAdjustment)
    else Delta < 0 (Partial Reduction - Frozen Buyer Allowed)
        DB->>DB: Require Rem >= abs(delta) & Orig - abs(delta) > 0 (409 ORDER_ESCROW_INSUFFICIENT)
        DB->>DB: Deposit buyer, decrease Orig+Rem (Settled/Refunded at 0) + OrderPaymentAdjustmentRefund
    end
    API-->>Commerce: 200 Adjustment response (or 409 IDEMPOTENCY_KEY_REUSED)

    Note over Commerce,DB: == 3. Pre-Settlement Cancellation (CancelOrderEscrow) ==
    Commerce->>API: POST ~/internal/wallets/orders/{orderId}/escrow/cancel (Idempotency-Key, reason)
    API->>DB: BeginTx -> LockOrderPaymentAsync(orderId) -> check key replay -> LockOrderEscrow -> LockWallet
    alt RemainingAmount != OriginalAmount
        API-->>Commerce: 409 ORDER_ESCROW_PARTIALLY_RESOLVED
    else Untouched Active Escrow (Frozen Buyer Allowed)
        DB->>DB: Credit full RemainingAmount (EscrowCancellationRefund), Rem=0, Status=Cancelled
        Note over API,DB: AuctionDepositEscrow on same OrderId stays untouched (must call RefundCapturedHold separately)
        DB-->>API: Commit
        API-->>Commerce: 200 CancelOrderEscrowResponse
    end

    Note over Commerce,DB: == 4. Platform Subsidy Hold & Atomic Settlement V2 (SettleOrderPayout) ==
    opt Voucher / Platform Subsidy Applies (ReleaseFeature OrderSettlementV2)
        Commerce->>API: POST ~/internal/wallets/sub-orders/{subOrderId}/subsidy-hold
        API->>DB: LockOrderPayment(subOrderId) -> Lock Active Budget FOR UPDATE -> check PerOrder/Daily/Total limits
        DB-->>API: Insert PlatformSubsidyReservation(Held) + increment budget.ReservedAmount
        API-->>Commerce: 200 SubsidyHoldResponse
    end

    Commerce->>API: POST ~/internal/wallets/order-settlements (BusinessType OrderPayout or AuctionPayout)
    API->>API: Validate OrderEscrow + DepositEscrow + Subsidy == SellerReceive + PlatformFee + Shipping
    API->>DB: BeginTx -> LockOrderPayment min(Parent,Sub) then max -> check replay / conflicts / V1 legacy
    alt Replay Matches Payload
        API-->>Commerce: 200 Replayed OrderSettlement (IsReplay = true)
    else Payload or Cross-Route Conflict
        DB->>DB: Persist AuditAction.SettlementConflict & COMMIT conflict audit
        API-->>Commerce: 409 SETTLEMENT_CONFLICT
    else Prior V1 EscrowRelease Exists on SubOrderId
        API-->>Commerce: 409 SETTLEMENT_ALREADY_PAID_LEGACY
    else Valid Settlement V2
        API->>Acc: LoadOrReconcile OrderEscrow (if >0) -> Lock DepositEscrows FIFO -> Lock Budget -> Lock SellerWallet
        DB->>DB: SaveChanges #1 -> EscrowRelease(SellerReceive - Subsidy), drain OrderEscrow & DepositEscrows FIFO
        opt SubsidyAmount > 0
            API->>API: Advance subsidyAt >= release.CreatedAt + 10us (I8 strict ordering)
            DB->>DB: SaveChanges #2 -> PlatformSubsidyCredit(Subsidy), Reservation=Used, Budget Reserved->Used
        end
        DB->>DB: Insert OrderSettlement (with PlatformFee & Shipping audit allocations) + commit
        Note over API,DB: Two SaveChanges calls inside 1 Tx record separate ORDER_ESCROW & PLATFORM_SUBSIDY ledger events
        API-->>Commerce: 200 OrderSettlementResponse
    end

    Note over Commerce,DB: == 5. Pre-Payout Refund (ReleaseFeature OrderRefund - Default OFF Everywhere) ==
    Commerce->>API: POST ~/internal/wallets/sub-orders/{subOrderId}/refund (ParentOrderId, amount, Idempotency-Key?)
    alt IsSubOrder and Missing ParentOrderId
        API-->>Commerce: 400 PARENT_ORDER_REQUIRED
    else Parent Provided
        API->>DB: LockOrderPayment min(Parent,Sub) then max -> Lock BuyerWallet (frozen buyer allowed)
        alt Same Key or Unkeyed Exact Replay
            API-->>Commerce: 200 Replayed RefundCredit (Replayed = true)
        else Different Key or Second Refund Attempt on Same SubOrderId
            API-->>Commerce: 409 REFUND_CONFLICT (Single refund per sub-order rule)
        else Eligible Pre-Payout Refund
            API->>Acc: Verify OrderEscrow RemainingAmount >= amount & reduce Remaining (Refunded at 0)
            DB->>DB: Credit buyer (RefundCredit) + AuditAction.OrderRefund + commit
            API-->>Commerce: 200 Refund response
        end
    end
```

---

## Diagram 4B — Post-Payout Refund Fund (T30 Phase 1) & Best-Effort Seller Recovery

```mermaid
sequenceDiagram
    actor Admin as Admin / Finance
    actor Commerce as Commerce Dispute Flow
    participant API as AdminWalletsController / PayRefundFundCase
    participant DB as PostgreSQL (RefundFund / Buyer / Seller / Payouts / Ledger)

    Note over Admin,DB: == 1. Admin Refund Fund Funding ==
    Admin->>API: POST /api/v1/admin/wallets/refund-fund/deposits (amount, evidenceRef)
    API->>DB: Ensure/Lock RefundFundAccount (f0f0f0f0...0001) -> verify Active -> deposit RefundFundDeposit
    API-->>Admin: 200 Updated Refund Fund balance

    Note over Admin,DB: == 2. Pre-Lock Policy, Threshold & Cap Checks ==
    Commerce->>API: POST ~/internal/wallets/refund-fund/sub-orders/{subOrderId}/payout
    alt RefundFund Enabled = false (Default Everywhere)
        API-->>Commerce: 503 REFUND_FUND_DISABLED
    else Exact Replay in RefundFundPayouts
        API-->>Commerce: 200 Refunded (Replayed = true, or 409 REFUND_FUND_CONFLICT on mismatch)
    else Amount > SecondApprovalThreshold (200k VND) Without Distinct SecondApproverId
        API-->>Commerce: 403 SECOND_APPROVAL_REQUIRED
    else Missing OrderPayout Settlement or Partial Amount Request
        API-->>Commerce: 409 SUB_ORDER_NOT_SETTLED or 400 PARTIAL_REFUND_NOT_SUPPORTED
    else Exceeds PerCaseCap (500k) or Pre-Lock DailyCap (5M Vietnam UTC+7 Day)
        API-->>Commerce: 200 NeedsFinance (PER_CASE_CAP_EXCEEDED / DAILY_CAP_EXCEEDED)
    else Pre-Lock Checks Pass
        Note over Admin,DB: == 3. Sorted Wallet Locks & Concurrency-Safe Re-Check ==
        API->>DB: BeginTx -> Lock {RefundFundAccount, Buyer, Seller} in ascending UserId order
        DB->>DB: Re-check RefundFundPayouts(SubOrderId), Vietnam DailyCap, & fund.AvailableBalance >= amount
        alt Concurrent Replay Won First
            DB-->>API: Existing payout row
            API-->>Commerce: 200 Refunded (Replayed = true)
        else Concurrent DailyCap Exhausted or Fund Low
            DB-->>API: Soft-stop condition
            API-->>Commerce: 200 NeedsFinance (DAILY_CAP_EXCEEDED / FUND_INSUFFICIENT)
        else Fund Frozen (Protected System Wallet) or Unpaid / Already Refunded
            API-->>Commerce: 403 WALLET_FROZEN or 409 NOT_PAID_BY_WALLET or 200 NeedsFinance
        else Cleared to Pay (Frozen Buyer Allowed)
            Note over Admin,DB: == 4. Two-Step Save - Buyer Payout & Seller Recovery ==
            DB->>DB: SaveChanges #1 at now -> fund.Deduct (RefundFundPayout) + buyer.Deposit (RefundFundRefund)
            Note over API,DB: Step A & Step B are split inside 1 Tx so Shadow Ledger records -Amount and +Recovery even on 100% recovery
            API->>API: target = min(amount, SellerReceiveAmount), recovery = Active seller available up to target
            alt Seller Is Active and AvailableBalance > 0
                API->>API: recoveryAt >= now + 10us (keeps I8 balance-chain timestamp ordering strict)
                DB->>DB: SaveChanges #2 at recoveryAt -> seller.Deduct (SellerRecoveryDebit) + fund.Deposit (RefundFundRecovery)
                DB->>DB: Note - Never touches seller LockedBalance or any OrderEscrow
            else Seller Frozen or AvailableBalance = 0
                API->>API: recovered = 0, full SellerShortfall recorded on RefundFundPayout
            end
            DB->>DB: Insert RefundFundPayout (target, recovered, shortfall, tx IDs) + AuditAction.OrderRefund & commit
            DB-->>API: Committed
            API-->>Commerce: 200 RefundFundCaseResult (Outcome = Refunded, recovered, shortfall)
        end
    end
```
