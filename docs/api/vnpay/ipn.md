# WALLET SERVICE - GET /api/v1/wallets/vnpay/ipn

> **Feature / Use Case:** ProcessVnPayIpn
> **Status:** ✅ IMPLEMENTED
> **Date:** 2026-11-03
> **Scope:** WalletService only; wallet top-ups and direct checkout orders

---

## 1. Routing and authorization

- **HTTP method:** `GET` (what VNPay uses, with the parameters in the query string). `POST` with `application/x-www-form-urlencoded` or a JSON object is also accepted.
- **Route:** `/api/v1/wallets/vnpay/ipn`
- **Register with VNPay:** the merchant IPN URL must be the public **HTTPS** URL of this route (VNPay requires SSL), e.g. `https://<public-host>/api/v1/wallets/vnpay/ipn`. Through the gateway this is the `GET` route listed in `specs/06_SYNCHRONOUS_SERVICE_INTEGRATION.md`.
- **Authorization:** Anonymous provider callback; authenticity is established by VNPay HMAC validation, not forwarded user identity.
- **Controller:** `Wallet.Api/Controllers/Public/WalletsController.cs`
- **CQRS handler:** `Wallet.Application/Features/VnPay/Commands/ProcessVnPayIpn/ProcessVnPayIpnCommandHandler.cs`
- **Side effect:** A verified success notification transitions one pending top-up and credits its wallet once, or transitions one pending direct-order payment without changing wallet balance. The browser-return query never mutates payment state.

## 2. Input and validation

The endpoint accepts the query string of a `GET` (VNPay), `application/x-www-form-urlencoded`, or a JSON object. Parameter names and values are case-sensitive for signature construction. A repeated parameter name (including names that differ only by case) is rejected with `99` instead of being merged.

| VNPay parameter | Required | Validation |
|---|---:|---|
| `vnp_SecureHash` | Yes | Hex-encoded HMAC-SHA512; compared in constant time |
| `vnp_SecureHashType` | Yes | Must be `HmacSHA512` (case-insensitive) |
| `vnp_TmnCode` | Yes | Must exactly match configured `VnPay:TmnCode` |
| `vnp_TxnRef` | Yes | Must identify an existing pending Wallet top-up or direct checkout order |
| `vnp_ResponseCode` | Yes | Together with `vnp_TransactionStatus` decides the outcome (see below) |
| `vnp_TransactionStatus` | No | When present it must also be `00` for a successful payment (VNPay's own sample requires both) |
| `vnp_Amount` | Yes | Unsigned integer VND minor units; must equal the pending top-up or direct order amount multiplied by 100 |
| `vnp_CurrCode` | Yes | Must be `VND` |
| `vnp_TransactionNo` | No | Provider transaction identifier; never treated as proof without a valid signature |
| `vnp_BankCode` | No | Provider bank identifier; never treated as proof without a valid signature |

Signature input is the ordinally sorted parameter set excluding `vnp_SecureHash` and `vnp_SecureHashType`, URL-encoded and signed with the configured `VnPay:HashSecret`. Required configuration is supplied through WalletService configuration or environment variables; no secret is part of the API contract.

A payment is **successful** only when `vnp_ResponseCode` is `00` and, if `vnp_TransactionStatus` is present, it is also `00`. Any other combination marks the pending payment failed (and the callback is still acknowledged with `00`, because the merchant did update the payment).

## 3. Response contract

The endpoint returns HTTP `200 OK` with the JSON object that VNPay requires (`RspCode` and `Message`, PascalCase):

```json
{
  "RspCode": "00",
  "Message": "Confirm Success"
}
```

| `RspCode` | `Message` | Meaning | VNPay retries? |
|---|---|---|---|
| `00` | `Confirm Success` | Notification accepted and processed (including a declined payment recorded as failed) | No |
| `01` | `Order not found` / … | Top-up, direct order, or associated wallet not found | Yes |
| `02` | `Order already confirmed` | Payment was already processed; no balance mutation occurs | No |
| `04` | `Invalid amount or currency` | Amount or currency is invalid | Yes |
| `97` | `Invalid signature` | Signature, merchant code, or signature algorithm is invalid | Yes |
| `99` | `Missing parameters` / `Invalid request payload` | Required parameters are missing, duplicated, or the payload cannot be parsed | Yes |

Per VNPay, `00` and `02` end the notification flow; any other code (or a timeout) makes VNPay retry up to 10 times, 5 minutes apart.

## 3a. Callback log

Every callback that reaches the handler is stored in `VnpayIpnLogs` (reference, response code, transaction number, caller address, raw payload as `jsonb`, and a processing status: `Processed`, `Duplicate`, `InvalidSignature`, `OrderNotFound`, `InvalidAmount`, `SystemError`). It is written in a separate step after the settlement transaction, so rejected callbacks (forged signature, unknown order) are kept too. Size is bounded: at most 50 parameters, values truncated to 500 characters, reference to 100. A failure to write the log never changes the acknowledgement. Requests rejected before the handler (duplicate parameter names, unparseable body) are not logged.

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
