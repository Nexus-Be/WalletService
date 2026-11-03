# WalletService idempotency key and retry contract

> **Status:** Draft proposal — not implemented and not yet approved.
> **Date:** 2026-11-03
> **Scope:** WalletService client-initiated financial mutations.

## 1. Goals

- Prevent a client retry after a timeout from creating a second financial operation.
- Return the original operation result when the same request is retried.
- Reject reuse of one key for a different request body.
- Preserve existing wallet balance invariants and transactional audit behavior.

This proposal is separate from provider callback replay handling. VNPay IPN delivery is deduplicated by the provider transaction reference, pending-state transition, and database row lock; provider callbacks do not send a client `Idempotency-Key`.

## 2. Proposed header and scope

Client-initiated mutation requests send:

```http
Idempotency-Key: 8e03978e-40d5-43e8-bc93-6894a57f9324
```

Proposal:

- Require a canonical UUIDv4 key for each new request; clients generate it before their first attempt.
- Scope uniqueness to `(authenticated user ID, operation name, key)`, where operation names are stable identifiers such as `wallet.topup.vnpay`, `wallet.transfer`, and `wallet.checkout.direct`.
- A key is not a credential and must not be used to authorize or identify a user.
- Apply this first to top-up, transfer, and direct checkout. Withdrawal request idempotency remains part of its Phase 2 contract; admin balance adjustments require a separate actor/scope decision.
- Store a normalized request fingerprint with the key. It includes operation/version and all client-controlled values that affect the financial operation; it excludes transport-only fields such as IP address and trace ID.

The exact operation set, UUID requirement, and scope require owner approval before implementation.

## 3. Proposed replay behavior

| Request condition | Proposed behavior |
|---|---|
| New key and valid request | Execute once; persist key, fingerprint, operation reference, HTTP status, and replayable response in the same consistency boundary as the operation |
| Same key and same fingerprint after completion | Return the original HTTP status and response body; do not repeat external or wallet side effects |
| Same key and different fingerprint | Return `409 Conflict` with `errorCode: IDEMPOTENCY_KEY_REUSED`; do not execute |
| Same key while the first request is in progress | Serialize on the durable key record. Wait for the first result; if the request cannot wait, return `409 Conflict` with `errorCode: IDEMPOTENCY_REQUEST_IN_PROGRESS` and `Retry-After: 1` |
| Client times out or receives a retryable transport/server failure | Retry with the identical key and identical payload; do not generate a new key |
| Client intentionally changes amount, recipient, order, or other operation inputs | Start a new logical request with a new key |

Validation failures that occur before any durable operation is created should not reserve the key. Once a key is reserved for an operation, its request fingerprint must not be changed.

## 4. External provider calls and uncertain outcomes

For operations that call a payment provider, persist an operation state before or atomically with dispatch, and pass a stable provider idempotency/reference value when the provider supports one. Do not retry a provider call with a new provider reference after an ambiguous timeout.

If the provider does not support idempotency and its outcome is unknown, the operation must enter a reconciliation-required state rather than automatically issuing a second charge. Client replay returns the same operation/reference; it does not trigger a fresh charge.

This proposal does not assert that a provider supports idempotency or define its retry policy.

## 5. Persistence, concurrency, and retention

- Enforce a unique database constraint on `(user ID, operation name, idempotency key)` so concurrent duplicate requests cannot both execute.
- Use a transaction/row lock or equivalent atomic insert-and-claim pattern; an application-only “check then insert” is insufficient.
- Link the durable key record to the resulting top-up, transfer, or checkout operation.
- Preserve the key/fingerprint association at least as long as the associated financial record exists. Do not expire pending or outcome-unknown keys automatically.
- Retain replay response data only as long as needed by the approved retry window and privacy policy; after removing a response body, retain a tombstone/key association to prevent a late replay from creating a duplicate.
- Select response retention, data minimization, encryption, and archival rules with the service owner before implementation.

## 6. Client retry guidance

1. Generate one key for a user action and persist it client-side until the operation reaches a known terminal result.
2. Send the exact same key and payload on retry after timeout, connection loss, or an ambiguous server error.
3. Treat `409 IDEMPOTENCY_KEY_REUSED` as a client defect; do not retry that key with modified parameters.
4. For `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`, wait at least the supplied `Retry-After`, then retry the same request.
5. Do not infer a failed payment solely from a timeout. Query the operation status where a status endpoint exists, or replay with the same key.
6. Generate a new key only for a genuinely new user action.

## 7. Open decisions before implementation

- Confirm supported endpoints and whether this applies to withdrawal requests in Phase 2.
- Confirm UUIDv4 versus another key format and maximum header length.
- Confirm whether same-key in-progress requests wait or receive a conflict response.
- Confirm HTTP response replay guarantees and how expired payment URLs are refreshed without creating a new operation.
- Confirm response-body retention and key tombstone retention requirements.
- Confirm provider-level idempotency capabilities and handling for ambiguous outcomes.
- Confirm whether administrators need a separate idempotency scope for wallet adjustments.

No endpoint, database schema, or runtime behavior is changed by this draft.
