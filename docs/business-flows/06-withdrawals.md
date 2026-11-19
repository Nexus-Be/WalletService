# Request and process a withdrawal

[Business overview](../business-use-cases.md) | [Shared request path](00-shared-request-path.md)

**Stories:** As a seller, I want to request withdrawal to a bank account. As
admin or staff, I want to approve a completed external payout or reject it.

## Create a request

1. Send `POST /api/v1/wallets/withdrawals` with `amount`, `currency`,
   `bankCode`, `accountNumber` and `accountHolderName`. The user ID comes from
   context. The role filter accepts seller, admin or staff. The
   `ReleaseFeatures:WithdrawalsEnabled` gate defaults to enabled in
   Development and disabled otherwise unless configured.
2. Validation requires VND, bank details, a 50,000 to 500,000,000 amount and
   supported money precision. If the gate is disabled, Wallet returns `503
   FEATURE_DISABLED` rather than running the handler.
3. [RequestWithdrawalCommandHandler](../../Wallet.Application/Features/Withdrawals/Commands/RequestWithdrawal/RequestWithdrawalCommandHandler.cs)
   locks the wallet, reserves available funds by increasing locked balance,
   creates a `Pending` request with fee zero and saves an audit record in a
   database transaction. Total balance is not yet reduced.
4. Wallet returns `201` with `withdrawalId`, `amount`, `fee`, `status`, and
   `requestedAt`. Missing wallets or insufficient available funds fail.

## Approve or reject

1. Admin/staff sends `PATCH /api/v1/admin/wallets/withdrawals/{id}` with
   `action` (`Approve` or `Reject`), optional `adminNote`, and for approval a
   required `referenceCode`. Existing request processing is not gated by the
   new-request flag.
2. [ProcessWithdrawalCommandHandler](../../Wallet.Application/Features/AdminWallets/Commands/ProcessWithdrawal/ProcessWithdrawalCommandHandler.cs)
   locks the pending request and its wallet. Already processed requests are
   rejected rather than processed again.
3. On approval, it reduces both locked and total balance, marks `Approved`,
   records a `WithdrawalDebit` and saves the external reference. On rejection,
   it marks `Rejected` and releases the locked amount without debiting total
   balance. Either path writes an audit entry and commits.
4. Wallet returns `200` with `withdrawalId`, `status`, `processedAt`,
   `transactionId` (approval only) and `referenceCode` (approval only).
   Gateway output follows the [shared path](00-shared-request-path.md).

The code records a bank reference but does not initiate a bank transfer.
Neither request nor approval is a linked-bank-account management API.
