# Wallet Service — API changes for the frontend

> Date: 2026-11-09. Base path: `/api/v1/wallets` (through the gateway). Identity is unchanged: `x-user-id` (and `x-user-roles` where noted).
> This note covers only what this branch adds on top of `dev`. The VNPay-only checkout, `409 PAYMENT_ORDER_CONFLICT`, and the withdrawal request flow already on `dev` are documented in [`docs/api/checkout/direct.md`](docs/api/checkout/direct.md) and [`docs/api/wallet-payments.md`](docs/api/wallet-payments.md).

## Summary

| # | Change | Type | Frontend action |
|---|--------|------|-----------------|
| 1 | New `GET /checkout/direct/{orderId}` | New endpoint | Poll it to learn the direct-payment result |
| 2 | Demo wallets are no longer created outside Development | Data | Don't rely on seeded users/balances outside local dev |

## 1. New: payment status — `GET /api/v1/wallets/checkout/direct/{orderId}`

Requires an authenticated user (`x-user-id`). Only the payer can read it; admin/staff (`x-user-roles`) can read any order.

```json
{
  "transactionId": "…",
  "orderId": "…",
  "amount": 80000.00,
  "currency": "Vnd",
  "status": "Pending",
  "gatewayTransactionNo": null,
  "createdAt": "2026-11-09T02:30:00+00:00",
  "updatedAt": "2026-11-09T02:30:00+00:00"
}
```

- `status`: `Pending` | `Success` | `Failed`. It changes only when VNPay's IPN settles the payment.
- `gatewayTransactionNo` is the VNPay transaction number once settled, otherwise `null`.
- Errors: **404** `PAYMENT_NOT_FOUND` (unknown order, or not yours), **400** `VAL_INVALID_INPUT` (invalid order id), **401** `UNAUTHORIZED` (no identity).

Recommended flow: `POST /checkout/direct` → redirect the buyer to `paymentUrl` → VNPay redirects back to `returnUrl` → poll this endpoint until `status` is no longer `Pending`. The browser return alone is not proof of payment; trust this status.

The wallet service does **not** expire pending payments; Commerce Service owns order expiry. The wallet balance never changes for a direct payment.

Details: [`docs/api/checkout/direct-status.md`](docs/api/checkout/direct-status.md).

## 2. Demo data

Demo wallets/balances (system, admin, staff, seller, buyer) are no longer part of the database migration. They are created only when the service starts with `ASPNETCORE_ENVIRONMENT=Development`. Databases that already contain them keep them. Any other environment starts with zero wallets; a wallet is created on first `GET /me`.

## Not changed

All other routes, request/response shapes and error codes.
