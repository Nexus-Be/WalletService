# Commerce–Wallet integration handover

> **Audience:** Commerce implementers, testers and operators
>
> **Snapshot:** WalletService working tree, 17 November 2026
>
> **Readiness:** Integration handover, **not an end-to-end release sign-off**.

This is the single Wallet-side starting point for order payment, seller
settlement, refunds and cancellation. The Wallet source is authoritative for
what it implements; Commerce behavior, Gateway deployment configuration and
live Stripe integration have not been independently verified here.

## 1. What is available

| Capability | Wallet status | Important limitation |
|---|---|---|
| Pay an order from buyer wallet balance | Existing endpoint | Working-tree changes add order-payment escrow; those changes are uncommitted and PostgreSQL verification remains pending. |
| Initiate and query a direct Stripe order payment | Implemented for Stripe test mode | Asynchronous confirmation; no Wallet balance debit. Live/provider and Gateway acceptance remain unverified. |
| Credit seller or refund a wallet-funded order | Existing endpoints, release-gated | Matching-order escrow rules below describe **uncommitted work**; legacy child paths do not guarantee backing by the parent payment. |
| Cancel an existing pending direct payment | **Proposed, not implemented** | No finalized wire response, retry or polling contract; do not call the proposed route yet. |
| Refund a direct Stripe payment | **Not currently exposed** | Wallet-balance refund endpoints are not Stripe refunds. A refund for a payment racing cancellation is an approved future requirement. |
| Reserve, capture, release and refund auction deposits | Implemented as scoped auction-service operations | Separate from order-payment escrow. Order-purpose capture is release-gated. |

The order payment, settlement and refund routes exist in the controller but
are hidden from Swagger. Their existence does not establish that the caller,
network routing, production settings or cross-service business process is
wired correctly.

## 2. Boundary, identity and response format

- Wallet exposes REST JSON on port `8008` by default. Configure a service base
  URL appropriate to the deployment; do not assume `localhost` is reachable
  from another container.
- Wallet reads a valid `x-user-id` UUID from trusted server context/header.
  The Wallet host does not currently validate the bearer token itself.
  Confirm that the actual Gateway or trusted service-to-service boundary
  authenticates the user and safely propagates identity. The general
  internal-service authentication middleware is disabled.
- For wallet payment and direct checkout, the buyer identity comes from
  context, not a buyer ID in the body. `GET` direct status is available to
  the owner or admin/staff. The current settlement and order-refund
  controller actions do **not** themselves require buyer identity or a
  service key. Their callers and network access must be protected by the
  deployment; do not interpret this as authenticated Commerce access.
- Wallet returns **raw DTOs** directly to its caller. The checked-in Gateway
  wraps nonempty successful JSON in an envelope with `success`, `statusCode`,
  `message`, `data`, `traceId` and `timestamp`; it passes non-success and
  empty responses through. Actual routing and other Gateway middleware can
  affect the result. Do not expect a Gateway envelope on a direct Wallet call.
- Wallet failures normally use `application/problem+json` with `status`,
  `errorCode`, `detail`, `traceId` and, for validation, possibly `errors`.
  An HTTP or network timeout is an **unknown outcome**, not proof that no
  money moved. Preserve the correlation ID and reconcile before retrying.

## 3. Routes at a glance

All paths below are WalletService paths, not Commerce-facing order routes.
Examples in this document show **raw Wallet responses**, not Gateway envelopes.
UUID placeholders are nonempty UUIDs.

| Operation | Method and path | Success | Status / access |
|---|---|---:|---|
| Read own wallet / initialize if absent | `GET /api/v1/wallets/me` | `200` | Existing; authenticated identity. |
| Pay with wallet balance | `POST /api/v1/wallets/orders/{orderId}/payment` | `200` | Existing route; buyer identity; escrow change uncommitted. |
| Start direct Stripe payment | `POST /api/v1/wallets/checkout/direct` | `200` | Existing; buyer identity; test mode. |
| Read direct-payment status | `GET /api/v1/wallets/checkout/direct/{orderId}` | `200` | Existing; owner or admin/staff. |
| Seller wallet settlement | `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` | `200` | Existing; `LegacyCommerceCredits` gate; escrow change uncommitted. |
| Refund wallet-funded order | `POST /api/v1/wallets/orders/{orderId}/refund` | `200` | Existing; `OrderRefund` gate (off by default everywhere). |
| Refund sub-order | `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` | `200` | `OrderRefund` gate; body must include `parentOrderId` (else `400 PARENT_ORDER_REQUIRED`, nothing credited); the answer carries `replayed`. One refund per sub-order id. |
| Cancel direct payment | `POST /api/v1/wallets/checkout/direct/{orderId}/cancel` | **TBD** | Approved direction only; **not implemented**. |
| Stripe event callback | `POST /api/v1/wallets/stripe/webhooks` | Empty `200` | Existing; anonymous but Stripe signature verified. |

