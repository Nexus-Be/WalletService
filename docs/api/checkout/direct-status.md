# Wallet Service — GET /api/v1/wallets/checkout/direct/{orderId}

> **Feature / Use Case:** `GetDirectPaymentStatus`
> **Status:** ✅ IMPLEMENTED
> **Date:** 2026-11-09
> **Scope:** WalletService only

## 1. Routing and authorization

- **Method:** `GET`
- **Route:** `/api/v1/wallets/checkout/direct/{orderId}`
- **Authorization:** `[RequireRole]` with the existing `x-user-id` service-to-service identity contract (401 `UNAUTHORIZED` without it). The payer can read only their own payment. Callers with the `admin` or `staff` role (`x-user-roles`) can read any payment.
- **Controller:** `Wallet.Api/Controllers/Public/WalletsController.cs`
- **Handler:** `Wallet.Application/Features/Checkout/Queries/GetDirectPaymentStatus/GetDirectPaymentStatusQueryHandler.cs`
- **Read-only:** `AsNoTracking`; no state changes.

## 2. Purpose

[`POST /checkout/direct`](direct.md) returns a VNPay URL and a `Pending` payment. VNPay settles it through the IPN (`POST /vnpay/ipn`). This endpoint lets the caller (frontend or Commerce Service) poll the settled result. The browser return alone is informational; this status is the source of truth.

## 3. Response

```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "01000000-0000-0000-0000-000000000001",
  "amount": 850000.00,
  "currency": "Vnd",
  "status": "Pending",
  "gatewayTransactionNo": null,
  "createdAt": "2026-11-09T02:30:00+00:00",
  "updatedAt": "2026-11-09T02:30:00+00:00"
}
```

| Field | Notes |
|---|---|
| `status` | PascalCase `TransactionStatus`: `Pending`, `Success`, `Failed` |
| `gatewayTransactionNo` | VNPay transaction number, set once the IPN settles the payment; otherwise `null` |

`status` changes only through the VNPay IPN. Pending payments are not expired by the wallet service; expiry is owned by Commerce Service.

## 4. Error matrix

| HTTP | Error code | Meaning |
|---|---|---|
| `400` | `VAL_INVALID_INPUT` | `orderId` is not a valid non-empty UUID, or the caller identity is invalid |
| `401` | `UNAUTHORIZED` | No `x-user-id` identity |
| `404` | `PAYMENT_NOT_FOUND` | Unknown order, or the payment belongs to another user |

## 5. Architecture note

This adds one use case (`GetDirectPaymentStatusQuery`), moving the frozen handler count from 14 to 15. `AGENTS.md`, `.agents/`, the specs and `scripts/verify-wallet-architecture.sh` were updated in the same change. The controller count (2) is unchanged.

## 6. Verification

- Tests: `Wallet.Tests/DirectPaymentStatusTests.cs` (status progression Pending → Success/Failed after the IPN, ownership, admin/staff override, validation, controller identity handling).
- `./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c Release`, `dotnet test Wallet.sln -c Release`.
