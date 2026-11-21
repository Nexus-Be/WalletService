# Order payout protocol V2 and platform subsidy budget

Internal service routes (service key required, not exposed by the gateway). The V2 payout and the subsidy hold are behind
`ReleaseFeatures:OrderSettlementV2Enabled` (default on in Development, off elsewhere). Releasing a subsidy is a recovery action and is not gated.

## POST /api/v1/internal/wallets/order-settlements

One call pays one sub-order (or one auction order) completely and atomically.

| Field | Meaning |
|---|---|
| `businessType` | `OrderPayout` (regular order) or `AuctionPayout` (uses the deposit escrow too) |
| `parentOrderId`, `subOrderId` | Escrow key and payout key. For an auction order both are the auction order id |
| `sellerId` | Seller user id |
| `sellerReceiveAmount` | Total credited to the seller |
| `platformFeeAmount`, `shippingAmount` | Allocated in the settlement record. They change no wallet balance |
| `subsidyAmount` | Platform-funded part of a voucher discount, paid from the reserved budget (must be reserved first) |
| `orderEscrowAmount`, `depositEscrowAmount` | What leaves each escrow |
| `payloadVersion` | Version of the payload shape (part of the fingerprint) |

Identity (otherwise 400 `SETTLEMENT_IDENTITY_INVALID`):
`orderEscrowAmount + depositEscrowAmount + subsidyAmount = sellerReceiveAmount + platformFeeAmount + shippingAmount`.

The seller credit is an `EscrowRelease` transaction for `sellerReceiveAmount - subsidyAmount` plus a `PlatformSubsidyCredit`
transaction for `subsidyAmount`. Both reference the sub-order id.

Idempotency: key is (`businessType`, `parentOrderId`, `subOrderId`). Same key and same payload fingerprint returns the first result with
`isReplay = true` and moves no money. Same key with a different payload, or the same sub-order under another parent order, returns 409
`SETTLEMENT_CONFLICT`, keeps money and the first result, and writes an audit entry (`SettlementConflict`). A sub-order already paid by the
legacy route returns 409 `SETTLEMENT_ALREADY_PAID_LEGACY` and is never paid again.

Other errors: 404 `PAYMENT_NOT_FOUND`, 404 `DEPOSIT_ESCROW_NOT_FOUND`, 409 `ORDER_ESCROW_INSUFFICIENT`, 409 `DEPOSIT_ESCROW_INSUFFICIENT`,
409 `ORDER_ESCROW_CANCELLED`, 409 `ORDER_ESCROW_UNAVAILABLE`, 409 `SUBSIDY_NOT_RESERVED`.

Lock order shared by every path that can spend an order or deposit escrow: idempotency key, order payment lock(s) by ascending id, auction hold,
escrow rows, wallets, subsidy budget.

## POST /api/v1/internal/wallets/sub-orders/{subOrderId}/subsidy-hold

Body `{ "orderId", "amount" }`. Reserves the subsidy for one sub-order (one reservation per sub-order, idempotent). 409 codes:
`SUBSIDY_BUDGET_UNAVAILABLE`, `SUBSIDY_PER_ORDER_CAP_EXCEEDED`, `SUBSIDY_DAILY_LIMIT_EXCEEDED`, `SUBSIDY_BUDGET_EXHAUSTED`,
`SUBSIDY_CONFLICT`, `SUBSIDY_ALREADY_RELEASED`.

## POST /api/v1/internal/wallets/sub-orders/{subOrderId}/subsidy-release

Body `{ "reason" }`. Gives a held subsidy back. Repeating it, or releasing a sub-order that never reserved anything, changes nothing
(`status` `Released` or `None`). A subsidy already used by a payout returns 409 `SUBSIDY_ALREADY_USED`.

## Admin: /api/v1/admin/wallets/subsidy-budgets (admin role only)

* `GET` lists budgets with reserved, used and available amounts.
* `POST` proposes a budget (`name`, `totalLimit`, `dailyLimit`, `perOrderCap`, `evidenceRef`). It stays `PendingApproval` and cannot be used.
* `POST /{id}/approve` approves it. The approver must differ from the creator (403 `SUBSIDY_APPROVER_MUST_DIFFER`); only one budget can be active
  (409 `SUBSIDY_ACTIVE_BUDGET_EXISTS`).
* `POST /{id}/close` stops new reservations. Held reservations can still be used or released.

Limits: `perOrderCap <= dailyLimit <= totalLimit`; reserved plus used never exceeds the total limit (also enforced by a database check).
With no approved budget no subsidy can be reserved.
