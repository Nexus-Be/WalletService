namespace Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class AdjustWalletBalanceCommandHandler
    : IRequestHandler<AdjustWalletBalanceCommand, BalanceAdjustmentResponse>
{
    private readonly IWalletDbContext _context;

    public AdjustWalletBalanceCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<BalanceAdjustmentResponse> Handle(
        AdjustWalletBalanceCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.AdminUserId, out var adminGuid) || adminGuid == Guid.Empty)
            throw new ArgumentException("Admin user ID is invalid.", nameof(request));

        if (!request.AdjustmentType.TryToEnum<BalanceAdjustmentType>(out var adjustmentType))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_ADJUSTMENT_TYPE",
                "Adjustment type must be either 'Credit' or 'Debit'.");
        }

        var credit = adjustmentType == BalanceAdjustmentType.Credit;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByIdAsync(request.WalletId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.WalletId.ToString());

        var balanceBefore = wallet.Balance;
        if (credit)
            wallet.Deposit(request.Amount);
        else
            wallet.Deduct(request.Amount);

        var walletTransaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = credit ? WalletTransactionType.AdjustmentCredit : WalletTransactionType.AdjustmentDebit,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            Note = $"Admin adjustment ({request.AdjustmentType}): {request.Reason.Trim()}"
        };
        _context.WalletTransactions.Add(walletTransaction);
        _context.AddAuditLog(
            action: AuditAction.BalanceAdjustment,
            entityName: nameof(WalletAccount),
            entityId: wallet.Id.ToString(),
            oldValues: new { Balance = balanceBefore },
            newValues: new { wallet.Balance, walletTransaction.Amount, walletTransaction.Type },
            userId: adminGuid.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BalanceAdjustmentResponse(
            walletTransaction.Id.ToString(),
            wallet.Id.ToString(),
            request.Amount,
            wallet.Balance,
            walletTransaction.CreatedAt
        );
    }
}
