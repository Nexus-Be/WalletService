namespace Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
