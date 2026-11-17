# Auction deposit holds

**Status:** Reserve, release, capture, refund, lookups and buyer listing
implemented. Deposit escrow remains separate from Commerce settlement.

Auction service calls require the approved scoped service key. User calls use
the current gateway identity. Money is whole VND; held balances include
withdrawal reservations. Hold state transitions are `Held` to `Released` or
`Captured`, and `Captured` to `Refunded` only for order payment deposits.
Wallet must reject conflicting idempotency payloads, and persist the result of
each mutation atomically with the balance and audit changes.

| Route | Purpose |
|---|---|
| `POST /api/v1/wallets/holds` | Reserve available buyer balance once per reference. |
| `POST /api/v1/wallets/holds/{holdId}/release` | Release a held deposit. |
| `POST /api/v1/wallets/holds/{holdId}/capture` | Capture the full amount to specified seller/platform recipients or deposit escrow for an order. |
| `POST /api/v1/wallets/holds/{holdId}/refund-captured` | Refund an order-payment deposit from its own escrow. |
| `GET /api/v1/wallets/holds/{holdId}` | Service status lookup. |
| `GET /api/v1/wallets/holds/by-reference/{referenceType}/{referenceId}` | Service lookup after an ambiguous create. |
| `GET /api/v1/wallets/me/holds` | Paginated owned holds. |

Existing Wallet ProblemDetails conventions apply. Amounts, caller identity,
reference, recipients, order and transition validity must be checked before
committing. New holds are forbidden on frozen wallets; held deposits may be
resolved when frozen. Deposit escrow is not connected to existing Commerce
settlement or refund operations pending a separate agreed contract.

## Requests and responses

All service writes require `Idempotency-Key`; an equivalent replay returns the
original outcome and a conflicting reuse is rejected. Service routes require
`X-Internal-Service-Key`, including both lookup routes. A missing or invalid
key returns `401`; an unconfigured service key is an operational error. The
dedicated key is configured as `InternalSecurity:AuctionServiceKey` (or
environment variable `InternalSecurity__AuctionServiceKey`); it must not be
committed. All service routes in the table require this key.

- Create accepts `ownerUserId` (UUID), `referenceType`, `referenceId`, positive
  whole-VND `amount`, `currency` (`VND`) and optional JSON object `metadata`.
  The response contains `holdId`, `status`, `amount`, `balance`, `heldBalance`,
  `availableBalance` and `createdAt`. A new hold returns `201`; an equivalent
  existing reference returns `200`. The reference pair is globally unique.
- Release accepts `reason` and returns `holdId`, `status`, `releasedAt` and
  `availableBalance`. Valid reasons are `AUCTION_ENDED_NOT_WINNER`,
  `AUCTION_CANCELLED`, `AUCTION_FAILED` and `ADMIN_FORCE_CANCEL`.
- Capture accepts `purpose`, optional `orderId`, and `transfers` containing
  `toType`, `toOwnerId` and positive whole-VND `amount`. `FORFEIT` permits
  `SELLER_WALLET` and `PLATFORM` recipients; `ORDER_PAYMENT` requires an order
  UUID and exactly one `SYSTEM_ESCROW` transfer. The transfer sum equals the
  full hold. Response contains `holdId`, `status`, `purpose`, `capturedAt`,
  `transfers` with `ledgerRef`, and `buyerBalanceAfter`.
  Platform credits are recorded as platform ledger entries, not
  credited to an arbitrary user wallet; their sum represents the recorded
  platform balance for this feature. `SYSTEM_ESCROW` creates one
  deposit-specific escrow tied to the hold and order. The escrow is not
  consumed by Commerce operations. No platform payout or balance API exists:
  the entries can be inspected in `AuctionCaptureTransfers` by filtering
  `RecipientType = 'PLATFORM'`. Financial entry fields are immutable through
  EF; database access must be restricted to authorized operators.
