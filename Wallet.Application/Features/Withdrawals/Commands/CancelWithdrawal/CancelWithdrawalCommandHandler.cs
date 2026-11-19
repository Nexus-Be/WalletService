namespace Wallet.Application.Features.Withdrawals.Commands.CancelWithdrawal;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class CancelWithdrawalCommandHandler(IWalletDbContext context)
    : IRequestHandler<CancelWithdrawalCommand, WithdrawalSummaryResponse>
{
    public async Task<WithdrawalSummaryResponse> Handle(
        CancelWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var withdrawal = await context.LockWithdrawalRequestAsync(request.WithdrawalId, cancellationToken)
            ?? throw new EntityNotFoundException("WITHDRAWAL_NOT_FOUND", nameof(WithdrawalRequest), request.WithdrawalId);

        var wallet = await context.LockWalletByIdAsync(withdrawal.WalletId, cancellationToken)
            ?? throw new WalletNotFoundException(withdrawal.WalletId.ToString());

        if (wallet.UserId != request.UserId)
        {
            throw new EntityNotFoundException("WITHDRAWAL_NOT_FOUND", nameof(WithdrawalRequest), request.WithdrawalId);
        }

        if (withdrawal.Status != WithdrawalStatus.Pending)
        {
            throw new BusinessRuleValidationException(
                "WITHDRAWAL_INVALID_TRANSITION",
                "Only pending withdrawal requests can be cancelled.",
                409);
        }

        var lockedBefore = wallet.LockedBalance;
        var now = DateTimeOffset.UtcNow;
        wallet.ReleaseLockedFunds(withdrawal.Amount);
        withdrawal.Status = WithdrawalStatus.Cancelled;
        withdrawal.StatusReason = "Cancelled by seller.";
        withdrawal.ProcessedAt = now;
        withdrawal.UpdatedAt = now;

        context.AddAuditLog(
            AuditAction.WithdrawalCancel,
            nameof(WithdrawalRequest),
            withdrawal.Id.ToString(),
            oldValues: new { Status = WithdrawalStatus.Pending.ToString(), LockedBalance = lockedBefore },
            newValues: new { Status = withdrawal.Status.ToString(), LockedBalance = wallet.LockedBalance },
            userId: request.UserId.ToString());

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return WithdrawalReadMapper.ToSummary(withdrawal);
    }
}
