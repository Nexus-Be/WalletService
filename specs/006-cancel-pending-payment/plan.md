# Plan: Cancel pending direct payment

**Status:** Approved direction; detailed API and safety design pending.

## Objective

Implement an owner-authorized Wallet-side cancellation of an existing direct
Stripe payment. A successful payment racing cancellation is refunded; uncertain
provider or refund outcomes remain pending. A missing payment record is
rejected and does not create an order-wide cancellation marker.

## Context and impact

Read the current Wallet controller, direct payment preparation and settlement,
Stripe provider client, attempt entities, persistence, release configuration,
tests, architecture verifier and the service API template. Preserve the
existing uncommitted escrow work.

GitNexus on 2026-11-17 reports **CRITICAL** impact for `IStripeCheckoutClient`:
52 impacted symbols, 41 direct; dynamic-dispatch boundaries mean the count is
a lower bound, and no processes were resolved. Direct preparation, settlement
and controller class impacts report **UNKNOWN** with no resolved callers.
The index is stale relative to working-tree changes; these zeros are not
evidence of no impact. Inspect actual callers and rerun symbol-level impact
before editing each symbol.

## Steps

1. Finalize and document the Wallet API contract and state machine before code.
2. Design durable owner-scoped cancellation, provider expiration, idempotent
   refund and restart recovery, including a session being created concurrently.
3. Add a MediatR command and carefully coordinate direct checkout and webhook
   settlement. Keep provider calls outside database locks and do not report an
   uncertain outcome as complete.
4. Add reconciliation for pending direct-payment cancellations; preserve the
   existing top-up workflow. Update the approved handler-count governance and
   verifier together.
5. Add focused tests for owner isolation, no-record behavior, concurrent
   initiation/payment/cancellation, duplicate callbacks, provider outages,
   refund finality, idempotency and recovery.
6. Document the integration boundary: this work neither calls another service
   nor verifies end-to-end order state.

## Verification

Run `./scripts/verify-wallet-architecture.sh`, `dotnet build Wallet.sln -c
Release`, and `dotnet test Wallet.sln -c Release`; exercise PostgreSQL
concurrency and provider scenarios when their test environment is available.
Report skipped or unavailable checks explicitly.
