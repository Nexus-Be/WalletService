# Wallet integration handoff

> This is a WalletService-side integration note, not a contract for Commerce or the API Gateway. Their clients, routing, and deployment behavior are outside this repository and have not been verified here.

## Verified WalletService behavior

- The implemented API routes and DTOs are documented in the [Wallet payments API](../api/wallet-payments.md).
- User routes resolve identity from claims or forwarded `x-user-id`; admin routes also use forwarded roles. The API host does not currently configure JWT authentication, and internal-service authentication middleware is disabled. Only trust forwarded identity from an authenticated gateway that overwrites client-supplied headers and protects the service network.
- `POST /api/v1/wallets/checkout/direct` creates an external payment intent. It does not debit or credit the buyer wallet.
- For VNPay, a matching retry while the order is pending reuses the provider reference. The order, owner, amount, gateway, bank code, and return URL must match; changed parameters or a terminal payment conflict. This is not a general `Idempotency-Key` contract and has no documented TTL.
- The signed VNPay IPN callback (`GET /api/v1/wallets/vnpay/ipn`) settles a matching pending direct order without changing wallet balance. Browser return is informational, not authoritative.
- WalletService does not currently expose refund, escrow, seller-payout, or bank-transfer execution endpoints. The withdrawal workflow reserves wallet funds and records staff approval/rejection; it does not initiate a bank payout.

## Integration checks outside this repository

Before enabling an end-to-end purchase flow, verify in the owning services and deployment:

1. The gateway authenticates the buyer, overwrites forwarded identity headers, and routes only trusted traffic to WalletService.
2. The Commerce order is associated with the returned Wallet `transactionId` and remains unpaid until the provider's signed IPN settles the direct payment.
3. The VNPay merchant configuration and public IPN URL are correct, reachable, and tested against the provider sandbox.
4. Timeout/retry handling uses the same order and payment parameters while pending, then reconciles the authoritative Wallet/provider state before changing inventory or order state.
5. Refunds, escrow, and seller payouts are not assumed to be available until their contracts and implementations are agreed separately.

See the [synchronous integration specification](../../specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md) for WalletService's integration boundaries.
