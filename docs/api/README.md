# WalletService API documentation

## Implemented API

- [Wallet and payments API](wallet-payments.md) — route catalog and request/response behavior verified against this repository.
- [Direct checkout](checkout/direct.md) — Stripe checkout initiation, retry behavior, and payment state.
- [Direct payment status](checkout/direct-status.md) — polling endpoint for the settled payment result.
- [Cancel order escrow](checkout/cancel-order-escrow.md) — internal S2S strict all-or-nothing cancellation of an untouched wallet-payment `OrderEscrow`.
- [Stripe payment contracts](stripe/payments.md) — top-up, direct checkout and signed webhook contracts.
- [Stripe top-up recovery](stripe/topup-recovery.md) — history, resume and reconciliation.
- [Auction deposit holds](holds/auction-deposits.md) — reserve, release, capture, refund and status.
- [Auction integration request](holds/auction-integration-request.md) — questions and proposed reconciliation for Auction.
- [Frontend and gateway handover](stripe/api-handover.md) — test-mode integration requirements and remaining gates.

## Supporting documents

- [Service API document template](templates/service-api-doc-template.md) — pre-code contract template.
- [Idempotency key and retry proposal](proposals/idempotency-key-and-retry.md) — draft only; it is not implemented or approved.

The root [ecosystem API catalog](../../api/README.md) contains proposals for other services. Treat only the WalletService documents above as implementation evidence for this repository.