- Refund accepts `reason` and the matching `orderId`; the response contains
  `holdId`, `status`, `refundedAmount` and `buyerBalanceAfter`. Only captured
  order-payment deposits with sufficient deposit escrow are refundable.
  `reason` is nonblank and at most 128 characters.
- Service lookups return `holdId`, `ownerUserId`, `referenceType`,
  `referenceId`, `amount`, `status`, `capturePurpose`, `createdAt` and
  `resolvedAt`. Buyer listing supports `page`, `pageSize` and optional
  `status`, returns `PagedData<T>`, and includes the `auctionId` metadata.
- `GET /api/v1/wallets/me` retains `lockedBalance` and `availableBalance` and
  also exposes `heldBalance`, an alias of all locked funds, including
  withdrawal reservations.

Invalid input returns `400 VAL_INVALID_INPUT`; insufficient available funds
returns the existing `400 INSUFFICIENT_BALANCE`. Unknown holds and wallets
return `404 HOLD_NOT_FOUND` and `404 WALLET_NOT_FOUND`. A non-active wallet
returns `403 WALLET_FROZEN` for creation; release remains permitted.
Reference conflicts return `409 HOLD_REFERENCE_CONFLICT`, key or release
payload conflicts return `409 IDEMPOTENCY_CONFLICT`, and a resolved
non-released hold returns `409 HOLD_ALREADY_CAPTURED`. Error bodies use the
existing Wallet ProblemDetails format. Capture returns `409 HOLD_ALREADY_RELEASED`
for a released hold, `409 HOLD_ALREADY_CAPTURED_DIFFERENT` for a different
capture payload, `422 TRANSFERS_SUM_MISMATCH` when transfer amounts do not
equal the hold and `404 WALLET_NOT_FOUND` for a missing seller wallet.
Refund returns `409 HOLD_NOT_REFUNDABLE` for a non-order capture or wrong order,
and `409 ESCROW_INSUFFICIENT` when the associated escrow has insufficient
funds. Invalid recipients, duplicate recipient destinations, self-transfers,
and currency mismatches return `400 VAL_INVALID_INPUT`.
For a different key, an equivalent completed capture or refund returns its
original response, including when a later refund has changed the hold status;
a different payload conflicts. Transfer order does not affect equivalence.
Keys contain at most 128 ASCII letters, digits, colon, period, underscore or
hyphen. Create currently supports reference type `AUCTION_DEPOSIT`; reference
IDs are required and limited to 128 characters.
Capture accepts up to 100 transfers and rejects duplicate destinations.
Transaction history exposes `AuctionHoldCapture` (14),
`AuctionForfeitCredit` (15) and `AuctionDepositRefund` (16); the last also
appears in the `REFUND` filter. Audit actions `AuctionHoldCapture` (14) and
`AuctionHoldRefund` (15) record each transition.

## Persistence and financial invariants

Hold identity comprises its wallet, owner, reference pair, positive whole-VND
amount and creation metadata. State values are `Held` (0), `Captured` (1),
`Released` (2) and `Refunded` (3). Identity is immutable after creation.
`0 <= LockedBalance <= Balance` is enforced in PostgreSQL. The shared locked
balance includes both withdrawal reservations and auction holds; individual
hold rows record only auction reservations. Hold resolution and idempotency
records, ledger credits and deposit escrow must be persisted atomically with
the corresponding balance updates. Holds never expire automatically.
Each successful service mutation must store an immutable operation name,
request fingerprint and response against a unique idempotency key in the same
transaction. Create and release acquire the idempotency lock before their
reference or hold lock, followed by the wallet row lock. Capture and refund
use compatible ordering. Capture locks all participating wallets in ascending
user ID order, matching wallet transfers.
Checkout payment, user transfer and admin debit lock the wallet and use
`Deduct`, which checks available balance. Withdrawal requests and hold
creation lock the wallet and use `LockFunds`, which uses the same check.
Withdrawal completion and hold capture consume their existing reservations
under the wallet lock. PostgreSQL race tests cover competing hold creation
against checkout, transfer, admin debit and withdrawal requests.
