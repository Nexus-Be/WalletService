# Pay, settle and refund wallet-funded orders

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

**Stories:** As a buyer, I want to pay with my wallet. As an order workflow,
I want to credit a seller or refund the buyer without spending the same
matching order funds twice.

> **Working-tree caveat:** The order-escrow implementation and migration were
> already uncommitted when this guide was written. Its own
> [contract](../api/checkout/order-escrow-settlement.md) marks it in progress
> with PostgreSQL verification pending. The descriptions below are of that
> working tree, not a claim that the changes are deployed or verified.

## Buyer pays from wallet

1. A caller acting for the buyer sends
   `POST /api/v1/wallets/orders/{orderId}/payment` with `{ "amount": ... }`.
   The controller gets the buyer ID from context. This route is hidden from
   Swagger; the source describes Commerce as the caller.
2. [PayOrderWithWalletCommandHandler](../../Wallet.Application/Features/Checkout/Commands/PayOrderWithWallet/PayOrderWithWalletCommandHandler.cs)
   validates the order and amount, locks the order and wallet, checks for
   conflicting direct or wallet payment, and deducts available funds.
3. The same transaction saves a successful `PurchaseDebit`, an `OrderEscrow`
   with original and remaining amounts, and an audit record. An equivalent
   prior debit is returned rather than debited again; incompatible payment
   returns `409 PAYMENT_ORDER_CONFLICT`. Insufficient available funds are
   rejected.
4. Wallet returns `200` with the transaction's `id`, `walletId`, `userId`,
   `type`, `amount`, `currency`, `balanceAfter`, `status`, `note`, `createdAt`.
   A Gateway-mediated request can wrap this DTO.

## Settle the seller

1. The Commerce-side caller sends
   `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` with `sellerId` and
   `amount`. `LegacyCommerceCredits` gates this action; when disabled it
   returns `503 FEATURE_DISABLED`. The controller does not itself require a
   user identity or the auction service key for this action.
2. [SettleSellerOrderCommandHandler](../../Wallet.Application/Features/Checkout/Commands/SettleSellerOrder/SettleSellerOrderCommandHandler.cs)
   locks by the supplied ID and seller wallet. For a matching payment/escrow
   ID, it checks remaining escrow, reduces it, credits the seller, and saves an
   `EscrowRelease` transaction and audit entry atomically.
3. Wallet returns the same transaction DTO shape with `200`. A matching
   existing settlement is replayed; a changed settlement conflicts. If the
   matched escrow lacks funds, the result is `409 ORDER_ESCROW_INSUFFICIENT`.

**Important legacy boundary:** If no matching payment exists for the supplied
child ID, the handler retains a compatibility path that credits the seller
without a matching `OrderEscrow`; it does not infer a parent order. Do not
describe all settlement calls as escrow-backed.

## Refund

1. The Commerce-side caller sends
   `POST /api/v1/wallets/orders/{orderId}/refund` with `userId`, `amount` and
   optional `reason`. This route is also subject to `LegacyCommerceCredits`;
   the controller does not itself require user identity or the auction key.
2. [RefundOrderPaymentCommandHandler](../../Wallet.Application/Features/Checkout/Commands/RefundOrderPayment/RefundOrderPaymentCommandHandler.cs)
   finds a completed wallet payment, checks the payer and refund amount, locks
   the wallet, checks remaining matching order escrow, credits the buyer, and
   stores `RefundCredit` and audit data in the transaction. An equivalent
   existing refund is returned; mismatches or insufficient escrow conflict.
3. Wallet returns `200` with the transaction DTO. A partial seller settlement
   and a refund can both succeed if the remaining escrow covers each.
4. `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` uses the same
   handler with `IsSubOrder`; its legacy child path does **not** locate a
   parent payment or require matching order escrow. It is separately gated by
   the same flag. Its caller supplies the buyer ID and amount.

Order escrow is separate from captured auction-deposit escrow; these
settlements/refunds do not consume the deposit. See
[auction deposit refund](07-auction-holds.md#refund-a-captured-order-deposit).
Safe reconstruction of older matching payment history, where possible, is
handled by [OrderEscrowAccounting](../../Wallet.Application/Features/Checkout/OrderEscrowAccounting.cs).
