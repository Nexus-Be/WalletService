namespace Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class PayOrderWithWalletCommandHandler
    : IRequestHandler<PayOrderWithWalletCommand, WalletOrderTransactionResponse>
{
    private readonly IWalletDbContext _context;

    public PayOrderWithWalletCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletOrderTransactionResponse> Handle(
        PayOrderWithWalletCommand request,
        CancellationToken cancellationToken)
    {
        var buyerId = Guid.Parse(request.UserId);

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(buyerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var existing = await _context.WalletTransactions
            .Where(t => t.ReferenceId == request.OrderId &&
                        (t.Type == WalletTransactionType.PurchaseDebit ||
                         t.Type == WalletTransactionType.DirectGatewayPayment) &&
                        t.Status != TransactionStatus.Failed &&
                        t.Status != TransactionStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (existing.Type != WalletTransactionType.PurchaseDebit ||
                existing.WalletId != wallet.Id ||
                existing.Amount != request.Amount)
            {
                throw new BusinessRuleValidationException(
                    "PAYMENT_ORDER_CONFLICT",
                    "The order already has a different or completed payment.",
                    409);
            }

            return Map(existing);
        }

        if (wallet.Balance < request.Amount)
        {
            throw new InsufficientBalanceException(wallet.Balance, request.Amount);
        }

        var balanceBefore = wallet.Balance;
        wallet.Deduct(request.Amount);

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.PurchaseDebit,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = request.OrderId,
            Note = "Payment for order via wallet",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(transaction);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return Map(transaction);
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
