# Wallet handover: cancelling an order with a pending direct payment

**Audience:** Commerce integration team  
**Date:** 2026-11-17  
**Status:** **Draft handover; cancellation endpoint not implemented.**

This document records the approved Wallet-side direction and the existing
direct-payment API. It is not a final wire contract or evidence that
cross-service cancellation works. Commerce behavior and deployment have not
been verified in this repository. Do not integrate against the proposed
endpoint until the request/response contract and implementation are released
and tested together.

## 1. Business scenario

A buyer creates an order, starts direct Stripe checkout and then asks to cancel
before payment is complete. Cancelling the order alone does not necessarily
make an already-issued Stripe Checkout URL unpayable. Payment can also succeed
while cancellation is in progress.

The approved desired outcomes are:

1. If checkout has not been paid, confirm with Stripe that it can no longer
   succeed before marking the Wallet cancellation complete.
2. If payment succeeds in the cancellation race, refund it to the original
   payment method and mark cancellation complete only after the refund succeeds.
3. If the provider or refund outcome is uncertain, keep cancellation pending;
   a timeout or browser redirect is not proof of payment or cancellation.
4. Equivalent retries must not produce duplicate refunds.

These are **requirements**, not behavior currently implemented by Wallet.
See the [draft feature specification](../../specs/006-cancel-pending-payment/spec.md)
and [Wallet plan](../../specs/006-cancel-pending-payment/plan.md).

## 2. Existing Wallet APIs

The following routes are implemented. They are separate from the proposed
cancellation API.

| Method and route | Purpose | Wallet success response |
|---|---|---|
| `POST /api/v1/wallets/checkout/direct` | Create or reuse an active Stripe checkout for an order. | `200` with `transactionId`, `orderId`, `amount`, `paymentUrl`, `status`, `createdAt`. |
| `GET /api/v1/wallets/checkout/direct/{orderId}` | Read the direct-payment ledger status. | `200` with `transactionId`, `orderId`, `amount`, `currency`, `status`, `gatewayTransactionNo`, `createdAt`, `updatedAt`. |
| `POST /api/v1/wallets/stripe/webhooks` | Receive and verify Stripe events. | Empty `200` when acknowledged; it is a provider callback, not an order-cancellation API. |

Direct checkout requires the authenticated owner, an existing VND wallet, a
nonempty order UUID and a whole-VND amount from 1 to 99,999,999. It does not
debit the wallet balance. An initial response is `Pending`; a return from
Stripe is not confirmation of payment.

The current status query is owner-scoped, with an admin/staff read override.
An unknown or non-owned payment returns `404 PAYMENT_NOT_FOUND`. A `404`
does **not** close the race with a concurrent checkout creation and is not
proof that a payment cannot later be created. The existing payment status
does not provide the proposed cancellation or refund-finality contract.

See [direct checkout](../api/checkout/direct.md),
[direct-payment status](../api/checkout/direct-status.md), and
[Stripe payments](../api/stripe/payments.md). The checked-in implementation
is test-mode oriented; real Stripe and Gateway integration are not verified.

## 3. Proposed cancellation API — not implemented

| Item | Approved direction |
|---|---|
| Method and Wallet route | `POST /api/v1/wallets/checkout/direct/{orderId}/cancel` |
| Identifier | The same nonempty order UUID used for direct checkout. |
| Caller | The authenticated payment owner. The mechanism by which a trusted caller supplies that owner's identity must be agreed and tested end to end. |
| Existing record | Required. Missing or non-owned direct payments are rejected; the operation does not create an order-wide cancellation marker. |
| Repeated request | An equivalent retry returns the current or final cancellation state without duplicating refunds. The exact retry protocol is not finalized. |
| Completion | Only after provider-confirmed inability to pay, or after a payment that raced cancellation has been successfully refunded. |
| Uncertainty | Remains pending; do not treat a timeout, lost response or unconfirmed refund as completed cancellation. |

**Not yet specified:** request body, exact JSON response fields and enum
values, HTTP statuses and error codes, idempotency headers, retry interval,
status polling mechanism, asynchronous completion notification, refund failure
handling and service-to-service identity propagation. These require a final
contract before implementation and client integration. In particular, do not
assume that `200`, `202`, `404`, `409` or `503` will have a specific meaning
for this proposed operation.

Wallet currently returns raw DTOs to direct callers. If a call is actually
proxied through the checked-in Gateway, its middleware normally wraps
nonempty successful JSON in an envelope. The eventual cancellation response
shape and routing still need to be verified in the target deployment.

## 4. Integration responsibilities to agree

The following is a **proposed coordination outline**, not a description of
current Commerce implementation:

1. On a buyer cancellation request, prevent new checkout initiation for the
   order at the ordering boundary. Coordinate in-flight initiation as well:
   a Wallet lookup that returns no payment cannot by itself prove that a
   concurrent initiation will not subsequently create one.
2. If there is an existing Wallet direct-payment attempt, invoke the proposed
   cancellation route with the payment owner's authorized identity after the
   API is implemented. The exact caller-to-Wallet identity contract is open.
3. Keep the order cancellation unresolved while Wallet is pending or its
   result is unknown. Use the eventual agreed retry and status contract;
   reconcile any lost response rather than assuming success or failure.
4. Treat the cancellation as financially complete only after Wallet reports
   confirmed closure or a completed refund under the final contract. Decide
   separately how and when the order state and buyer-facing message change.
5. Handle failures, retries, restarts and duplicate requests without starting a
   new checkout or issuing an independent duplicate refund.

**Before payment initiation:** Because the approved Wallet operation rejects
orders without an existing payment record, it cannot stop a later first
checkout for a cancelled order. This remains an ordering/integration
responsibility and must be designed together with in-flight initiation.

**Payment already completed:** The approved refund requirement addresses
payment racing a pending cancellation. The policy and API for cancellation
of an order already known to be paid before the request are not yet agreed.
Wallet's existing order wallet-balance refund route is not a Stripe refund and
must not be used as a substitute.

## 5. Acceptance scenarios for joint verification

Before enabling the integration, verify at least:

| Scenario | Expected business outcome |
|---|---|
| Cancellation before checkout creation | No payment is created after cancellation; caller handles the absence of a Wallet record and coordinates concurrent creation. |
| Existing open, unpaid checkout | Cancellation is not complete until the provider confirms the session cannot succeed. |
| Checkout creation races cancellation | No payable session is left behind after cancellation is reported complete. |
| Payment succeeds during cancellation | Exactly one provider refund is issued; cancellation remains unresolved until refund success. |
| Provider unavailable, timeout or lost response | Outcome remains pending/unknown; retry and recovery do not create a duplicate financial action. |
| Duplicate requests, callbacks and restart | The same final result is recovered; there is no duplicate refund. |
| Wrong owner or missing payment | Request is rejected; no other user's payment is changed and no cancellation marker is created. |
| Refund pending or failed | No successful cancellation is claimed solely because a refund was requested. |

## 6. Open handover items

- Finalize the wire contract and owner identity propagation, including
  machine-readable pending versus completed outcomes.
- Agree how order cancellation, checkout initiation and in-flight requests are
  coordinated across services without falsely treating a missing payment as
  finality.
- Agree retry, status visibility, operational reconciliation and treatment of
  permanently failed refunds.
- Implement and verify Wallet provider expiration, idempotent refund,
  persistence, webhook races and recovery.
- Implement the ordering-side call and state transitions, then run joint
  tests against the configured Gateway and Stripe test environment.

Until these items are complete, this handover must not be interpreted as a
working end-to-end cancellation guarantee.