## 4. Pay with wallet balance

### Request

```http
POST /api/v1/wallets/orders/11111111-1111-1111-1111-111111111111/payment
x-user-id: 22222222-2222-2222-2222-222222222222
Content-Type: application/json

{ "amount": 100000 }
```

`amount` must be positive with at most two fractional digits. The buyer's
wallet must already exist and have enough **available** funds:
`balance - lockedBalance`. `GET /api/v1/wallets/me` can create an absent
zero-balance VND wallet; the payment operation itself does not create one.

### Response

```json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "walletId": "44444444-4444-4444-4444-444444444444",
  "userId": "22222222-2222-2222-2222-222222222222",
  "type": "PurchaseDebit",
  "amount": 100000,
  "currency": "Vnd",
  "balanceAfter": 50000,
  "status": "Success",
  "note": "Payment for order via wallet",
  "createdAt": "2026-11-17T08:00:00Z"
}
```

The handler uses the order ID as the business reference. An existing matching
wallet debit for the same wallet and amount is returned rather than debited
again. A conflicting direct payment, another wallet or different amount yields
`409 PAYMENT_ORDER_CONFLICT`. No caller-supplied idempotency header is used by
this Wallet action. Preserve order ID, buyer and amount on retries.

**Working-tree escrow:** The current uncommitted implementation creates an
`OrderEscrow` with the debit, transaction and audit entry in one transaction.
This is not yet a deployed guarantee. Earlier matching payments can be
reconstructed only when the history is unambiguous. See the
[in-progress escrow contract](../api/checkout/order-escrow-settlement.md).

## 5. Pay directly using Stripe

### Initiate

```http
POST /api/v1/wallets/checkout/direct
x-user-id: 22222222-2222-2222-2222-222222222222
Content-Type: application/json

{
  "orderId": "11111111-1111-1111-1111-111111111111",
  "amount": 100000
}
```

The amount must be a whole VND value between 1 and 99,999,999. The authenticated
buyer must already have a VND wallet, but its balance is not debited. Wallet
chooses the Stripe success/cancel URLs. The Wallet command accepts `orderId`
and `amount`; it does not use a client-supplied return URL or payment gateway
selector as part of this contract.

**`200` raw Wallet DTO:**

```json
{
  "transactionId": "55555555-5555-5555-5555-555555555555",
  "orderId": "11111111-1111-1111-1111-111111111111",
  "amount": 100000,
  "paymentUrl": "https://checkout.stripe.com/...",
  "status": "Pending",
  "createdAt": "2026-11-17T08:00:00Z"
}
```

The returned `transactionId` is the Wallet direct-payment ledger ID, not
proof that the order was paid. Use the same order, owner and amount on
retries: an eligible active attempt reuses its checkout URL. A changed
fingerprint or an unresolved attempt returns `409 PAYMENT_CONFLICT`.
Do not start a new charge merely because a URL expired or a request timed out.
The Wallet handler rejects a conflicting wallet-funded order payment.

### Confirm status

```http
GET /api/v1/wallets/checkout/direct/11111111-1111-1111-1111-111111111111
x-user-id: 22222222-2222-2222-2222-222222222222
```

The `200` DTO contains `transactionId`, `orderId`, `amount`, `currency`,
`status`, nullable `gatewayTransactionNo`, `createdAt` and `updatedAt`.
The ledger status can be `Pending`, `Success` or `Failed`; historical
`Cancelled` is also an existing transaction enum value, not evidence that
the proposed cancellation operation exists. The gateway transaction number
is a Stripe PaymentIntent ID when available.

Stripe sends a signed webhook to Wallet. Wallet checks the signature and
provider state before settling its own records. A successful browser redirect
is not sufficient to mark an order paid. Wallet has no verified outbound
order-paid callback to Commerce in this flow; decide and test how Commerce
obtains and processes authoritative payment status. The periodic Wallet worker
currently selects **top-ups**, not direct-order attempts, so do not assume it
automatically recovers a missed direct-payment webhook.

## 6. Seller settlement and wallet refunds

These are existing hidden routes. `ReleaseFeatures:LegacyCommerceCreditsEnabled`
controls all three; the default is enabled in Development and disabled
otherwise when unset. A disabled action returns `503 FEATURE_DISABLED`;
invalid flag configuration returns `500 INTERNAL_CONFIGURATION_ERROR`.
Do not enable these paths merely because the endpoints exist: caller
authorization, financial backing, and the complete integration need
verification.

