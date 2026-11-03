# WalletService API documentation

## Implemented API

- [Wallet and payments API](wallet-payments.md) — route catalog and request/response behavior verified against this repository.
- [Direct checkout](checkout/direct.md) — VNPay and generic-gateway initiation, retry behavior, and payment state.
- [VNPay IPN](vnpay/ipn.md) — signature validation, top-up credit, direct-order settlement, and duplicate callbacks.

## Supporting documents

- [Service API document template](templates/service-api-doc-template.md) — pre-code contract template.
- [Idempotency key and retry proposal](proposals/idempotency-key-and-retry.md) — draft only; it is not implemented or approved.

The root [ecosystem API catalog](../../api/README.md) contains proposals for other services. Treat only the WalletService documents above as implementation evidence for this repository.
