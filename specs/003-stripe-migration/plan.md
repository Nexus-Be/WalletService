# Implementation Plan: Stripe payment migration

**Branch**: `dev` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

## Summary

Replace VNPay top-up and direct payment with Stripe-hosted Checkout and signed
webhooks. Preserve the existing wallet, ledger, ownership, two controllers and
15-handler architecture. User-approved API changes are recorded in the
specification. Test mode is in scope; live operation remains blocked until
account eligibility, VND availability, wallet funding approval and financial
reversal policy are verified.

## Technical Context

- .NET 8, ASP.NET Core, EF Core/PostgreSQL and MediatR; direct `IWalletDbContext`.
- Use Stripe's .NET SDK, pinned API version, test credentials provided through
  environment configuration; no SDK or secret embedded in source control.
- PostgreSQL on port 5440, API on port 8008, xUnit tests in `Wallet.Tests`.
- VND is represented as whole currency units; do not multiply by 100.
- Initial card and account-enabled delayed payment methods are supported.
- Automatic Stripe refunds and disputes are out of scope for test mode.
- Preserve `WalletsController` and `AdminWalletsController`, 15 handlers and
  the existing balance and authorization invariants. Replace the return query
  with an owned top-up status query rather than adding a handler.

## GitNexus Impact and Preflight

Baseline architecture verifier: passed. GitNexus reported **HIGH** risk for the
shared `VnPayService.CreatePaymentUrl` generator: direct callers include top-up
and direct checkout; the top-up handler and signing/query flows are affected.
Interface dispatch means some graph results are lower bounds. Repeat
method-level impact on each symbol immediately before its implementation.
The refreshed index reports truncated process discovery; absence from the graph
is not evidence that a flow is unaffected. Existing unrelated changes in the
worktree must be preserved.

