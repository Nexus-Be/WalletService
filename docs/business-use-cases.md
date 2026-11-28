# WalletService: business use cases and user stories

This guide documents what the current `WalletService` implementation does (71 MediatR use cases across `WalletsController` and `AdminWalletsController`, plus 3 hosted background workers), audited against the codebase on **26 November 2026**. Start here, then follow the numbered walkthroughs for request-to-response execution details.

## What the service does

`WalletService` owns wallet balances, transaction history, saved bank accounts, withdrawal requests, auction deposit holds/escrows, order-payment escrows, platform subsidy budgets/reservations, the post-payout refund fund ledger, Stripe payment attempt tracking, and an opt-in shadow ledger + reconciliation engine (`I1`–`I12`). It does not initiate bank payouts itself or own upstream Commerce/Auction state machines; Stripe handles external checkout sessions while `WalletService` verifies state directly with Stripe before crediting or finalizing.

| Term | Meaning |
|---|---|
| `Balance` | Total recorded money in a wallet (`WalletAccount.Balance >= 0`). |
| `Locked balance` / `heldBalance` | Money reserved for pending/approved/processing withdrawal requests and active `Held` auction holds (`0 <= LockedBalance <= Balance`). `heldBalance` in API responses equals total `LockedBalance`. |
| `Available balance` | `Balance - LockedBalance`; only this portion can be spent, transferred, withdrawn, locked for new holds, or recovered from a seller by the refund fund. |
| `Wallet transaction` | Immutable ledger row (`WalletTransaction`) capturing type, amount, `BalanceBefore`/`BalanceAfter`, `Status`, `ReferenceId`, and optional idempotency fingerprint. |
| `Order-payment escrow` (`OrderEscrow`) | Mutable escrow balance backing a buyer's wallet-funded order (`OriginalAmount`, `RemainingAmount`, `Status`: `Active`, `Settled`, `Refunded`, `Cancelled`). |
| `Auction deposit escrow` (`AuctionDepositEscrow`) | Separate escrow created per auction hold captured with `Purpose = ORDER_PAYMENT`; multiple deposit escrows under one `OrderId` form a FIFO pool for `AuctionPayout`. |
| `Order settlement allocation` (`OrderSettlement`) | Durable Settlement V2 allocation record per sub-order (`OrderPayout` or `AuctionPayout`) proving the identity `OrderEscrowAmount + DepositEscrowAmount + SubsidyAmount = SellerReceiveAmount + PlatformFeeAmount + ShippingAmount`. |
| `Platform subsidy budget & reservation` | Admin-approved promotional budget (`PlatformSubsidyBudget`, four-eyes approval) and per-sub-order hold (`PlatformSubsidyReservation`) funding `PlatformSubsidyCredit` outside order/deposit escrows. |
| `Refund fund` (`RefundFundAccount` / `RefundFundPayout`) | System wallet (`f0f0f0f0-0000-0000-0000-000000000001`, protected from freezing) funded by admins to reimburse buyers for already-settled wallet-paid sub-orders and recover available funds from sellers (`RefundFund:Enabled` default `false`). |
| `Shadow ledger` | Opt-in double-entry posting queue (`LedgerPostingPending`) and balanced journal (`JournalEntries`/`JournalLines`) written atomically via `WalletDbContext.SaveChangesAsync`. When enabled, any balance change without a matching `Success` `WalletTransaction` in the same save is refused. |

For example, a wallet with `Balance = 100` and `LockedBalance = 20` has `AvailableBalance = 80`. Locking `50` more keeps `Balance = 100`, raises `LockedBalance = 70`, and lowers `AvailableBalance = 30`. Capturing or completing the withdrawal deducts both `Balance` and `LockedBalance`; releasing/rejecting/cancelling reduces only `LockedBalance` and restores `AvailableBalance`.

## Actors and authorization boundaries

