namespace Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;

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

internal sealed class RefundOrderPaymentCommandHandler
    : IRequestHandler<RefundOrderPaymentCommand, WalletOrderTransactionResponse>
{
    private readonly IWalletDbContext _context;

    public RefundOrderPaymentCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletOrderTransactionResponse> Handle(
        RefundOrderPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var buyerId = Guid.Parse(request.UserId);

        WalletTransaction? payment = null;
        if (!request.IsSubOrder)
        {
            payment = await _context.WalletTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.ReferenceId == request.OrderId &&
                         t.Type == WalletTransactionType.PurchaseDebit &&
                         t.Status == TransactionStatus.Success,
                    cancellationToken)
                ?? throw new BusinessRuleValidationException(
                    "PAYMENT_NOT_FOUND",
                    "No completed wallet payment was found for the order.",
                    404);

            if (payment.UserId != request.UserId)
            {
                throw new BusinessRuleValidationException(
                    "REFUND_CONFLICT",
                    "The refund user does not match the order payer.",
                    409);
            }
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(buyerId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId);

        var existing = await _context.WalletTransactions
            .FirstOrDefaultAsync(
                t => t.ReferenceId == request.OrderId && t.Type == WalletTransactionType.RefundCredit,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.Amount != request.Amount)
            {
                throw new BusinessRuleValidationException(
                    "REFUND_CONFLICT",
                    "The order was already refunded with a different amount.",
                    409);
            }

            return Map(existing);
        }

        if (payment is not null && request.Amount > payment.Amount)
        {
            throw new BusinessRuleValidationException(
                "REFUND_EXCEEDS_PAYMENT",
                "Refund amount exceeds the original payment.",
                409);
        }

        var balanceBefore = wallet.Balance;
        wallet.Deposit(request.Amount);

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.RefundCredit,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = request.OrderId,
            Note = string.IsNullOrWhiteSpace(request.Reason) ? "Order refund" : request.Reason.Trim(),
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
