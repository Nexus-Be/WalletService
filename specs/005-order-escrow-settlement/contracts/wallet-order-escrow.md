# Wallet Order Escrow Contract

This feature changes Wallet accounting behavior only. Routes, JSON request and
response shapes, and status-code contracts remain unchanged.

## Existing operations

| Operation | Route | Reference |
|---|---|---|
| Buyer payment | `POST /api/v1/wallets/orders/{orderId}/payment` | `orderId` |
| Seller payout | `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` | `subOrderId`; for same-ID auction orders this is the funded order ID |
| Order refund | `POST /api/v1/wallets/orders/{orderId}/refund` | `orderId` |
| Deposit capture | `POST /api/v1/wallets/holds/{holdId}/capture` | Captured `ORDER_PAYMENT` includes `orderId` |
| Deposit refund | `POST /api/v1/wallets/holds/{holdId}/refund-captured` | `holdId` plus the original `orderId` |

## Accounting behavior

- Payment creates a wallet-payment escrow record in the same transaction as the buyer
  debit and payment transaction.
- Seller payout uses the supplied `sellerId` and `amount`; Wallet performs no
  commission calculation. It consumes only matching remaining wallet-payment
  escrow, then credits the seller. It never uses or changes captured-deposit
  escrow.
- A seller payout greater than remaining wallet-payment escrow fails even when a
  captured deposit exists.
- Order refund is limited to remaining wallet-payment escrow and never draws from
  captured deposit escrow.
- Captured-deposit refund remains a separate hold operation and can still
  refund its full original amount after seller settlement.
- Unspent escrow remains associated with the order. Wallet does not infer a
  platform-fee or shipping recipient.
- Retries use the existing stable IDs. One seller payout and one refund may
  both succeed if each is covered by the remaining escrow. Exact retries
  return the original transaction; changed retries or amounts exceeding the
  remaining escrow fail without another movement.

## Existing errors

Existing HTTP status codes and existing error codes remain unchanged. The new
wallet-payment funding check uses a specific code so it cannot be confused with
the existing captured-deposit failure. Existing codes include
`PAYMENT_ORDER_CONFLICT`, `PAYMENT_NOT_FOUND`, `REFUND_CONFLICT`,
`REFUND_EXCEEDS_PAYMENT`, `SETTLEMENT_CONFLICT`, and `FEATURE_DISABLED`.
The existing `ESCROW_INSUFFICIENT` remains reserved for captured-deposit
refunds; order payment escrow uses `ORDER_ESCROW_INSUFFICIENT`.

## Deployment

Existing order-credit and order-payment-capture release gates remain unchanged.
This feature does not enable any operation outside its current environment
configuration.
