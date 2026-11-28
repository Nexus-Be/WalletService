# Request to Auction: deposit integration and reconciliation

**Status:** Integration request; awaiting Auction's response.
**Scope:** Existing Wallet-funded auction deposits, not a new escrow feature.

Hello Auction team,

Wallet has implemented reserve, release, capture, refund and lookup operations
for auction deposits. Before either service relies on these operations in a
release, please review the [current Wallet hold contract](auction-deposits.md)
and confirm the points below. We have not reviewed Auction's implementation or
verified the services together.

## What Wallet currently provides

- Deposits reserve the buyer's **Nexus wallet balance**, not a payment-gateway
  authorization. Holds do not expire automatically.
- Service calls use the scoped Auction service key; mutation calls require an
  `Idempotency-Key`. Wallet stores the original result and rejects conflicting
  requests. The exact routes, keys, payloads and error codes are in the hold
  contract.
- Auction can look up an individual hold by hold ID or by its stable reference.
  The response identifies the owner, reference, amount, current status, capture
  purpose and timestamps. It does not expose the original capture allocation,
  order ID or a complete operation history.
- Auction resolves holds by release or full capture. A `FORFEIT` capture
  credits specified seller wallets and records platform entries. An
  `ORDER_PAYMENT` capture creates a deposit-specific escrow tied to an order;
  its current refund operation returns the full deposit to the buyer's wallet.
- For the initial release, `ORDER_PAYMENT` capture is disabled by default
  outside Development. Hold creation, release, forfeiture and eligible
  captured-deposit refunds remain available; see the hold contract for the
  gate and response.
- Wallet sends no result callback to Auction. Platform entries are not a
  spendable platform wallet and there is no platform payout API.

**Important:** captured order deposits are not consumed by the existing
Commerce settlement or refund handlers. Do not treat the current hold API as
an integrated Commerce escrow or refund the same funds through multiple paths.
That integration remains a separate decision and is outside this request.

## Please confirm the integration contract

1. What are the stable identifiers for an auction, bidder, winning order and
   hold reference? Is each hold reference unique across all auctions and
   bidders, and will Auction persist the returned `holdId`?
2. Which Auction events trigger reserve, release, `FORFEIT`, `ORDER_PAYMENT`
   capture and captured-deposit refund? Who decides the final amounts and
   who invokes each operation?
3. What payloads, recipient types and whole-VND amounts will Auction send
   for each capture? For a forfeiture, who owns and validates the
   seller/platform split? Please provide examples without credentials or
   personal data.
4. When a winner pays or fails to pay, how will Auction learn the authoritative
   outcome from Commerce? How will it avoid prematurely capturing or releasing
   a deposit? Please identify the owner of any cross-service coordination.
5. What should happen on auction cancellation, seller rejection, payment
   timeout, duplicate events, late payment, a frozen buyer wallet, and a
   permanently unavailable recipient wallet?
6. Who requests a captured-deposit refund before Commerce integration exists,
   and how will the teams ensure the deposit and remainder are not refunded
   twice? Please do not enable a combined settlement flow until the ownership
   and funding contract is agreed and implemented.

## Proposed minimal reconciliation process

Please confirm or amend this process; it relies on existing Wallet lookups and
does not require a callback, message broker or new Wallet endpoint.

1. Persist the operation, business reference, request payload and
   idempotency key before sending a financial mutation.
2. If a response is lost, look up the hold by ID or original reference; retry
   the **same operation, payload and key** where appropriate. A timeout is
   not proof that the operation failed. Preserve the original response when
   received, including capture transfer ledger references.
3. Periodically compare Auction's unresolved records to Wallet's current hold
   status, owner, reference, amount and capture purpose. Resolve differences
   according to the agreed auction rules; do not overwrite either system's
   records or force a conflicting financial transition.
4. Escalate discrepancies that an individual lookup cannot explain, such as a
   missing order ID or capture allocation, an ambiguous operation, or an
   unexpectedly unresolved hold. Agree the operator, evidence and response
   time before enabling real financial traffic.

Please specify the intended polling frequency, retry/backoff, operational
owner, escalation deadline, volume and recovery after an Auction restart.
If individual lookups are insufficient, describe the exact missing data and
volume before requesting any new endpoint or bulk reconciliation feature.

## Joint acceptance scenarios

- Duplicate reserve/release/capture/refund calls and response loss do not
  double-lock, debit, credit or release money.
- Non-winners and cancelled auctions are released; winner capture and
  forfeiture follow the agreed events and amounts.
- Concurrent resolution requests produce one valid outcome; Auction records
  the Wallet-authoritative result and reconciles discrepancies.
- Service-key rejection, insufficient available balance, frozen wallets and
  provider/service outages are visible and recoverable.
- Test against PostgreSQL and the actual Auction-to-Wallet network path;
  record results and named owners. Do not use real funds for integration tests.

Please return the confirmed contract, event examples, reconciliation owner,
and any required corrections before production sign-off.
