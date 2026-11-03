# Wallet Service — POST /api/v1/wallets/checkout/direct

> **Feature / Use Case:** `CheckoutDirect`
> **Status:** ✅ IMPLEMENTED
> **Date:** 2026-11-03
> **Scope:** WalletService only

## 1. Routing and authorization

- **Method:** `POST`
- **Route:** `/api/v1/wallets/checkout/direct`
- **Authorization:** Existing `x-user-id` service-to-service identity contract; this change does not alter authorization.
- **Controller:** `Wallet.Api/Controllers/Public/WalletsController.cs`
- **Handler:** `Wallet.Application/Features/Checkout/Commands/CheckoutDirect/CheckoutDirectCommandHandler.cs`
- **VNPay settlement:** `POST /api/v1/wallets/vnpay/ipn`, handled by the existing `ProcessVnPayIpnCommandHandler`.
- **Balance behavior:** Direct merchant checkout is an external payment. It records payment state but does not debit or credit the wallet balance.

## 2. Request

The route remains unchanged. This endpoint uses VNPay exclusively. The controller supplies user ID and client IP; neither is accepted from the request body.

```json
{
  "orderId": "01000000-0000-0000-0000-000000000001",
  "amount": 850000.00,
  "bankCode": "NCB",
  "returnUrl": "https://shop.example/checkout/return"
}
```

| Field | Required | Rules |
|---|---:|---|
| `orderId` | Yes | Non-empty UUID |
| `amount` | Yes | Positive VND amount, at most two decimal places |
| `bankCode` | No | Optional provider bank code |
| `returnUrl` | Yes | Absolute HTTPS URL |

VNPay requests require a VND wallet and whole-VND amounts because VNPay accepts integer VND minor-unit values. Invalid currency/amount requests are rejected before creating a payment record.

## 3. Persistence, replay, and response

The existing `DirectPaymentOrder` row stores the stable VNPay reference, amount, status, provider transaction number, and callback payload. Its order ID is unique; the request fingerprint binds the pending payment intent to the wallet owner and client-controlled payment parameters. The existing `WalletTransaction` row is recorded as `DirectGatewayPayment` with `Pending` status and the order ID as `ReferenceId`.

- A matching retry while pending returns a new signed URL for the **same provider reference**, without creating another payment intent.
- Reuse with changed request parameters, a different wallet owner, or a terminal payment returns `409 Conflict` (`PAYMENT_ORDER_CONFLICT`).
- The database transaction commits the pending order and transaction before the URL is returned.

Success response remains HTTP `200 OK` with the existing DTO:

```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "01000000-0000-0000-0000-000000000001",
  "amount": 850000.00,
  "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
  "status": "Pending",
  "createdAt": "2026-11-03T03:30:00Z"
}
```

## 4. VNPay callback behavior

The existing IPN endpoint validates signature, merchant, currency, and amount. It looks up the pending top-up first, then the direct payment by `vnp_TxnRef`.

- A successful direct-payment IPN marks both `DirectPaymentOrder` and its `DirectGatewayPayment` transaction `Success`, records the provider transaction number/payload, and audits the transition.
- A provider-declined IPN marks both records `Failed`.
- Repeated delivery after a terminal transition returns the existing duplicate acknowledgement and never performs a second settlement.
- Unknown reference or mismatched amount/currency does not settle any record.
- No direct-payment IPN changes wallet balance. The signed browser-return endpoint remains informational and is not the settlement source of truth.

## 5. Error matrix

| HTTP | Error code | Meaning |
|---|---|---|
| `400` | `VAL_INVALID_INPUT` | Request validation failed |
| `400` | `VAL_INVALID_USER_ID` / `VAL_INVALID_ORDER_ID` | Invalid service identity or order UUID |
| `400` | `VAL_INVALID_AMOUNT` | Amount is not a valid positive whole-VND value |
| `400` | `VAL_INVALID_CURRENCY` | VNPay checkout requires a VND wallet |
| `400` | `VAL_INVALID_RETURN_URL` | VNPay return URL must be an absolute HTTPS URL |
| `404` | `WALLET_NOT_FOUND` | Wallet for the supplied user does not exist |
| `409` | `PAYMENT_ORDER_CONFLICT` | Order is reused with changed parameters, another owner, or terminal state |
| `502` | `PAYMENT_GATEWAY_UNAVAILABLE` | VNPay configuration is missing or invalid |

The IPN preserves the existing HTTP `200` provider acknowledgement DTO and response-code mapping.

## 6. Pre-code implementation plan

1. Reuse the existing `CheckoutDirectCommandHandler`, `ProcessVnPayIpnCommandHandler`, and `WalletsController`; do not add a handler/controller or change the CQRS count.
2. Extend `IVnPayService`/`VnPayService` with direct-order URL generation while preserving the existing top-up URL contract; use VNPay for every direct checkout.
3. Store the order's VNPay reference and request fingerprint with the pending order/transaction in one database transaction. Add a unique `OrderId` index through an additive migration.
4. Add a row-lock helper for direct orders. In the IPN transaction, settle direct orders exactly once and audit; preserve current top-up semantics.
5. Test URL signing/configuration, VND amount validation, retry and mismatch behavior, direct IPN success/failure/duplicate handling, and unchanged wallet balance.

## 7. Verification checklist

- [x] Pre-code specification written before C# changes.
- [x] Existing 11-handler/2-controller architecture preserved.
- [x] Migration and snapshot are synchronized.
- [x] Architecture verification succeeds.
- [x] Release build succeeds with zero errors.
- [x] Full Release test suite passes.
