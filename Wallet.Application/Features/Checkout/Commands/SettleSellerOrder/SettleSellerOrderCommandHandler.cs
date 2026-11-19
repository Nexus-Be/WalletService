namespace Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Features.Checkout;
using global::Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class SettleSellerOrderCommandHandler
    : IRequestHandler<SettleSellerOrderCommand, WalletOrderTransactionResponse>
{
    private readonly IWalletDbContext _context;

    public SettleSellerOrderCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletOrderTransactionResponse> Handle(
        SettleSellerOrderCommand request,
        CancellationToken cancellationToken)
    {
        var sellerId = Guid.Parse(request.SellerId);

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.LockOrderPaymentAsync(request.SubOrderId, cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(sellerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.SellerId);

        var existing = await _context.WalletTransactions
            .FirstOrDefaultAsync(
                t => t.ReferenceId == request.SubOrderId && t.Type == WalletTransactionType.EscrowRelease,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.WalletId != wallet.Id || existing.Amount != request.Amount)
            {
                throw new BusinessRuleValidationException(
                    "SETTLEMENT_CONFLICT",
                    "The sub-order was already settled with different details.",
                    409);
            }

            return Map(existing);
        }

        var escrowState = await OrderEscrowAccounting.LoadOrReconcileAsync(
            _context,
            request.SubOrderId,
            cancellationToken);
        var escrow = escrowState.Escrow;
        if (escrow is null && escrowState.HasPayment)
        {
            if (escrowState.HasResolvedOperation)
            {
                throw new BusinessRuleValidationException(
                    "SETTLEMENT_CONFLICT",
                    "The order payment was already resolved without recoverable escrow.",
                    409);
            }

            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_UNAVAILABLE",
                "The order's wallet-payment escrow cannot be safely reconstructed.",
                409);
        }

        if (escrow is not null && escrow.RemainingAmount < request.Amount)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_INSUFFICIENT",
                "The order has insufficient remaining wallet-payment escrow for this payout.",
                409);
        }

        var escrowBefore = escrow?.RemainingAmount;
        if (escrow is not null)
        {
            escrow.RemainingAmount -= request.Amount;
            escrow.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var balanceBefore = wallet.Balance;
        wallet.Deposit(request.Amount);

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.EscrowRelease,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = request.SubOrderId,
            Note = "Sub-order payout released to seller",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(transaction);
        _context.AddAuditLog(
            AuditAction.OrderSettlement,
            escrow is null ? nameof(WalletTransaction) : nameof(OrderEscrow),
            request.SubOrderId.ToString(),
            oldValues: new { Balance = balanceBefore, EscrowRemaining = escrowBefore },
            newValues: new
            {
                Balance = wallet.Balance,
                EscrowRemaining = escrow?.RemainingAmount,
                transaction.Amount,
                SellerId = sellerId,
                TransactionId = transaction.Id
            },
            userId: sellerId.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new WalletOrderTransactionResponse(
            transaction.Id.ToString(),
            transaction.WalletId.ToString(),
            transaction.UserId,
            transaction.Type.ToString(),
            transaction.Amount,
            transaction.Currency,
            transaction.BalanceAfter,
            transaction.Status.ToString(),
            transaction.Note,
            transaction.CreatedAt);
    }

    private static WalletOrderTransactionResponse Map(WalletTransaction t) => new(
        t.Id.ToString(),
        t.WalletId.ToString(),
        t.UserId,
        t.Type.ToString(),
        t.Amount,
        t.Currency,
        t.BalanceAfter,
        t.Status.ToString(),
        t.Note,
        t.CreatedAt);
}