- **Wallet owner (`[RequireRole]`):** any authenticated user (`x-user-id` GUID) managing their own balance, transactions, Stripe top-ups, direct Stripe checkouts, wallet transfers, or viewing their own holds.
- **Seller (`[RequireRole("seller")]`):** manages saved bank accounts and requests/cancels withdrawals. Admins are also permitted (`IsAdmin()`); **`staff` is not admitted** on seller-only routes.
- **Staff (`[RequireRole("admin", "staff")]`):** inspects user wallets/holds/withdrawals, freezes/unfreezes non-system wallets, processes standard withdrawal transitions (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`), and views any user's direct Stripe checkout status.
- **Admin (`admin` / `administrator` + `EnsureAdmin`):** full control, including **admin-only** operations: `AdjustWalletBalance`, `ResolveFailed` withdrawal reversal, Stripe manual recovery/reconciliation, all Subsidy Budget operations (`Create`/`Approve`/`Close`/`List`), all Refund Fund operations (`Status`/`Deposit`), all Shadow Ledger & Reconciliation endpoints (`I1`–`I12`), and legacy ledger/snapshot reconciliation reports.
- **Auction caller (`[RequireAuctionServiceKey]`):** service-to-service caller using `X-Internal-Service-Key` for hold reservation, release, capture, captured-deposit refund, and hold lookup.
- **Commerce / internal caller (`[RequireInternalServiceKey]`):** service-to-service caller using `X-Internal-Service-Key` on `~/api/v1/internal/...` (plus 4 legacy `/api/v1/wallets/...` routes) for order payment, payment adjustment, escrow cancel, Settlement V1/V2, subsidy holds/releases, T30 refund-fund payouts, pre-payout refunds, purchase freeze/unfreeze, direct-order expiration, and audit-log sync.
- **Stripe:** external payment gateway hosting Checkout and sending signed webhooks (`POST /api/v1/wallets/stripe/webhooks`).

## Main business flows

| Business goal / use case | Detailed flow | Sequence diagram | Key boundary & status |
|---|---|---|---|
| Shared gateway headers, auth filters, release gates, ProblemDetails & workers | [00 Shared request path](business-flows/00-shared-request-path.md) | [Diagram](business-flows/00-shared-request-path.diagram.md) | `RequireRole`, `RequireInternalServiceKey`, `RequireAuctionServiceKey`, `ReleaseFeature`, PascalCase enums, 3 hosted workers. |
| View balance/history and manage saved bank accounts | [01 Wallet, history & bank accounts](business-flows/01-wallet-and-history.md) | [Diagram](business-flows/01-wallet-and-history.diagram.md) | `GET /me` auto-creates a VND wallet; internal/admin lookups use `AutoCreate = false`; saved bank accounts enforce at most one default via partial unique index. |
| Top up wallet balance with Stripe | [02 Stripe top-up](business-flows/02-stripe-topup.md) | [Diagram](business-flows/02-stripe-topup.diagram.md) | Two-phase DB-first prep (`10,000`–`99,999,999` VND), webhook + 1-min background recovery; active VNPAY & top-up cancel endpoints are absent. |
| Pay an order directly via Stripe (without wallet balance) | [03 Direct Stripe checkout](business-flows/03-direct-checkout.md) | [Diagram](business-flows/03-direct-checkout.diagram.md) | `1`–`99,999,999` VND; mutual exclusion with wallet `PurchaseDebit`; buyer cancel, internal expire, webhook & background polling. |
| **Pay, hold/adjust/cancel escrow, settle V1/V2, subsidize & refund orders (Escrow Deep Dive)** | [04 Wallet orders and escrow](business-flows/04-wallet-orders.md) | [Diagrams 4A & 4B](business-flows/04-wallet-orders.diagram.md) | Covers `OrderEscrow`, `AuctionDepositEscrow` pool, `AdjustOrderPayment`, `CancelOrderEscrow`, V2 `SettleOrderPayout`, Platform Subsidies, `RefundOrderPayment` (`OrderRefund` OFF by default everywhere), T30 `PayRefundFundCase` (`RefundFund:Enabled` default `false`), reconstruction, and global lock order. |
| Transfer available funds to another user | [05 Transfers](business-flows/05-transfers.md) | [Diagram](business-flows/05-transfers.diagram.md) | `1,000`–`100,000,000` VND; locks both wallets in ascending `UserId` order; both must be `Active`; optional `Idempotency-Key`. |
| Request, cancel, process & retry-notify bank withdrawals | [06 Withdrawals](business-flows/06-withdrawals.md) | [Diagram](business-flows/06-withdrawals.diagram.md) | Seller requests reserve `LockedBalance` (`WithdrawalsEnabled` gate); admin/staff drive `Pending → Approved → Processing → Completed/Failed` (`ResolveFailed` admin-only); 30s notification retry worker. |
| Reserve, release, capture & refund auction deposits | [07 Auction holds](business-flows/07-auction-holds.md) | [Diagram](business-flows/07-auction-holds.diagram.md) | `ORDER_PAYMENT` capture is gated by `AuctionOrderCaptureEnabled` and creates `AuctionDepositEscrow`; `FORFEIT` bypasses that gate; shared lock order prevents double-spend with `AuctionPayout`. |
| Admin adjustments, freeze guards, withdrawal/Stripe recovery, budgets, refund fund & audit sync | [08 Administration](business-flows/08-administration.md) | [Diagram](business-flows/08-administration.diagram.md) | Covers all 32 `/api/v1/admin/wallets` endpoints plus `~/api/v1/internal/audit-logs/*`; freezes block `RefundFundAccount`. |
| Shadow ledger chokepoint, journal worker & reconciliation engine (`I1`–`I12`) | [09 Shadow ledger & reconciliation](business-flows/09-shadow-ledger-and-reconciliation.md) | [Diagram](business-flows/09-shadow-ledger-and-reconciliation.diagram.md) | Real-time ledger guard on every `Balance` change, 2s `JournalPostingWorker`, backfill, trial balance, and full `I1`–`I12` reconciliation rules. |

## Feature gates & configuration highlights

- **Default enabled in `Development`, disabled elsewhere unless set `true`:** `WithdrawalsEnabled`, `AuctionOrderCaptureEnabled` (for `Purpose = ORDER_PAYMENT`), `LegacyCommerceCreditsEnabled`, `LegacyCommerceSettlementEnabled` (falls back to `LegacyCommerceCreditsEnabled`), `OrderSettlementV2Enabled`.
- **Disabled by default in ALL environments (including `Development`):**
  - `ReleaseFeatures:OrderRefundEnabled` (`false` unless explicitly configured `true`).
  - `RefundFund:Enabled` (`false` in `RefundFundOptions` unless configured `true`).
  - `LedgerSettings.Enabled` (DB toggle `PUT /api/v1/admin/wallets/ledger/shadow`, starts `false`).
- **Gateway routing & envelopes:** WalletService always returns direct DTOs (or RFC 9110 `application/problem+json` on errors). If traffic enters through an upstream API Gateway, headers (`x-user-id`, `x-user-roles`, etc.) and success response envelopes may be added/unwrapped at that boundary.
- **Core source entry points:** [WalletsController](../Wallet.Api/Controllers/Public/WalletsController.cs), [AdminWalletsController](../Wallet.Api/Controllers/Public/AdminWalletsController.cs), [WalletAccount](../Wallet.Domain/Entities/WalletAccount.cs), [Reconciliation catalog](RECONCILIATION_CATALOG.md), and [API index](api/README.md).
