namespace Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class ProcessWithdrawalCommandHandler
    : IRequestHandler<ProcessWithdrawalCommand, ProcessWithdrawalResponse>
{
    private readonly IWalletDbContext _context;

    public ProcessWithdrawalCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<ProcessWithdrawalResponse> Handle(
        ProcessWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.AdminUserId, out var adminGuid) || adminGuid == Guid.Empty)
            throw new ArgumentException("Admin user ID is invalid.", nameof(request));

        if (!request.Action.TryToEnum<WithdrawalDecision>(out var decision))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_ACTION",
                "Action must be either 'Approve' or 'Reject'.");
        }

        var approve = decision == WithdrawalDecision.Approve;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var withdrawal = await _context.LockWithdrawalRequestAsync(request.WithdrawalId, cancellationToken);

        if (withdrawal is null)
            throw new EntityNotFoundException(nameof(WithdrawalRequest), request.WithdrawalId);
        if (withdrawal.Status != WithdrawalStatus.Pending)
            throw new BusinessRuleValidationException("Withdrawal request has already been processed.");

        var wallet = await _context.LockWalletByIdAsync(withdrawal.WalletId, cancellationToken)
                     ?? throw new WalletNotFoundException(withdrawal.WalletId.ToString());

        var oldStatus = withdrawal.Status;
        string? transactionId = null;
        if (approve)
        {
            wallet.CompleteLockedWithdrawal(withdrawal.Amount);
            withdrawal.Status = WithdrawalStatus.Approved;
            withdrawal.ProcessedAt = DateTimeOffset.UtcNow;
            var debit = new WalletTransaction
            {
                WalletId = wallet.Id,
                UserId = wallet.UserId.ToString(),
                Type = WalletTransactionType.WithdrawalDebit,
                Amount = withdrawal.Amount,
                Currency = wallet.Currency.ToString(),
                BalanceBefore = wallet.Balance + withdrawal.Amount,
                BalanceAfter = wallet.Balance,
                ReferenceId = withdrawal.Id,
                Status = TransactionStatus.Success,
                Note = $"Withdrawal to {withdrawal.BankName}, {withdrawal.BankAccountNumber}; fee {withdrawal.Fee.ToString("0.00", CultureInfo.InvariantCulture)}"
            };
            _context.WalletTransactions.Add(debit);
            transactionId = debit.Id.ToString();
        }
        else
        {
            wallet.ReleaseLockedFunds(withdrawal.Amount);
            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.ProcessedAt = DateTimeOffset.UtcNow;
        }

        withdrawal.ProcessedBy = adminGuid;
        _context.AddAuditLog(
            action: approve ? AuditAction.WithdrawalApprove : AuditAction.WithdrawalReject,
            entityName: nameof(WithdrawalRequest),
            entityId: withdrawal.Id.ToString(),
            oldValues: new { Status = oldStatus.ToString() },
            newValues: new
            {
                Status = withdrawal.Status.ToString(),
                withdrawal.ProcessedBy,
                Reason = approve ? null : request.AdminNote
            },
            userId: adminGuid.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProcessWithdrawalResponse(
            withdrawal.Id.ToString(),
            withdrawal.Status.ToString(),
            withdrawal.ProcessedAt ?? DateTimeOffset.UtcNow,
            transactionId
        );
    }
}
