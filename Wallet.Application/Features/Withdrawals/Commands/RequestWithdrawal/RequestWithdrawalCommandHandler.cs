namespace Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class RequestWithdrawalCommandHandler : IRequestHandler<RequestWithdrawalCommand, WithdrawalResponse>
{
    private readonly IWalletDbContext _context;

    public RequestWithdrawalCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WithdrawalResponse> Handle(
        RequestWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_USER_ID",
                "User ID must be a valid UUID.");
        }

        if (!string.Equals(request.Currency, "VND", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_CURRENCY",
                "Currency must be VND.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(userId, cancellationToken)
                     ?? throw new WalletNotFoundException(userId.ToString());

        var lockedBalanceBefore = wallet.LockedBalance;
        wallet.LockFunds(request.Amount);

        var withdrawal = new WithdrawalRequest
        {
            WalletId = wallet.Id,
            Amount = request.Amount,
            Fee = 0m,
            NetAmount = request.Amount,
            BankName = request.BankCode.Trim(),
            BankAccountNumber = request.AccountNumber.Trim(),
            BankAccountHolder = request.AccountHolderName.Trim(),
            Status = WithdrawalStatus.Pending
        };
        _context.WithdrawalRequests.Add(withdrawal);
        _context.AddAuditLog(
            action: AuditAction.WithdrawalRequest,
            entityName: nameof(WithdrawalRequest),
            entityId: withdrawal.Id.ToString(),
            oldValues: new { LockedBalance = lockedBalanceBefore },
            newValues: new
            {
                withdrawal.Amount,
                withdrawal.Fee,
                withdrawal.NetAmount,
                withdrawal.Status,
                LockedBalance = wallet.LockedBalance,
                BankName = withdrawal.BankName
            },
            userId: userId.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WithdrawalResponse(
            withdrawal.Id.ToString(),
            withdrawal.Amount,
            withdrawal.Fee,
            withdrawal.Status.ToString(),
            withdrawal.CreatedAt);
    }
}