Persistence preflight: `IWalletDbContext` impact is LOW but lower-bound because
of interface dispatch; `WalletDbContext` is MEDIUM (API startup, infrastructure
registration and migration metadata). `TestWalletDbContext` is UNKNOWN in the
graph; text search confirms it is used across existing wallet and VNPay tests.
The EF model snapshot and its `BuildModel` are UNKNOWN in the graph; EF
discovers the snapshot by its context metadata. The new tables are additive;
existing payment routes and records are not changed by the foundation.
The first additive migration creates Stripe attempt and event tables and
refuses automatic rollback because dropping payment records requires separate
authorization. Provider identifiers and blocking attempts use unique indexes;
the PostgreSQL check constraints enforce positive whole VND and a single
purpose-specific parent. On an isolated PostgreSQL 16 container, all migrations
applied and direct-order blocking uniqueness, whole-VND and purpose constraints
were exercised successfully. Broader concurrency and provider-ID coverage
still need integration verification before these tasks are complete.
Next persistence increment: GitNexus reports `WalletDbContext` MEDIUM and
`IWalletDbContext` LOW with interface-dispatch lower bounds; the new attempt
and configuration are UNKNOWN because the index does not yet include them.
Text search finds their model, migration and tests but no payment handlers.
The test context is UNKNOWN and text search confirms broad use by wallet tests.
The shared SQL-lock builder is unchanged. Identity fields are init-only with
EF after-save rejection (not a database trigger); raw SQL/bulk writes must not
change them. Lock order is parent, attempt, then wallet, inside a transaction.
Provider registration: `AddInfrastructureServices` is UNKNOWN in GitNexus
because receiver typing loses a caller; text search confirms API startup and
connection-string tests call it. Stripe configuration is validated lazily when
the client is resolved so incomplete test-mode setup cannot break existing
VNPay operations. Stripe.net 51.0.0 is pinned; its SDK API version is used
consistently with webhook parsing rather than depending on account defaults.
Webhook verification: the DI method remains UNKNOWN; text search confirms API
startup and infrastructure tests as callers. Register a lazy verifier with the
SDK's five-minute signature tolerance and version compatibility checks.
Reject non-UTF-8, payloads over 1 MiB and events without explicit test-mode
`livemode: false`. Future HTTP handling must enforce the same byte limit while
reading, distinguish verification errors from server misconfiguration, avoid
logging raw payloads, and keep mismatched API versions visible for retry and
operator action. A verified event is not evidence that a payment is settled.
Reconciliation provider reads: GitNexus reports UNKNOWN for the new client and
interface because they are not indexed; text search finds only DI and tests.
The client can retrieve test-mode VND session and PaymentIntent snapshots,
including provider status, amount and correlation metadata. These reads do not
decide retry eligibility or settlement. The caller must compare the snapshots
with the persisted attempt and obtain definitive provider evidence before
releasing a blocking attempt; network failure remains ambiguous.
Reconciliation policy and creation response validation: the new symbols and
`CreateSessionAsync` are UNKNOWN in the stale graph; text search finds only
provider client tests and no payment handler callers. The policy classifies
matching snapshots conservatively but performs no settlement or state change.
An invalid creation response is an ambiguous provider outcome and must not
cause an attempt to be discarded or replaced without reconciliation.
Legacy parent migration: GitNexus reports LOW lower-bound impact for the
`WalletTopup` and `DirectPaymentOrder` entities, including the existing
creation, status and seed paths. Both EF configurations and the snapshot
are UNKNOWN; EF discovers them through context assembly scanning and migration
metadata. The nullable reference preserves historical values and unique
indexes, and the VNPay direct handler rejects an absent reference. PostgreSQL
verification applied the migration over existing historical rows, inserted
multiple null references and confirmed duplicate legacy references still fail.
Automatic rollback is blocked because it would require handling Stripe rows.
Ledger history preparation: `WalletTransactionType` and the transaction-list
handler are UNKNOWN in the graph; text search identifies ledger writers, the
transaction endpoint and its tests. The new `TopupStripe=13` preserves
`TopupVnpay=0`, and the `DEPOSIT` alias includes both without changing
ownership or database pagination.
Stripe documents that an expired Checkout Session cannot be completed and a
canceled PaymentIntent cannot receive further charges. These are provider
observations, not substitutes for transaction-level revalidation; Checkout
recovery is requested disabled and checked in provider responses before using
expiry as replacement evidence. Missing or enabled recovery remains blocking.
Real Stripe test-mode verification is still required. See [Checkout Session expiry](https://docs.stripe.com/api/checkout/sessions/expire)
and [PaymentIntent cancellation](https://docs.stripe.com/api/payment_intents/cancel).

The user initially selected a drain-first cutover, then explicitly waived
that prerequisite because all VNPay use to date was sandbox testing and its
data need not be preserved. This is a test-mode cutover decision, not evidence
that any real payments were reconciled. Preserve historical rows and schema
unless removal is actually needed; do not expose Stripe payment initiation
before webhook settlement, failure handling and status are integrated.

Cutover impact: GitNexus reports UNKNOWN for `WalletsController`, the VNPay
top-up, IPN and return handlers, and both direct-payment handlers. The graph
does not resolve the HTTP or MediatR entry points; check their controllers,
registrations and tests through text search rather than assuming no callers.
The existing direct-status query now selects the successful Stripe attempt's
PaymentIntent, or the latest attempt when none is successful, without changing
legacy VNPay output. Its handler method and test class also report UNKNOWN;
the controller dispatch and direct handler tests are confirmed by text search.
This behavior is covered against both the test context and PostgreSQL.
The top-up status query reads only Stripe top-ups belonging to the
authenticated wallet owner, and returns the same not-found error for another
owner and a nonexistent payment. Its graph impact is UNKNOWN because it is not
indexed; text search found tests and its new MediatR handler. The approved
owner-protected HTTP status action now dispatches this query. It replaces the
VNPay browser-return action without changing the 15-handler count. Stripe
initiation and webhook actions have since been wired.
Stripe API failures, transport failures and provider timeouts now produce a
sanitized `503 PAYMENT_PROVIDER_UNAVAILABLE` through the existing exception
middleware; caller cancellation still propagates. The middleware method has
LOW GitNexus risk (caller: `InvokeAsync`; affected request/error-formatting
flows), while the new Stripe client methods and tests are UNKNOWN in the stale
index. Text search found the client registration, initiation and webhook
helpers, and their tests. Ambiguous creation remains persisted and blocking.
Webhook verification now validates the signature independently using the SDK
before parsing the event. Invalid signatures map to a sanitized 400; validly
signed parsing or version failures remain server errors and must not be
misreported as signature errors. GitNexus reports UNKNOWN for the verifier and
its interface; text search found DI, the staged processor and test callers.
The middleware impact is LOW through `InvokeAsync` and its error-formatting
flows. These checks are now used by the webhook route.
The bounded webhook body reader is wired to the HTTP route.
GitNexus cannot resolve the new reader or the verifier's payload-size constant
(UNKNOWN); text search finds the constant in the verifier and its tests only.
The reader must enforce the same byte limit while streaming, including when
Content-Length is missing or misleading, without decoding or logging payloads.
The VNPay return, initiation and IPN actions have been removed during the
approved sandbox cutover.

Staged implementation: the new initiation, settlement and webhook-processing
helpers are not indexed by GitNexus; text search finds only their own tests.
The existing wallet-order and direct-checkout handlers report UNKNOWN graph
impact, and text search identifies their API and test callers. They now share
a transaction-scoped order lock so first-time wallet and direct payments cannot
race, and wallet payment rejects blocking or escalated Stripe attempts.
The shared context reports MEDIUM impact and the interface LOW lower-bound;
locked entities are refreshed, and settlement rejects pending context writes.
Provider calls occur after attempt commit and before session-binding locks.
PostgreSQL tests exercised concurrent duplicate/distinct events, rollback,
early callbacks, competing wallet/direct payments and concurrent initiation.
They also exercise a late success after an expired direct attempt while a
replacement is being created: the order settles once, the replacement response
is rejected, and a later second paid event is durably marked for reconciliation.
The new test class is UNKNOWN in GitNexus; text search finds no production
callers.
The initiation, settlement and webhook helpers are wired to HTTP and MediatR.
Reconciliation-required events are committed and logged at critical severity.
Real Stripe test-mode and gateway integration validation remain unfinished.

Stripe's PaymentIntent API specifies an eight-digit maximum. For zero-decimal
VND this makes 99,999,999 VND the maximum single payment, one VND below the
legacy top-up limit. Both staged initiation paths and the client reject higher
amounts before creating an attempt; account-specific limits still need testing.
Source: [Stripe PaymentIntent creation](https://docs.stripe.com/api/payment_intents/create).

## Constitution Check

- [x] User approved the proposed endpoint and direct checkout contract changes.
- [x] Two controllers and CQRS remain; no feature-service facade or repository.
- [x] Ownership, row locking, audit, idempotency and non-negative balances are
      required by the design.
- [x] Pre-code endpoint documentation and the endpoint checklist are required.
- [x] User approved proceeding with test-mode implementation after the
      constitution correction and test-mode contract finalization.
- [ ] Destructive database changes are separately approved before execution.

## Repository Structure

- `Wallet.Domain/Entities/` and `Enums/`: payment attempts and provider events.
- `Wallet.Application/Features/Topups/`, `Checkout/` and `VnPay/`: replace
  VNPay handlers with Stripe handlers without changing the handler count.
- `Wallet.Application/Interfaces/`: Stripe integration and lock contracts.
- `Wallet.Infrastructure/Payments/` and `Persistence/`: SDK, webhook parsing,
  EF model, additive migration, indexes and lock helpers.
- `Wallet.Api/Controllers/Public/WalletsController.cs`: approved endpoint
  updates and raw-body webhook boundary.
- `Wallet.Tests/`: payment, callback, database and contract coverage.
- `docs/api/`: pre-code endpoint specification and setup guide.

## Data and API Changes

See [data-model.md](data-model.md) and [contracts/payments.md](contracts/payments.md).
Use additive schema first. Keep immutable migration history and do not destroy
historical rows, reset a database or remove historical columns without separate
authorization. Success and cancellation origins are server configuration.
External provider calls occur outside database transactions. Reject ambiguous
attempt replacement until Stripe confirms the earlier payment cannot succeed.
An unexpected second charge requires durable manual reconciliation and an
alert; do not credit or settle twice.

The API and attempt-transition documents now describe proposed replay,
provider-error, lifecycle and recovery semantics. Validate implementation
details and any still-unapproved public contract changes before coding; do not
represent account-dependent limits or live eligibility as verified.

## Verification

Run focused payment tests, real PostgreSQL locking and migration tests,
Stripe test-mode webhook/checkout scenarios, and gateway raw-body contract
checks. Verify webhook duplication, concurrency, late events, expiry, provider
timeouts and recovery. Confirm test credentials remain outside source.

```bash
./scripts/verify-wallet-architecture.sh
dotnet build Wallet.sln -c Release
dotnet test Wallet.sln -c Release
```

Verify Swagger routes and responses; coordinate downstream client and gateway
changes before cutover. No live launch until the eligibility gates pass.

Staged verification: Release build succeeded with no warnings, the architecture
verifier passed, and EF reports no pending model changes. Tests were also run
against disposable PostgreSQL. Repeat all checks after endpoint integration;
Stripe test-mode and gateway validation remain outstanding.

## Complexity Tracking

The user approved route and payload changes. Adding an owned top-up query is
balanced by replacing the obsolete return query; the 15-handler count stays
fixed. If that proves infeasible, stop and request specific architecture
approval rather than silently updating the verifier.
