# Research: Stripe migration

## Checkout and confirmation

**Decision**: Stripe-hosted Checkout in payment mode; verified webhooks are
the authoritative payment signal. Treat session completion with unpaid status
as pending, and support asynchronous success and failure.

**Rationale**: Browser navigation can be lost and delayed payment methods may
complete checkout before payment confirmation.

**Alternatives considered**: Embedded payment form, browser-return settlement.

## Idempotency and recovery

**Decision**: Persist an attempt and stable provider idempotency key before
creating a session; verify correlation metadata and safely bind early webhooks.
Track events and apply financial effects once in one transaction. Reconcile
ambiguous attempts instead of blindly recreating sessions after provider
idempotency retention has elapsed.

**Alternatives considered**: Provider call inside a database transaction,
using only webhook event IDs to deduplicate financial effects.

## Retry and money

**Decision**: Allow a replacement direct-order attempt only after the provider
confirms the prior attempt cannot still succeed. Use whole VND units and
checked integer conversion. Record and escalate unexpected extra charges.

**Alternatives considered**: Retry immediately on browser cancel or local
timeout; automatic refund of an extra charge, which is outside current scope.

## Eligibility and cutover

**Decision**: Implement and validate in test mode; do not enable live funding
before Stripe approves the account and transferable wallet use case, and the
team defines refunds, disputes and reversals. Preserve historical migration
records; destructive cleanup requires separate approval.

**Alternatives considered**: Immediate live cutover or destructive reset.
