# Feature Plan: Handover Wallet Hardening & Notification Integration

- **Branch**: `feature/handover-wallet-hardening`
- **Session Plan**: `.agents/plan/PLAN-20261119-0839-HANDOVER-WALLET-HARDENING.md`
- **Integration Handoff**: `docs/integrations/handover-response.md`

## Summary
- Rebuilt baseline ACER work on `dev`: 6-action withdrawal workflow (`Approve`, `Reject`, `StartProcessing`, `Complete`, `Fail`, `ResolveFailed`), `Idempotency-Key` + SHA-256 fingerprint on `RequestWithdrawal`, 4 withdrawal read queries, `FreezeWallet`/`UnfreezeWallet`, `LockOrEnsureWalletByUserIdAsync` auto-creation, `StripeSettings`, `ListTopups` status filter, `BUYER_WITHDRAWN`, and live hold status on `CreateHold` replay.
- Applied P0 security fixes: `[RequireInternalServiceKey]` on internal S2S endpoints (`~/api/v1/internal/wallets/...`) and the 4 legacy Commerce routes on `WalletsController`, `EnsureAdmin` on `AdminWalletsController.AdjustBalance`, `AvailableBalance` check in `PayOrderWithWalletCommandHandler`, and `403 WALLET_FROZEN` enforcement across `PayOrderWithWallet`, `Transfer`, `RequestWithdrawal`, and `AdjustOrderPayment`.
- Added P1/P2 APIs: Seller saved `BankAccount` CRUD + withdrawal fallback, seller `CancelWithdrawal`, `GetTransactionById`, direct checkout `Resume`/`Cancel`/`Expire`, Admin wallet/hold/reconciliation queries + manual attempt resolution, internal `holds` mirror routes, and post-order `AdjustOrderPayment`.
- Wired outbound REST notifications via `INotificationClient` / `NotificationClient` after commit for `withdrawal-events` and `auction-deposits`, recording `AuditAction.NotificationDispatchFailed` on failure without rolling back committed ledger state.
