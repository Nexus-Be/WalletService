# WALLET SERVICE - POST /api/v1/wallets/vnpay/ipn

> **Feature / Use Case:** ProcessVnPayIpn
> **Status:** ✅ IMPLEMENTED
> **Date:** 2026-11-03
> **Scope:** WalletService only; wallet top-ups and direct checkout orders

---

## 1. Routing and authorization

- **HTTP method:** `POST`
- **Route:** `/api/v1/wallets/vnpay/ipn`
- **Authorization:** Anonymous provider callback; authenticity is established by VNPay HMAC validation, not forwarded user identity.
- **Controller:** `Wallet.Api/Controllers/Public/WalletsController.cs`
- **CQRS handler:** `Wallet.Application/Features/VnPay/Commands/ProcessVnPayIpn/ProcessVnPayIpnCommandHandler.cs`
- **Side effect:** A verified success notification transitions one pending top-up and credits its wallet once, or transitions one pending direct-order payment without changing wallet balance. The browser-return query never mutates payment state.

## 2. Input and validation

The endpoint accepts `application/x-www-form-urlencoded` and JSON object payloads. Parameter names and values are case-sensitive for signature construction. Duplicate parameter names are rejected during request parsing or fail signature validation.

| VNPay parameter | Required | Validation |
|---|---:|---|
| `vnp_SecureHash` | Yes | Hex-encoded HMAC-SHA512; compared in constant time |
| `vnp_SecureHashType` | Yes | Must be `HmacSHA512` (case-insensitive) |
| `vnp_TmnCode` | Yes | Must exactly match configured `VnPay:TmnCode` |
| `vnp_TxnRef` | Yes | Must identify an existing pending Wallet top-up or direct checkout order |
| `vnp_ResponseCode` | Yes | `00` means successful payment; another provider code marks the pending payment failed |
| `vnp_Amount` | Yes | Unsigned integer VND minor units; must equal the pending top-up or direct order amount multiplied by 100 |
| `vnp_CurrCode` | Yes | Must be `VND` |
| `vnp_TransactionNo` | No | Provider transaction identifier; never treated as proof without a valid signature |
| `vnp_BankCode` | No | Provider bank identifier; never treated as proof without a valid signature |

Signature input is the ordinally sorted parameter set excluding `vnp_SecureHash` and `vnp_SecureHashType`, URL-encoded and signed with the configured `VnPay:HashSecret`. Required configuration is supplied through WalletService configuration or environment variables; no secret is part of the API contract.

## 3. Response contract

The endpoint preserves the provider acknowledgement DTO and returns HTTP `200 OK`.

```json
{
  "responseCode": "00",
  "message": "Success"
}
```

| Response code | Meaning |
|---|---|
| `00` | Notification accepted and processed |
| `01` | Top-up, direct order, or associated wallet not found |
| `02` | Payment was already processed; no balance mutation occurs |
| `04` | Amount or currency is invalid |
| `97` | Signature, merchant code, or signature algorithm is invalid |
| `99` | Required parameters are missing or payload cannot be parsed |

No HTTP or JSON contract change is intended. Exact provider acknowledgement messages remain implementation details.

## 4. State and replay safety

The handler verifies authenticity and required payment fields before opening a database transaction. Within the transaction it first looks for a top-up reference and then a direct-payment reference. For a top-up it obtains the top-up row lock, confirms pending status and exact amount, and performs the top-up status transition, wallet credit, wallet transaction write, and audit write atomically. For direct checkout it locks the `DirectPaymentOrder`, confirms pending status and exact amount, and atomically updates the order, matching `DirectGatewayPayment` transaction, provider transaction number/payload, and audit log. Direct-order settlement does not change wallet balance.

A repeated notification sees a terminal record and returns `02`; it must not create another credit or settle the order again. The signed browser-return endpoint remains informational and is never authoritative for persisted payment state.

The database row lock is required for concurrent duplicate callbacks in PostgreSQL. In-memory tests can verify sequential duplicate behavior but cannot prove PostgreSQL lock semantics; a database-backed concurrency test remains recommended.

## 5. Error and persistence notes

- Invalid authenticity does not mutate the database.
- Invalid currency or amount does not transition the top-up or credit the wallet.
- A missing top-up, direct order, or required wallet does not create a wallet transaction.
- Direct-payment orders have a unique order ID and a fingerprint of their original checkout parameters. A retry of the same pending request reuses the same VNPay reference; changed parameters or a terminal payment are rejected by checkout.
- The existing CQRS handler and route are reused; no handler or controller is added.
- This callback hardening does not define client request idempotency, external payout, or bank reconciliation.

## 6. Pre-code implementation plan and checklist

1. Keep the existing IPN route, command, handler, transaction boundary, and response type.
2. Enforce the configured merchant code and supported HMAC algorithm in the shared VNPay signature validator.
3. Validate `vnp_CurrCode` and parse `vnp_Amount` as unsigned integer minor units before processing.
4. Settle either a pending top-up or pending direct checkout order under a PostgreSQL row lock; wallet balance changes only for the top-up path.
5. Add cryptographic validator tests and IPN behavior tests for malformed inputs and duplicate success delivery for both record types.
6. Update the checkout/IPN specifications and readiness checklist.

- [x] Pre-code specification written before C# changes.
- [x] Handler and signature-validator tests pass for top-up and direct checkout.
- [x] Architecture verification and Release build pass.
- [x] Full Release test suite passes.
- [x] GitNexus index refreshed and change analysis reviewed.