### Credit a seller

```http
POST /api/v1/wallets/sub-orders/11111111-1111-1111-1111-111111111111/settle
Content-Type: application/json

{
  "sellerId": "66666666-6666-6666-6666-666666666666",
  "amount": 60000
}
```

The seller UUID and amount are required; amount must be positive with at most
two decimal places, and the seller wallet must exist. `200` returns the common
transaction DTO shown in section 4, with `type: "EscrowRelease"`. The Wallet
handler returns an existing settlement for the same reference, seller wallet
and amount; conflicting seller or amount returns `409 SETTLEMENT_CONFLICT`.

**Identifier and funding limitation:** In the uncommitted escrow work, the
handler looks for a wallet payment using the **exact ID in the settle route**.
Where it finds matching order escrow, it debits only remaining escrow and
rejects an excessive payout with `409 ORDER_ESCROW_INSUFFICIENT`. If no
payment matches that ID, the existing legacy compatibility path still credits
the seller without locating a parent order or consuming matching order escrow.
Do not assume arbitrary Commerce sub-order IDs are funded or safely mapped.
Agree parent/child allocation and funding before using this route.

### Refund a wallet-funded order

```http
POST /api/v1/wallets/orders/11111111-1111-1111-1111-111111111111/refund
Content-Type: application/json

{
  "userId": "22222222-2222-2222-2222-222222222222",
  "amount": 40000,
  "reason": "Order cancelled"
}
```

`userId` must match the payer; amount is positive with at most two fractional
digits; `reason` is optional and at most 500 characters. `200` returns the
common transaction DTO with `type: "RefundCredit"`. A completed
`PurchaseDebit` must exist; a direct Stripe payment does not qualify.

For the uncommitted matching-order escrow implementation, refund cannot exceed
the original payment or the remaining order escrow. One partial settlement and
one refund can both succeed if covered by what remains. A prior refund for the
same reference is returned when the amount matches; a different amount
conflicts. The handler's replay check does **not** compare the reason. Do not
assume support for multiple independent partial refunds on the same reference.

### Legacy sub-order refund

`POST /api/v1/wallets/sub-orders/{subOrderId}/refund` takes the same body and
returns the same DTO. It uses the supplied sub-order ID as its reference and
does **not** find or verify a parent order payment or draw down its escrow.
Its existing replay check compares the amount for the reference, not the
reason or recipient wallet. Do not treat this route as a safe substitute for
a parent-backed order refund; agree and verify a funding and ownership
contract first.

## 7. Cancelling an unpaid order

**Current state:** Wallet has no direct-payment cancellation endpoint and no
Stripe refund operation for this scenario. An already-issued checkout may
still be paid after an order is cancelled. Wallet settlement does not check the
ordering service's cancellation state. Do not claim a cross-service
cancellation is financially complete merely because the order state changed.

**Approved direction, not implemented:**

```http
POST /api/v1/wallets/checkout/direct/{orderId}/cancel
```

- Only the authenticated existing payment owner may request it. A missing or
  non-owned payment is rejected; no cancellation marker is created for an order
  with no Wallet payment record.
- Confirm with Stripe that an unpaid checkout cannot succeed before reporting
  cancellation complete. If payment succeeds during the race, refund the
  original payment method and complete cancellation only after refund success.
- If provider or refund status is uncertain, keep cancellation pending.
  Equivalent retries must not duplicate refunds.
- Body, exact status codes and error codes, response fields, status lookup,
  idempotency protocol and caller identity propagation are **not finalized**.

The ordering side must prevent a new checkout after cancellation and coordinate
any in-flight initiation. A no-record result cannot itself prove that
concurrent checkout creation will not later succeed. Agree what happens to an
already-paid order, a permanently failed refund, and a lost cancellation
response. The detailed scenario is in the
[cancellation draft](pending-payment-cancellation-handover.md).

## 8. Auction-order deposit boundary

An auction deposit held or captured in Wallet is **not** the same money as an
order's wallet-payment escrow. The current uncommitted order-escrow changes
do not use captured deposit escrow to fund a seller settlement or order refund.
Refunding an eligible captured order deposit uses its separate hold operation.

Auction service hold operations require the dedicated configured service key;
mutations also require `Idempotency-Key`. `ORDER_PAYMENT` capture is disabled
by default outside Development via
`ReleaseFeatures:AuctionOrderCaptureEnabled`. Do not assume the ordering
caller can invoke these routes or that the deposit is automatically released
or refunded on an order cancellation. Coordinate ownership and state with the
auction workflow. See [auction deposits](../api/holds/auction-deposits.md).

