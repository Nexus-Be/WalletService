# Research: Order Escrow Settlement

**Feature**: [Order Escrow Settlement](spec.md)  
**Date**: 2026-11-17

## Decision 1: Keep order rules outside Wallet

**Decision**: The order caller continues to decide when payment, seller payout, or refund is permitted and supplies the amounts. Wallet validates that funds exist and performs the requested transfer.

**Rationale**: The current order API owns payment deadlines, status transitions, seller rejection/expiration, refund reasons, and seller payout calculation. Wallet must not calculate commissions, shipping, deposit offsets, or order eligibility.

**Alternatives considered**:
- Move lifecycle or amount calculations into Wallet — rejected because it crosses the established service boundary.
- Change the order-service contract — rejected because this is a Wallet-only change and the current AuctionOrder calls already reuse the same order ID.

## Decision 2: Track wallet-payment funds separately from captured deposits

**Decision**: Add an order escrow record containing the order ID, funding wallet, original amount debited from the buyer's digital Wallet balance, and remaining wallet-payment amount. Keep captured deposits in the existing `AuctionDepositEscrow`.

**Rationale**: Seller settlement and order-payment refunds operate only on buyer-paid digital Wallet funds. The captured deposit remains a separate escrow source and stays fully refundable through the existing hold operation, even after seller settlement. Physical cash/COD collection and reconciliation are outside this Wallet flow.

**Alternatives considered**:
- Treat the wallet debit transaction as sufficient escrow state — rejected because it does not track the remaining amount after payout or refund.
- Merge deposit and order payment into one balance — rejected because seller settlement or an order-payment refund must not consume deposit escrow.

## Decision 3: Limit seller payout to wallet-payment funds

**Decision**: Apply seller payout only against remaining wallet-payment escrow. Reject a payout exceeding that amount even when captured-deposit escrow exists. Never change deposit escrow as part of seller settlement.

**Rationale**: The existing captured-deposit refund operation only refunds the original full hold amount. Spending any part of the deposit on seller payout would make the remaining deposit unrefundable. Wallet-payment-only settlement preserves the current hold contract and source separation.

**Alternatives considered**:
- Spend order-payment funds and then deposit escrow — rejected because partial deposit consumption makes the existing full-deposit refund fail and strands the residual.
- Change captured-deposit refund to return only its remainder — rejected because that changes its financial outcome and requires a separately approved contract decision.
- Automatically settle the remainder to platform or carrier — rejected because Wallet receives no destination or decision for those amounts.

## Decision 4: Serialize order-payment escrow mutations with the existing order advisory lock

**Decision**: Reuse `LockOrderPaymentAsync` for order payment, same-ID seller settlement, and order-payment refund. Keep deposit capture/refund serialized by their existing hold and idempotency locks; they do not mutate order-payment escrow.

**Rationale**: A common order-scoped transaction lock prevents concurrent operations from spending or refunding the same order-payment balance. Deposit operations use an independent funding source and must not affect that balance.

**Alternatives considered**:
- Add a separate escrow lock namespace — rejected because the existing order-payment advisory lock already serializes payment, payout, and order refund.
- Depend only on wallet row locks — rejected because deposit escrow is not itself a wallet balance row.

## Decision 5: Preserve compatibility where parent/child IDs cannot be mapped

**Decision**: Apply backed escrow accounting to the existing order-payment reference and same-ID seller settlement/refund flow. Leave the existing general marketplace child-operation behavior unchanged when the child ID has no matching order-payment escrow.

**Rationale**: Current child operations carry a child ID but no parent-payment ID. Wallet alone cannot safely allocate a parent payment across multiple child orders without changing the caller contract.

**Alternatives considered**:
- Guess the parent payment from amount or buyer — rejected because it is ambiguous and can spend another order's funds.
- Change all child routes or the caller payload — rejected by the Wallet-only scope.

## Decision 6: Preserve operation contracts and release gates

**Decision**: Keep current routes, DTOs, response values, and release feature configuration unchanged. Continue to rely on stable order/hold identifiers for operation replay.

**Rationale**: The consuming client currently uses the existing operations without an idempotency header on the order payout/refund routes. Stable order references already identify the single operation. Changing authorization or deployment gates would require coordinated caller changes and is outside this refactor.

**Alternatives considered**:
- Enable legacy order credits outside Development — rejected; this change must not change deployment exposure.
- Require a new service-key or idempotency header — rejected because current callers do not send those headers.

## Resolved Technical Context

- PostgreSQL advisory lock support already exists through `IWalletDbContext.LockOrderPaymentAsync`.
- Existing captured deposit escrow already stores `OrderId`, `OriginalAmount`, and `RemainingAmount`.
- The existing seller settlement and order refund handlers already persist wallet transaction references by order/sub-order ID, which supports idempotent replay.
- The existing captured-deposit refund requires the full original deposit amount to remain available. Therefore order-payment seller settlement must not consume or mutate captured-deposit escrow.
- No new NuGet dependency, endpoint, or CQRS handler is required.
