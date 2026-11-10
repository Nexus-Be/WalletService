# Admin Financial Operations API Contract

This contract draft covers only the WalletService P2-03 and P2-04 work approved for planning. Service-to-service P2-05 and Auction escrow endpoints are deferred.

## Withdrawal decision (existing route, additive field)

`PATCH /api/v1/admin/wallets/withdrawals/{id}`

- **Authorization**: admin or staff, as on the existing action.
- **Request**:
  - `action`: `Approve` or `Reject`
  - `referenceCode`: optional for `Reject`; required, non-blank, maximum 100 characters for `Approve`
  - `adminNote`: optional
- **Behavior**:
  - `Approve`: staff confirms that the manual bank transfer has already occurred; persist `referenceCode`, consume locked funds, record actor/time/audit, return current status `Approved`.
  - `Reject`: release locked funds and record actor/time/audit; return `Rejected`.
  - Repeated decisions do not repeat balance effects.
- **Response**: existing `ProcessWithdrawalResponse` fields plus nullable `referenceCode`. Keep existing status values and response code.
- **Errors**: validation problem for missing/invalid reference; existing not-found and already-processed errors remain stable.
- **Security/data handling**: do not log or return the full bank account number. Mask it in any withdrawal transaction note.

## Administrator transaction report (new route)

`GET /api/v1/admin/wallets/ledger`

- **Authorization**: admin only.
- **Query parameters**: `page` (default 1), `pageSize` (default 10, maximum 100), optional `walletId`, `type`, `status`, `fromDate`, `toDate`.
- **Response**: pure `PagedData<AdminWalletLedgerEntry>` using `items` and standard pagination metadata.
- **Ordering**: `CreatedAt DESC`, then transaction ID descending for deterministic pages.
- **Data fields**: transaction ID, wallet ID, type, amount, currency, balance before/after, status, reference ID, timestamp.
- **Errors**: HTTP 400 problem details for invalid filters/date ranges, HTTP 401 for unauthenticated requests, and HTTP 403 for non-admin callers.
- **Envelope**: no service-side `ApiResponse` wrapper.

## Wallet reconciliation report (new route)

`GET /api/v1/admin/wallets/reconciliation`

- **Authorization**: admin only.
- **Query parameters**: `page`, `pageSize` (same bounds as ledger), optional `walletId`.
- **Response**: pure `PagedData<WalletReconciliationResult>`.
- **Comparison**: current `WalletAccount.Balance` versus latest successful transaction `BalanceAfter`, ordered by `CreatedAt DESC` then transaction ID DESC.
- **Result values**: `Balanced`, `Discrepancy`, `InsufficientHistory` (serialized as PascalCase strings). A wallet without any successful transaction snapshot is `InsufficientHistory`, even when its balance is zero.
- **Limitations**: this endpoint reconciles stored wallet snapshots only; it is not double-entry accounting, journal balancing, or external bank reconciliation.
- **Errors**: same authorization and pagination behavior as the ledger route.

## Deferred API surface

Do not add `/api/v1/internal/wallets/*` or auction-deposit routes in this implementation. Their route shape, service authentication, idempotency, ownership semantics, and cross-service failure behavior need a separate approved contract.