## 9. Error and retry guidance

The exact error may depend on the route and whether validation happens before
the handler. This table covers the principal **existing Wallet** outcomes.
It is not a contract for the proposed cancellation endpoint.

| HTTP / code | Typical condition | Caller action |
|---|---|---|
| `400 VAL_INVALID_INPUT` | Invalid UUID, amount or request fields. | Correct the input; do not blindly retry. |
| `400 INSUFFICIENT_BALANCE` | Not enough available wallet funds. | Do not mark the wallet payment successful. |
| `401 UNAUTHORIZED` | Required user identity missing or invalid. | Correct identity propagation. |
| `404 WALLET_NOT_FOUND` | Required buyer or seller wallet absent. | Resolve the missing wallet; do not assume a payment occurred. |
| `404 PAYMENT_NOT_FOUND` | Missing/non-owned direct payment, or no completed wallet debit for order refund. | Reconcile the correct operation and identity; a missing payment is not a cancellation barrier. |
| `409 PAYMENT_CONFLICT` | Direct checkout has conflicting input or unresolved prior attempt. | Reconcile the existing attempt; do not create an alternate charge. |
| `409 PAYMENT_ORDER_CONFLICT` | Wallet debit conflicts with another order payment. | Reconcile by order and original payment method. |
| `409 SETTLEMENT_CONFLICT`, `REFUND_CONFLICT` | An existing operation conflicts with the supplied request. | Investigate stored reference, amount and recipient. |
| `409 REFUND_EXCEEDS_PAYMENT` | Order refund exceeds original wallet payment. | Correct the requested amount. |
| `409 ORDER_ESCROW_INSUFFICIENT`, `ORDER_ESCROW_UNAVAILABLE` | Uncommitted escrow work finds insufficient or unrecoverable matching funds. | Do not create a replacement credit; reconcile payment and payout history. |
| `503 FEATURE_DISABLED` | Relevant release flag is disabled. | Leave operation unresolved; coordinate release readiness. |
| `503 PAYMENT_PROVIDER_UNAVAILABLE` | Stripe operation failed or timed out. | Treat financial outcome as uncertain; reconcile with original order/attempt. |
| `500 INTERNAL_SERVER_ERROR` | Unexpected Wallet failure. | Record `traceId`, reconcile, and escalate; do not assume rollback of an external provider action. |

## 10. Joint integration checklist

- [ ] Confirm deployed Wallet version, flags, route visibility and identity
      propagation; do not rely on uncommitted escrow code as deployed.
- [ ] Agree owner and service authorization, especially for seller credits
      and refunds, and restrict trusted network access.
- [ ] Verify raw versus Gateway-enveloped success and Problem Details errors.
- [ ] Verify wallet-funded payment, insufficient available balance, same-order
      retries, conflicting payment methods and lost responses.
- [ ] Agree order/sub-order ID mapping and funding for seller settlement and
      partial or full refunds; test payout/refund concurrency and duplicate
      operations against a disposable PostgreSQL environment.
- [ ] Verify Stripe test-mode checkout, signed webhook delivery, polling,
      duplicate/out-of-order callbacks and delayed or missed webhook recovery.
- [ ] Finalize and implement the cancellation wire contract on both sides;
      test cancellation before checkout, during creation, after checkout,
      concurrent payment, refund failure, provider timeout and restart.
- [ ] Agree handling for direct Stripe refunds, disputes and cancellations of
      already-paid orders; do not substitute a wallet-balance credit.
- [ ] Test auction deposit lifecycle separately from order payment and make
      each service's responsibility explicit.

## 11. Wallet sources

- [Wallet controller](../../Wallet.Api/Controllers/Public/WalletsController.cs)
  and [release feature filter](../../Wallet.Api/Filters/ReleaseFeatureAttribute.cs).
- [Wallet order payment handler](../../Wallet.Application/Features/Checkout/Commands/PayOrderWithWallet/PayOrderWithWalletCommandHandler.cs),
  [settlement handler](../../Wallet.Application/Features/Checkout/Commands/SettleSellerOrder/SettleSellerOrderCommandHandler.cs),
  and [refund handler](../../Wallet.Application/Features/Checkout/Commands/RefundOrderPayment/RefundOrderPaymentCommandHandler.cs).
- [Direct Stripe contract](../api/stripe/payments.md),
  [escrow work in progress](../api/checkout/order-escrow-settlement.md),
  and [cancellation specification](../../specs/006-cancel-pending-payment/spec.md).
