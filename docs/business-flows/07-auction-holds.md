# Reserve, release, capture, and refund auction deposits

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md) | [Wallet orders & escrow](04-wallet-orders.md) | [Sequence diagram](07-auction-holds.diagram.md)

**Story:** As the Auction workflow and bidder, I need auction deposits reserved without deducting total balance until an auction settles—either releasing when outbid, forfeiting to seller/platform on default, or converting into `AuctionDepositEscrow` for `AuctionPayout` or buyer deposit refund.

## Authentication, service keys & routes

Every Auction write and hold-by-reference lookup is exposed on **both** `/api/v1/wallets/holds/...` and `~/api/v1/internal/wallets/holds/...` (`[ApiExplorerSettings(IgnoreApi = true)]`), protected by `[RequireAuctionServiceKey]` (`X-Internal-Service-Key` validated against `InternalSecurity:AuctionServiceKey` -> `InternalSecurity:ServiceKey` -> `INTERNAL_SERVICE_SECRET`; missing secret -> `500 INTERNAL_CONFIGURATION_ERROR`, wrong/missing header -> `401 UNAUTHORIZED`).

- All Auction mutations (`create`, `release`, `capture`, `refund-captured`) **require `Idempotency-Key`** and store deterministic `AuctionHoldOperations` responses with SHA-256 request fingerprints.
- Bidder self-service read: `GET /api/v1/wallets/me/holds?page=1&pageSize=10&status=...` (`[RequireRole]`).
- Admin/staff reads: `GET /api/v1/admin/wallets/holds` and `GET /api/v1/admin/wallets/{userId:guid}/holds` (`[RequireRole("admin", "staff")]`).
- Holds have **no automatic expiry**; the Auction service explicitly drives resolution.

## State machine & frozen-wallet rules

```mermaid
stateDiagram-v2
    [*] --> Held: "CreateHold (wallet Active & VND req -> LockFunds)"
    Held --> Released: "ReleaseHold (ReleaseLockedFunds; allowed if wallet frozen)"
    Held --> Captured: "CaptureHold FORFEIT (CompleteLockedWithdrawal -> Seller/Platform; no feature gate)"
    Held --> Captured: "CaptureHold ORDER_PAYMENT (AuctionOrderCapture gate -> AuctionDepositEscrow)"
    Captured --> Refunded: "RefundCapturedHold (ORDER_PAYMENT only; reduces DepositEscrow -> buyer Deposit)"
```

- **Frozen wallets:** `CreateHold` enforces `ACTIVE` status (`403 WALLET_FROZEN`). Once a hold exists, `ReleaseHold`, `CaptureHold` (`FORFEIT` or `ORDER_PAYMENT`), and `RefundCapturedHold` **intentionally allow frozen buyer wallets** so locked deposits can always be resolved cleanly.

---

## Operations

### 1. Reserve (`CreateHold`)
- **Routes:** `POST /api/v1/wallets/holds` | `POST ~/api/v1/internal/wallets/holds`
- **Body:** `ownerUserId`, `referenceType` (`AUCTION_DEPOSIT`), `referenceId`, positive whole-VND `amount`, `currency: VND`, optional JSON object `metadata`.
- **Handler ([CreateHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/CreateHold/CreateHoldCommandHandler.cs)):**
  - Acquires `LockAuctionKeyAsync("key:{key}")` -> replays same `Idempotency-Key` with live hold status (`200 OK`) or rejects conflicting payload (`409`).
  - Acquires `LockAuctionKeyAsync("reference:{hash(ReferenceType, ReferenceId)}")`: if a hold already exists for `(ReferenceType, ReferenceId)`, equivalent active details return the existing hold (`200 OK`) and link the new key; deleted/changed metadata/owner/amount returns `409 HOLD_REFERENCE_CONFLICT`.
  - Locks owner wallet (`404 WALLET_NOT_FOUND`), verifies `ACTIVE` (`403 WALLET_FROZEN`) and `Currency == Vnd`, calls `wallet.LockFunds(amount)` (`400 INSUFFICIENT_BALANCE` if `AvailableBalance < amount`), inserts `AuctionHold(Held)`, `AuctionHoldOperation`, and `AuditAction.AuctionHoldCreate`, commits (`201 Created` on first create, `200 OK` on replay).

### 2. Release (`ReleaseHold`)
- **Routes:** `POST /api/v1/wallets/holds/{holdId:guid}/release` | `POST ~/api/v1/internal/wallets/holds/{holdId:guid}/release`
- **Handler ([ReleaseHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/ReleaseHold/ReleaseHoldCommandHandler.cs)):**
  - Locks `"key:{key}"`, then `LockAuctionHoldAsync(holdId)` (`404 HOLD_NOT_FOUND`).
  - Already `Released`: replays if SHA-256 fingerprint matches, else `409 IDEMPOTENCY_CONFLICT`. Already `Captured`/`Refunded` -> `409 HOLD_ALREADY_CAPTURED`.
  - Locks owner wallet, calls `wallet.ReleaseLockedFunds(hold.Amount)` (`Balance` unchanged; `AvailableBalance` restored), transitions to `Released`, writes `AuditAction.AuctionHoldRelease`, returns `200`.

