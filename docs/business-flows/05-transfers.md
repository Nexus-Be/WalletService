# Transfer between wallets

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

**Story:** As a wallet owner, I want to transfer available funds to another
user's wallet.

1. Send `POST /api/v1/wallets/transfers` with `recipientUserId`, `amount` and
   optional `note`. The sender ID comes from the request context. Amount must
   be between 1,000 and 100,000,000 VND with the supported money precision.
2. [TransferCommandHandler](../../Wallet.Application/Features/Transfers/Commands/Transfer/TransferCommandHandler.cs)
   rejects invalid IDs and self-transfer, starts a database transaction, and
   locks the two wallets in deterministic user-ID order. Both wallets must
   exist and use the same currency.
3. The domain deducts only if the sender has enough **available** balance;
   the recipient is credited. The handler records `TransferOut` and
   `TransferIn` ledger entries with their respective balance snapshots, then
   commits both sides together.
4. Wallet returns `200` with the sender's `transactionId`, `amount`,
   `currency`, `status` and `createdAt`. Gateway wrapping is described in the
   [shared path](00-shared-request-path.md).

Validation, missing-wallet, currency and insufficient-balance errors are
returned instead of a successful transfer. This handler has no explicit
idempotency key: do not assume an ambiguous retry will be deduplicated.
