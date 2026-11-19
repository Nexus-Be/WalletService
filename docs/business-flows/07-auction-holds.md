# Reserve and resolve auction deposits

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

**Story:** As a participant, I need a deposit reserved without spending it
until the auction outcome determines whether it is released or captured.

Auction service routes require `X-Internal-Service-Key` configured for the
auction caller. Mutations also require `Idempotency-Key`. The owner-facing
list uses the user's gateway identity instead. A missing/invalid service key
is `401`; missing key configuration is an operational `500`.

## Reserve

1. The auction caller sends `POST /api/v1/wallets/holds` with `ownerUserId`,
   `referenceType: AUCTION_DEPOSIT`, `referenceId`, positive whole-VND
   `amount`, `currency: VND` and optional object `metadata`.
2. [CreateHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/CreateHold/CreateHoldCommandHandler.cs)
   validates input, locks the idempotency key and reference, then the owner's
   active VND wallet. It reserves available funds by increasing locked
   balance and stores the hold, operation response and audit entry atomically.
3. Wallet returns `201` for a new hold, or `200` for an equivalent replay,
   with `holdId`, `status`, `amount`, `balance`, `heldBalance`,
   `availableBalance`, `createdAt`. Reusing a key or reference for different
   details conflicts; insufficient available balance fails.

The [tagged lifecycle test](../../Wallet.Tests/AuctionHoldLifecycleTests.cs)
demonstrates that existing reservations remain locked, retries do not create
another hold, and a release restores only its own reservation.

## Release

1. Send `POST /api/v1/wallets/holds/{holdId}/release` with an allowed `reason`
   such as `AUCTION_ENDED_NOT_WINNER`, plus both service and idempotency keys.
2. [ReleaseHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/ReleaseHold/ReleaseHoldCommandHandler.cs)
   locks the operation and hold, releases the hold amount from locked balance,
   changes `Held` to `Released`, and saves the result and audit atomically.
   An equivalent replay returns the prior result; a conflicting or already
   captured hold fails.
3. Wallet returns `200` with `holdId`, `status`, `releasedAt`,
   `availableBalance`. Total balance is unchanged.

## Capture

1. Send `POST /api/v1/wallets/holds/{holdId}/capture` with `purpose`,
   optional `orderId` and `transfers`. The full transfer sum must equal the
   hold. `FORFEIT` permits seller-wallet and platform destinations;
   `ORDER_PAYMENT` requires an order ID and a system-escrow transfer.
2. The `ORDER_PAYMENT` purpose is controlled by
   `ReleaseFeatures:AuctionOrderCaptureEnabled`, disabled by default outside
   Development; even retries are gated. Forfeiture is not blocked by that
   particular gate.
3. [CaptureHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/CaptureHold/CaptureHoldCommandHandler.cs)
   validates and locks the hold and involved wallets, deducts the held funds
   from the buyer, credits seller recipients where specified, records platform
   transfer entries where applicable, and creates a separate
   `AuctionDepositEscrow` for order-payment capture. It writes the ledger,
   operation result and audit within the transaction.
4. Wallet returns `200` with `holdId`, `status`, `purpose`, `capturedAt`,
   `transfers` and `buyerBalanceAfter`. Equivalent retries replay the prior
   result; conflicting capture or incorrect transfer sum is rejected.

## Refund a captured order deposit

1. Send `POST /api/v1/wallets/holds/{holdId}/refund-captured` with matching
   `orderId` and `reason`, plus both keys.
2. [RefundCapturedHoldCommandHandler](../../Wallet.Application/Features/Holds/Commands/RefundCapturedHold/RefundCapturedHoldCommandHandler.cs)
   requires a captured `ORDER_PAYMENT` hold and sufficient matching deposit
   escrow. It returns the full held amount to the buyer, reduces that escrow,
   changes the hold to `Refunded`, and writes transaction, operation and audit
   records atomically. Equivalent retries return the stored result.
3. Wallet returns `200` with `holdId`, `status`, `refundedAmount`,
   `buyerBalanceAfter`. Wrong purpose/order or insufficient escrow produces a
   conflict. This operation is not the same as refunding a wallet-funded
   order payment.

## Look up holds

- The auction service can call `GET /api/v1/wallets/holds/{holdId}` or
  `GET /api/v1/wallets/holds/by-reference/{referenceType}/{referenceId}`.
  [GetHoldQueryHandler](../../Wallet.Application/Features/Holds/Queries/GetHold/GetHoldQueryHandler.cs)
  returns `200` with owner, reference, amount, status, capture purpose and
  timestamps, or `404 HOLD_NOT_FOUND`.
- A user calls `GET /api/v1/wallets/me/holds` with optional `page`, `pageSize`,
  `status`. [ListMyHoldsQueryHandler](../../Wallet.Application/Features/Holds/Queries/ListMyHolds/ListMyHoldsQueryHandler.cs)
  returns a `200` paginated list scoped to that user, including auction ID
  from metadata when present.

Holds have no automatic expiry. For full reasons, error codes, key rules and
the state machine see the [auction contract](../api/holds/auction-deposits.md).
These service-to-service outputs return to their caller; a Gateway envelope
applies only if the call actually passes through that middleware.