### 3. Capture (`CaptureHold`)
- **Routes:** `POST /api/v1/wallets/holds/{holdId:guid}/capture` | `POST ~/api/v1/internal/wallets/holds/{holdId:guid}/capture`
- **Feature gate rule ([ReleaseFeatureAttribute](../../Wallet.Api/Filters/ReleaseFeatureAttribute.cs)):** `ReleaseFeatures:AuctionOrderCaptureEnabled` (default open in `Development`, closed elsewhere -> `503 FEATURE_DISABLED`) is enforced **only when `Purpose == "ORDER_PAYMENT"`** (including retries). `Purpose == "FORFEIT"` bypasses that gate.
- **Handler ([CaptureHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/CaptureHold/CaptureHoldCommandHandler.cs)):**
  - **Shared lock order with escrow & settlement:** `"key:{key}"` -> (if `ORDER_PAYMENT` & `OrderId != null`) `LockOrderPaymentAsync(OrderId)` -> `LockAuctionHoldAsync(holdId)` -> wallets in ascending `UserId` order (`{owner, recipients}`).
  - Rejects already released/refunded holds (`409 HOLD_ALREADY_RELEASED`), mismatched transfer sums (`422 TRANSFERS_SUM_MISMATCH`), or buyer receiving forfeited funds (`400 VAL_INVALID_INPUT`). Already captured replays identical fingerprint (`200`) or rejects different request (`409 HOLD_ALREADY_CAPTURED_DIFFERENT`).
  - Deducts held funds via `buyer.CompleteLockedWithdrawal(hold.Amount)` (decrements both `LockedBalance` and `Balance`) and writes `AuctionHoldCapture` + `AuctionCaptureTransfer` rows + `AuctionHoldCapture` ledger tx:
    - **`FORFEIT`:** Credits specified recipient user wallets (`AuctionForfeitCredit`) and/or records platform transfers.
    - **`ORDER_PAYMENT`:** Requires `OrderId` and `SYSTEM_ESCROW` transfer; creates an **`AuctionDepositEscrow`** row (`OriginalAmount = RemainingAmount = hold.Amount`). Multiple holds captured for one `OrderId` (e.g., Buy Now deposit + goods hold) create multiple `AuctionDepositEscrow` rows forming a single FIFO pool consumed by [SettleOrderPayout](04-wallet-orders.md#5-stage-4--atomic-payout-protocol-v2-settleorderpayout--platform-subsidies).

### 4. Refund a captured order deposit (`RefundCapturedHold`)
- **Routes:** `POST /api/v1/wallets/holds/{holdId:guid}/refund-captured` | `POST ~/api/v1/internal/wallets/holds/{holdId:guid}/refund-captured`
- **Handler ([RefundCapturedHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/RefundCapturedHold/RefundCapturedHoldCommandHandler.cs)):**
  - **Shared lock order:** `"key:{key}"` -> `LockOrderPaymentAsync(OrderId)` -> `LockAuctionHoldAsync(holdId)` -> `LockDepositEscrowByHoldIdAsync(holdId)` -> `LockWalletByIdAsync(hold.WalletId)`.
  - Locking `OrderPaymentAsync(OrderId)` and the `AuctionDepositEscrow` row before checking `RemainingAmount` prevents a concurrent `SettleOrderPayout(AuctionPayout)` and `RefundCapturedHold` from spending the same deposit escrow twice.
  - Requires `hold.Status == Captured`, `capture.Purpose == "ORDER_PAYMENT"`, matching `OrderId` (`409 HOLD_NOT_REFUNDABLE` otherwise), and `escrow.RemainingAmount >= hold.Amount` (`409 ESCROW_INSUFFICIENT` otherwise).
  - Credits full `hold.Amount` back to buyer wallet (`AuctionDepositRefund`), reduces `escrow.RemainingAmount` to `0`, sets `hold.Status = Refunded`, records `AuditAction.AuctionHoldRefund`, returns `200`.

### 5. Outbound notifications
`CreateHold`, `ReleaseHold`, `CaptureHold` (both `"Captured"` and `"Forfeited"`), and `RefundCapturedHold` dispatch idempotent `AuctionDepositNotificationEvent` payloads (`sourceEventId: "hold:{holdId:D}:{Status}"`) via `TryDispatchAuctionDepositEventAsync` after commit ([NotificationDispatchExtensions](../../Wallet.Application/Common/Extensions/NotificationDispatchExtensions.cs)), both on initial execution and on replays.

### 6. Hold lookups
- `GET /api/v1/wallets/holds/{holdId:guid}` / `~/api/v1/internal/wallets/holds/{holdId:guid}` and `.../by-reference/{referenceType}/{referenceId}` ([GetHoldQueryHandler](../../Wallet.Application/Features/Holds/Queries/GetHold/GetHoldQueryHandler.cs)): service-key authenticated (`404 HOLD_NOT_FOUND`).
- `GET /api/v1/wallets/me/holds` ([ListMyHoldsQueryHandler](../../Wallet.Application/Features/Holds/Queries/ListMyHolds/ListMyHoldsQueryHandler.cs)): owner-authenticated paginated list extracting `auctionId` from `MetadataJson`.

See [auction deposits API contract](../api/holds/auction-deposits.md) and [RECONCILIATION_CATALOG.md](../RECONCILIATION_CATALOG.md) invariant `I6` for external Auction state reconciliation.
