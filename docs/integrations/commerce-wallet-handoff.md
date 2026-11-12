# Wallet integration handoff

> This is a WalletService-side integration note, not a contract for Commerce or the API Gateway. Their clients, routing, and deployment behavior are outside this repository and have not been verified here.

## Verified WalletService behavior

- The implemented API routes and DTOs are documented in the [Wallet payments API](../api/wallet-payments.md).
- User routes resolve identity from claims or forwarded `x-user-id`; admin routes also use forwarded roles. The API host does not currently configure JWT authentication, and internal-service authentication middleware is disabled. Only trust forwarded identity from an authenticated gateway that overwrites client-supplied headers and protects the service network.
- `POST /api/v1/wallets/checkout/direct` creates an external payment intent. It does not debit or credit the buyer wallet.
- For Stripe, a matching retry reuses an active Checkout session. Order, owner and amount must match; an unresolved attempt blocks replacement until provider-confirmed finality.
- The signed Stripe webhook (`POST /api/v1/wallets/stripe/webhooks`) settles a matching direct order without changing wallet balance. Browser return is informational, not authoritative.
- WalletService does not currently expose refund, escrow, seller-payout, or bank-transfer execution endpoints. The withdrawal workflow reserves wallet funds and records staff approval/rejection; it does not initiate a bank payout.

## Integration checks outside this repository

Before enabling an end-to-end purchase flow, verify in the owning services and deployment:

1. The gateway authenticates the buyer, overwrites forwarded identity headers, and routes only trusted traffic to WalletService.
2. The Commerce order is associated with the returned Wallet `transactionId` and remains unpaid until the provider's signed webhook settles the direct payment.
3. Stripe test-mode configuration and the public webhook URL are correct, reachable, and tested.
4. Timeout/retry handling uses the same order and payment parameters while pending, then reconciles the authoritative Wallet/provider state before changing inventory or order state.
5. Refunds, escrow, and seller payouts are not assumed to be available until their contracts and implementations are agreed separately.

See the [synchronous integration specification](../../specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md) for WalletService's integration boundaries.
