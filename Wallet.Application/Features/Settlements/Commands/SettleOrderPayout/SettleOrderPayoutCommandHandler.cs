namespace Wallet.Application.Features.Settlements.Commands.SettleOrderPayout;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Checkout;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class SettleOrderPayoutCommandHandler : IRequestHandler<SettleOrderPayoutCommand, SettleOrderPayoutResponse>
{
    private readonly IWalletDbContext _context;

    public SettleOrderPayoutCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<SettleOrderPayoutResponse> Handle(SettleOrderPayoutCommand request, CancellationToken cancellationToken)
    {
        var sellerId = Guid.Parse(request.SellerId);
        ValidateIdentity(request);
        var fingerprint = request.Fingerprint();

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Lock order (shared with every path that spends an order escrow): order payment lock(s) by ascending id, then the escrow
        // rows, then the subsidy budget, then the seller wallet.
        await LockOrderAndSubOrderAsync(request.ParentOrderId, request.SubOrderId, cancellationToken);

        var existing = await _context.OrderSettlements.FirstOrDefaultAsync(
            item => item.BusinessType == request.BusinessType
                && item.ParentOrderId == request.ParentOrderId
                && item.SubOrderId == request.SubOrderId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadFingerprint == fingerprint)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return Map(existing, isReplay: true);
            }

            // Same key, different payload: keep the first result and money untouched, and leave a durable trace for reconciliation.
            _context.AddAuditLog(
                AuditAction.SettlementConflict,
                nameof(OrderSettlement),
                existing.Id.ToString(),
                oldValues: new { existing.PayloadFingerprint, existing.SellerReceiveAmount, existing.PlatformFeeAmount, existing.ShippingAmount, existing.SubsidyAmount, existing.OrderEscrowAmount, existing.DepositEscrowAmount },
                newValues: new { PayloadFingerprint = fingerprint, request.SellerReceiveAmount, request.PlatformFeeAmount, request.ShippingAmount, request.SubsidyAmount, request.OrderEscrowAmount, request.DepositEscrowAmount, request.SubOrderId, request.ParentOrderId },
                userId: sellerId.ToString());
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            throw new BusinessRuleValidationException(
                "SETTLEMENT_CONFLICT",
                "The sub-order was already settled with a different payload.",
                409);
        }

        // The same sub-order already settled under another parent order or business type is a conflict, not a new payout.
        var settledElsewhere = await _context.OrderSettlements.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SubOrderId == request.SubOrderId, cancellationToken);
        if (settledElsewhere is not null)
        {
            _context.AddAuditLog(
                AuditAction.SettlementConflict,
                nameof(OrderSettlement),
                settledElsewhere.Id.ToString(),
                oldValues: new { settledElsewhere.BusinessType, settledElsewhere.ParentOrderId, settledElsewhere.PayloadFingerprint },
                newValues: new { request.BusinessType, request.ParentOrderId, PayloadFingerprint = fingerprint, request.SubOrderId },
                userId: sellerId.ToString());
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            throw new BusinessRuleValidationException(
                "SETTLEMENT_CONFLICT",
                "The sub-order was already settled under a different order or business type.",
                409);
        }

        // A payout made by the legacy route is final: never pay it again, whatever the escrow looks like.
        var paidByLegacyRoute = await _context.WalletTransactions.AsNoTracking().AnyAsync(
            tx => tx.ReferenceId == request.SubOrderId && tx.Type == WalletTransactionType.EscrowRelease && tx.Status == TransactionStatus.Success,
            cancellationToken);
        if (paidByLegacyRoute)
        {
            throw new BusinessRuleValidationException(
                "SETTLEMENT_ALREADY_PAID_LEGACY",
                "The sub-order was already paid by the legacy payout route and cannot be paid again.",
                409);
        }

        var orderEscrowBefore = (decimal?)null;
        OrderEscrow? orderEscrow = null;
        if (request.OrderEscrowAmount > 0m)
        {
            await _context.LockOrderEscrowByOrderIdAsync(request.ParentOrderId, cancellationToken);
            var state = await OrderEscrowAccounting.LoadOrReconcileAsync(_context, request.ParentOrderId, cancellationToken);
            orderEscrow = state.Escrow;
            if (orderEscrow is null)
            {
                if (!state.HasPayment)
                {
                    throw new BusinessRuleValidationException("PAYMENT_NOT_FOUND", "No completed wallet payment was found for the order.", 404);
                }

                throw new BusinessRuleValidationException(
                    state.HasResolvedOperation ? "SETTLEMENT_CONFLICT" : "ORDER_ESCROW_UNAVAILABLE",
                    state.HasResolvedOperation
                        ? "The order payment was already resolved without recoverable escrow."
                        : "The order's wallet-payment escrow cannot be safely reconstructed.",
                    409);
            }

            if (orderEscrow.Status == OrderEscrowStatus.Cancelled)
            {
                throw new BusinessRuleValidationException("ORDER_ESCROW_CANCELLED", "The order escrow was already cancelled and cannot be settled.", 409);
            }

            if (orderEscrow.RemainingAmount < request.OrderEscrowAmount)
            {
                throw new BusinessRuleValidationException("ORDER_ESCROW_INSUFFICIENT", "The order has insufficient remaining wallet-payment escrow for this payout.", 409);
            }

            orderEscrowBefore = orderEscrow.RemainingAmount;
        }

        decimal? depositBefore = null;
        IReadOnlyList<AuctionDepositEscrow> depositEscrows = Array.Empty<AuctionDepositEscrow>();
        if (request.DepositEscrowAmount > 0m)
        {
            // An auction order can have several deposit escrows (Buy Now: the deposit and the goods hold). They are one pool for the payout.
            depositEscrows = await _context.LockDepositEscrowsByOrderIdAsync(request.ParentOrderId, cancellationToken);
            if (depositEscrows.Count == 0)
            {
                throw new BusinessRuleValidationException("DEPOSIT_ESCROW_NOT_FOUND", "No captured deposit escrow was found for the order.", 404);
            }

            depositBefore = depositEscrows.Sum(item => item.RemainingAmount);
            if (depositBefore < request.DepositEscrowAmount)
            {
                throw new BusinessRuleValidationException("DEPOSIT_ESCROW_INSUFFICIENT", "The deposit escrow has insufficient funds for this payout.", 409);
            }
        }

        PlatformSubsidyReservation? reservation = null;
        PlatformSubsidyBudget? budget = null;
        if (request.SubsidyAmount > 0m)
        {
            reservation = await _context.PlatformSubsidyReservations.FirstOrDefaultAsync(item => item.SubOrderId == request.SubOrderId, cancellationToken);
            if (reservation is null || reservation.Status != SubsidyReservationStatus.Held || reservation.Amount != request.SubsidyAmount)
            {
                throw new BusinessRuleValidationException(
                    "SUBSIDY_NOT_RESERVED",
                    "The platform subsidy for this sub-order is not reserved for this amount.",
                    409);
            }

            budget = await _context.LockSubsidyBudgetAsync(reservation.BudgetId, cancellationToken)
                ?? throw new InvalidOperationException("The subsidy budget of a reservation was not found.");
            if (budget.ReservedAmount < reservation.Amount)
            {
                throw new InvalidOperationException("The subsidy budget reserved total is smaller than one of its reservations.");
            }
        }

        var credit = request.SellerReceiveAmount;
        WalletAccount? wallet = null;
        Guid walletId;
        WalletTransaction? releaseTransaction = null;
        WalletTransaction? subsidyTransaction = null;
        if (credit > 0m)
        {
            wallet = await _context.LockOrEnsureWalletByUserIdAsync(sellerId, cancellationToken);
            walletId = wallet.Id;
            var escrowPart = credit - request.SubsidyAmount;
            if (escrowPart > 0m)
            {
                releaseTransaction = StageCredit(wallet, WalletTransactionType.EscrowRelease, escrowPart, request.SubOrderId, "Sub-order payout released to seller");
                // The shadow ledger posts one entry per save against one counter account (the first transaction type decides). The subsidy
                // belongs to a different account (PLATFORM_SUBSIDY) than the escrow release (ORDER_ESCROW), so it is saved on its own. Both
                // saves are inside the same database transaction: they still commit or roll back together.
                if (request.SubsidyAmount > 0m)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            if (request.SubsidyAmount > 0m)
            {
                DateTimeOffset? subsidyAt = null;
                if (releaseTransaction is not null)
                {
                    // Ensure subsidy timestamps sort strictly after the escrow release so I8's CreatedAt ordering matches the balance chain.
                    subsidyAt = DateTimeOffset.UtcNow;
                    if (subsidyAt.Value <= releaseTransaction.CreatedAt.AddMicroseconds(10))
                        subsidyAt = releaseTransaction.CreatedAt.AddMicroseconds(10);
                }

                subsidyTransaction = StageCredit(wallet, WalletTransactionType.PlatformSubsidyCredit, request.SubsidyAmount, request.SubOrderId, "Platform-funded voucher subsidy paid to seller", subsidyAt);
            }
        }
        else
        {
            // Nothing to pay the seller (the whole amount is fee or shipping): the allocation is still recorded, against the seller's wallet if it exists.
            wallet = await _context.LockWalletByUserIdAsync(sellerId, cancellationToken)
                ?? await _context.LockOrEnsureWalletByUserIdAsync(sellerId, cancellationToken);
            walletId = wallet.Id;
        }

        var now = DateTimeOffset.UtcNow;
        if (orderEscrow is not null)
        {
            orderEscrow.RemainingAmount -= request.OrderEscrowAmount;
            if (orderEscrow.RemainingAmount == 0m)
            {
                orderEscrow.Status = OrderEscrowStatus.Settled;
            }

            orderEscrow.UpdatedAt = now;
        }

        var depositToTake = request.DepositEscrowAmount;
        foreach (var depositEscrow in depositEscrows)
        {
            if (depositToTake <= 0m) break;
            var take = Math.Min(depositEscrow.RemainingAmount, depositToTake);
            if (take <= 0m) continue;
            depositEscrow.RemainingAmount -= take;
            depositEscrow.UpdatedAt = now;
            depositToTake -= take;
        }

        if (reservation is not null && budget is not null)
        {
            reservation.Status = SubsidyReservationStatus.Used;
            reservation.UsedAt = now;
            reservation.UpdatedAt = now;
            budget.ReservedAmount -= reservation.Amount;
            budget.UsedAmount += reservation.Amount;
            budget.UpdatedAt = now;
        }

        var settlement = new OrderSettlement
        {
            Id = Guid.NewGuid(),
            BusinessType = request.BusinessType,
            ParentOrderId = request.ParentOrderId,
            SubOrderId = request.SubOrderId,
            SellerId = sellerId,
            SellerWalletId = walletId,
            SellerReceiveAmount = request.SellerReceiveAmount,
            PlatformFeeAmount = request.PlatformFeeAmount,
            ShippingAmount = request.ShippingAmount,
            SubsidyAmount = request.SubsidyAmount,
            OrderEscrowAmount = request.OrderEscrowAmount,
            DepositEscrowAmount = request.DepositEscrowAmount,
            PayloadFingerprint = fingerprint,
            PayloadVersion = request.PayloadVersion,
            EscrowReleaseTransactionId = releaseTransaction?.Id,
            SubsidyTransactionId = subsidyTransaction?.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.OrderSettlements.Add(settlement);
        _context.AddAuditLog(
            AuditAction.OrderSettlement,
            nameof(OrderSettlement),
            settlement.Id.ToString(),
            oldValues: new { OrderEscrowRemaining = orderEscrowBefore, DepositEscrowRemaining = depositBefore },
            newValues: new
            {
                request.BusinessType,
                request.ParentOrderId,
                request.SubOrderId,
                SellerId = sellerId,
                request.SellerReceiveAmount,
                request.PlatformFeeAmount,
                request.ShippingAmount,
                request.SubsidyAmount,
                request.OrderEscrowAmount,
                request.DepositEscrowAmount,
                OrderEscrowRemaining = orderEscrow?.RemainingAmount,
                DepositEscrowRemaining = depositEscrows.Count == 0 ? (decimal?)null : depositEscrows.Sum(item => item.RemainingAmount),
                EscrowReleaseTransactionId = releaseTransaction?.Id,
                SubsidyTransactionId = subsidyTransaction?.Id
            },
            userId: sellerId.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return Map(settlement, isReplay: false);
    }

    private WalletTransaction StageCredit(WalletAccount wallet, WalletTransactionType type, decimal amount, Guid referenceId, string note, DateTimeOffset? createdAt = null)
    {
        var balanceBefore = wallet.Balance;
        wallet.Deposit(amount);
        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = type,
            Amount = amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = referenceId,
            Note = note,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(transaction);
        return transaction;
    }

    private static void ValidateIdentity(SettleOrderPayoutCommand request)
    {
        if (request.BusinessType == "OrderPayout" && request.DepositEscrowAmount != 0m)
        {
            throw new BusinessRuleValidationException("SETTLEMENT_INVALID", "An order payout cannot use a deposit escrow.", 400);
        }

        if (request.SubsidyAmount > request.SellerReceiveAmount)
        {
            throw new BusinessRuleValidationException("SETTLEMENT_INVALID", "The subsidy cannot exceed the amount paid to the seller.", 400);
        }

        var sources = request.OrderEscrowAmount + request.DepositEscrowAmount + request.SubsidyAmount;
        var uses = request.SellerReceiveAmount + request.PlatformFeeAmount + request.ShippingAmount;
        if (sources != uses)
        {
            throw new BusinessRuleValidationException(
                "SETTLEMENT_IDENTITY_INVALID",
                "The escrow and subsidy amounts must equal the seller amount plus the platform fee and shipping allocation.",
                400);
        }

        if (uses <= 0m)
        {
            throw new BusinessRuleValidationException("SETTLEMENT_INVALID", "The payout must move a positive amount.", 400);
        }
    }

    private async Task LockOrderAndSubOrderAsync(Guid parentOrderId, Guid subOrderId, CancellationToken cancellationToken)
    {
        if (parentOrderId == subOrderId)
        {
            await _context.LockOrderPaymentAsync(parentOrderId, cancellationToken);
            return;
        }

        var first = parentOrderId.CompareTo(subOrderId) < 0 ? parentOrderId : subOrderId;
        var second = first == parentOrderId ? subOrderId : parentOrderId;
        await _context.LockOrderPaymentAsync(first, cancellationToken);
        await _context.LockOrderPaymentAsync(second, cancellationToken);
    }

    private static SettleOrderPayoutResponse Map(OrderSettlement item, bool isReplay) => new(
        item.Id.ToString(),
        item.BusinessType,
        item.ParentOrderId.ToString(),
        item.SubOrderId.ToString(),
        item.SellerWalletId.ToString(),
        item.SellerReceiveAmount,
        item.PlatformFeeAmount,
        item.ShippingAmount,
        item.SubsidyAmount,
        item.OrderEscrowAmount,
        item.DepositEscrowAmount,
        item.EscrowReleaseTransactionId?.ToString(),
        item.SubsidyTransactionId?.ToString(),
        isReplay,
        item.CreatedAt);
}
