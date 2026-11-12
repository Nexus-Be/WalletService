# Data model: Stripe migration

## Payment attempt

- UUID primary key; purpose: top-up or direct order; owner ID; non-null amount
  as decimal(18,2) constrained to positive whole VND; immutable fingerprint.
- Status: creation pending, awaiting payment, processing, successful, failed,
  expired, reconciliation required. Define allowed transitions explicitly.
- Unique idempotency key; nullable unique Checkout Session ID; nullable unique
  PaymentIntent ID; session URL and expiry; creation/update timestamps.
- Foreign keys identify exactly one wallet top-up or direct payment order.
  Enforce one active attempt per business operation with database constraints
  and transaction/row locks. Multiple historical attempts are allowed.
- A new direct attempt is allowed only after the prior provider payment is
  confirmed incapable of later success. Unexpected paid duplicates must be
  persisted as reconciliation-required and surfaced to operators.
- Within EF, purpose, owner, parent, amount, fingerprint and idempotency keys
  are initialization-only and configured to reject updates after insertion.
  Raw SQL and bulk updates bypass that guard and must not modify these fields.

### Attempt transitions (proposed test-mode design)

| From | To | Evidence or action |
|---|---|---|
| CreationPending | AwaitingPayment | Session ID and URL are durably bound. |
| CreationPending or AwaitingPayment | Processing | A verified completion reports an unpaid delayed payment. |
| CreationPending, AwaitingPayment or Processing | Successful | Verified provider data confirms payment and all immutable references, amount and currency match. |
| CreationPending, AwaitingPayment or Processing | Failed or Expired | Provider evidence confirms failure or expiry; this alone does not necessarily authorize a replacement. |
| Any non-success state | ReconciliationRequired | An ambiguous provider outcome, conflicting reference or unexpected paid attempt requires investigation. |

`Successful` never regresses. Older pending, failure or expiry events cannot
undo a successful settlement. If a valid success arrives after a local
failure or expiry, retrieve authoritative provider state and reconcile it:
settle only if the business operation is still unsettled; otherwise record
the additional charge for manual resolution. Never automatically refund it.
Do not treat a timeout, browser cancellation or local session expiration as
proof that a payment cannot still succeed.

Replacement eligibility is a separate, persisted provider-confirmation
decision, not merely a `Failed` or `Expired` status. An attempt awaiting
confirmation remains blocking, including when its idempotency key may have
expired. Never create a replacement because Stripe's idempotency retention
window has elapsed.

The standalone reconciliation policy compares the provider snapshots to the
attempt's ID, amount and any bound session and PaymentIntent IDs. It classifies
a payment as confirmed only when a complete, paid session has a matching
succeeded intent with the full amount received. It permits a provider-final
classification only for an expired unpaid session without an intent, or an
expired/complete unpaid session whose matching intent is canceled with nothing
received, provided recovery is explicitly disabled. Unknown or enabled
recovery remains blocking. Other states remain pending; conflicting data
requires investigation.
These classifications do not themselves change a database record, release a
blocking attempt, or fulfill a payment. Future callers must verify account and
currency through the provider client, then recheck the attempt and business
record under the prescribed transaction locks before applying any transition.

## Provider event

- Stripe event ID unique and non-null; event type, attempt ID, outcome and
  received/processed timestamps. Persist only bounded, non-sensitive data.
- Event uniqueness and payment-level fulfillment uniqueness are separate.
  Both are enforced with the ledger update in a single transaction.
- Commit a verified relevant event only with its resulting state and financial
  effects; roll back the event record on transient failure so Stripe can
  retry. Invalid signatures never create a trusted event record. Record and
  alert on persistent integrity mismatches without applying financial effects.

## Existing entities

`WalletTopup` and `DirectPaymentOrder` remain business records. Add attempt
relationships without reinterpreting historical VNPay data. Keep integer
enum values stable; assign a new value for a Stripe top-up transaction.
Preserve current VNPay columns and tables in additive migrations until separate
destructive migration approval. Existing historical migration files remain
immutable.

The legacy VNPay reference columns on the two business records are nullable
for new Stripe records; their existing unique indexes and historical values are
preserved. The nullable reference does not identify a Stripe attempt: that
relationship is stored on the attempt. Rolling back this schema change needs
an explicitly approved treatment for records without VNPay references.

## Concurrency

Lock the order or top-up and attempt before transitions, using a consistent
lock order; lock the wallet when crediting. Apply ledger and audit records in
the same transaction. Do not hold database locks across Stripe API calls.
ID-based PostgreSQL `FOR UPDATE` helpers are available for each payment parent
and attempt. The caller must open the transaction and acquire the parent lock
before the attempt lock.

Direct-order initiation and wallet order payment first acquire the same
transaction-scoped PostgreSQL advisory lock derived from the order UUID, then
lock an existing direct-order parent and the wallet. This also serializes
first-time payments before a parent row exists. Stripe settlement locks the
parent, attempt and wallet; it checks for a successful wallet payment after
the wallet lock. New direct-order initiation paths must use the advisory lock.
Settlement requires a context with no pending writes and reloads locked entities
before applying changes, so pre-existing tracked values cannot overwrite a
newer balance.
