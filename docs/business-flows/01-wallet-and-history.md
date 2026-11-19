# View wallet and transaction history

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

## View my wallet

**Story:** As a user, I want to know my total, locked and spendable funds.

1. A user sends `GET /api/v1/wallets/me`, normally through the Gateway. Wallet
   requires a valid user identity and passes it to `GetMyWalletQuery`.
2. [GetMyWalletQueryHandler](../../Wallet.Application/Features/Wallets/Queries/GetMyWallet/GetMyWalletQueryHandler.cs)
   reads the user's wallet. If missing, it sends `EnsureWalletCommand`.
   [EnsureWalletCommandHandler](../../Wallet.Application/Features/Wallets/Commands/EnsureWallet/EnsureWalletCommandHandler.cs)
   creates a VND wallet with zero balance and an audit record; it also
   handles a concurrent creation by rereading the existing wallet.
3. Wallet returns `200` with `walletId`, `userId`, `balance`,
   `availableBalance`, `lockedBalance`, `heldBalance`, `currency`, `status`
   and `updatedAt`. `heldBalance` equals `lockedBalance`.

This GET can therefore create a wallet, even though its main purpose is a
read. An absent/invalid identity returns `401`.

## List my transactions

**Story:** As a user, I want to see a filtered, paginated history of my money.

1. Send `GET /api/v1/wallets/transactions` with optional `page`, `pageSize`,
   `type`, `fromDate` and `toDate`. The controller injects the caller's ID.
2. [ListTransactionsQueryHandler](../../Wallet.Application/Features/Wallets/Queries/ListTransactions/ListTransactionsQueryHandler.cs)
   filters transactions by that user, then by requested type/date, counts them
   and reads one newest-first database page. Type supports individual enum
   names and aliases including `ALL`, `DEPOSIT`, `WITHDRAWAL`, `PAYMENT` and
   `REFUND`; an invalid type is rejected.
3. Wallet returns `200` with paged transaction entries, including transaction
   and wallet IDs, type, amount, currency, balance after, status, note and
   creation time. The [shared path](00-shared-request-path.md) explains what
   the Gateway does with that result.
