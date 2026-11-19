# WalletService: business use cases and user stories

This is a guide to what the current WalletService code does, not a promise that
every integration is enabled or production-ready. It describes the working
tree inspected on 17 November 2026, including uncommitted order-escrow work.
Start here, then follow the links for request-to-response traces.

## What the service does

WalletService owns wallet balances, the corresponding transaction history, and
wallet-side records for payments, withdrawal requests and auction deposits. It
does not own order fulfillment or auction decisions. Stripe handles the
external checkout; WalletService records and verifies its payment state.

| Term | Meaning |
|---|---|
| Balance | Total recorded money in a wallet. |
| Locked balance | Money reserved for withdrawal requests and auction holds. |
| Available balance | `balance - lockedBalance`; only this portion can be spent or newly reserved. `heldBalance` in the wallet response is an alias for **all** locked balance, not just auction holds. |
| Wallet transaction | A ledger entry describing a money movement or direct payment, including its status and balance snapshot. |
| Order-payment escrow | In the current uncommitted implementation, the remaining amount of a buyer's wallet-funded order payment available for a matching payout or refund. |
| Auction deposit escrow | A separate record for a deposit captured for an order; it is not the same money as order-payment escrow. |

For example, a wallet with balance 100 and locked balance 20 has 80 available.
Reserving 50 more leaves the balance at 100, locked at 70 and available at
30. Releasing that hold restores availability without adding new money.

## Actors

- **Wallet owner:** a buyer, seller or other authenticated user viewing and
  spending their own wallet.
- **Seller:** requests a withdrawal; admin and staff are also accepted by the
  shared role filter.
- **Admin or staff:** reviews withdrawal requests and adjusts balances.
  Ledger and wallet reconciliation reports are restricted to admin.
- **Auction caller:** uses a dedicated service key to manage deposit holds.
- **Commerce caller:** invokes the wallet-side order payment, seller
  settlement and refund routes; this guide does not assert how Commerce routes
  or orchestrates those calls.
- **Stripe:** hosts checkout and sends signed payment notifications.

## Main use cases

| Business goal / user story | Detailed flow | Availability and important boundary |
|---|---|---|
| As a user, I want to see my balance and past transactions so I know what I can spend. | [Wallet and history](business-flows/01-wallet-and-history.md) | The first balance lookup creates a zero-balance VND wallet if needed. |
| As a user, I want to add money with Stripe, check its status, and resume an unfinished checkout. | [Stripe top-up](business-flows/02-stripe-topup.md) | The checkout URL alone is not proof of payment; settlement is asynchronous. |
| As a buyer, I want to pay for an order directly through Stripe without using my wallet balance. | [Direct Stripe checkout](business-flows/03-direct-checkout.md) | A separate payment path; balance is not debited. |
| As a buyer, I want to pay an order from available wallet funds; as the commerce workflow, I need to settle or refund eligible funds. | [Wallet order payment](business-flows/04-wallet-orders.md) | Order escrow changes are uncommitted and marked in progress; seller settlement and refunds are release-gated. Legacy child-operation compatibility differs from matching-order escrow. |
| As a user, I want to transfer available money to another wallet. | [Transfers](business-flows/05-transfers.md) | Both wallets must already exist and have matching currencies. |
| As a seller, I want to request a bank withdrawal; as staff, I want to approve or reject it. | [Withdrawals](business-flows/06-withdrawals.md) | New requests are release-gated; approval records a reference, but does not itself send a bank transfer. |
| As an auction participant, I want a deposit reserved, released, captured or refunded according to the auction outcome. | [Auction deposits](business-flows/07-auction-holds.md) | Service-key-protected writes; order-payment capture is release-gated. Holds do not automatically expire. |
| As an operator, I want to adjust a balance and inspect ledger and reconciliation reports. | [Administration](business-flows/08-administration.md) | Adjustments allow admin/staff; reporting is admin-only. |

## Read the flow documents

The [shared request and response path](business-flows/00-shared-request-path.md)
explains the Gateway boundary, authentication, validation and error shape.
Each numbered document then identifies the caller, route, business rules,
database/provider effects, and response. HTTP status and DTO descriptions are
WalletService's unless explicitly labeled Gateway output.

## Scope and caveats

- The code contains a `BankAccount` entity but no linked-bank-account
  management API. Bank details on a withdrawal are part of that request.
- The Stripe contract describes test-mode implementation; live Stripe and
  Gateway end-to-end acceptance are not established by this guide.
- Release flags default to enabled in Development and disabled otherwise
  unless configured; see the individual flows. A disabled action returns
  `503 FEATURE_DISABLED`.
- Sources: [Wallet controller](../Wallet.Api/Controllers/Public/WalletsController.cs),
  [admin controller](../Wallet.Api/Controllers/Public/AdminWalletsController.cs),
  [domain wallet](../Wallet.Domain/Entities/WalletAccount.cs), and the
  [API contracts](api/README.md).
