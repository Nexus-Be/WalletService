namespace Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Features.Withdrawals;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class ProcessWithdrawalCommandHandler
    : IRequestHandler<ProcessWithdrawalCommand, ProcessWithdrawalResponse>
{
    private readonly IWalletDbContext _context;
    private readonly INotificationClient? _notificationClient;

    public ProcessWithdrawalCommandHandler(
        IWalletDbContext context,
        INotificationClient? notificationClient = null)
    {
        _context = context;
        _notificationClient = notificationClient;
    }

    public async Task<ProcessWithdrawalResponse> Handle(
        ProcessWithdrawalCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.AdminUserId, out var adminGuid) || adminGuid == Guid.Empty)
        {
            throw new ArgumentException("Admin user ID is invalid.", nameof(request));
        }

        if (!WithdrawalWorkflow.TryParse(request.Action, out var action))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_ACTION",
                "Action must be one of: Approve, Reject, StartProcessing, Complete, Fail, ResolveFailed.");
        }

        if (WithdrawalWorkflow.RequiresAdmin(action) && !request.ActorIsAdmin)
        {
            throw new BusinessRuleValidationException(
                "FORBIDDEN",
                "Access denied: Administrator privileges are required to resolve a processing withdrawal.",
                403);
        }

        if (request.HasConflictingReferences)
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_INPUT",
                "Transfer reference and reference code must match when both are provided.");
        }

        var effectiveReference = request.EffectiveTransferReference;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var withdrawal = await _context.LockWithdrawalRequestAsync(request.WithdrawalId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(WithdrawalRequest), request.WithdrawalId);

        if (!WithdrawalWorkflow.CanTransition(withdrawal.Status, action))
        {
            throw new BusinessRuleValidationException(
                "WITHDRAWAL_INVALID_TRANSITION",
                $"Cannot apply action '{action}' to a withdrawal in status '{withdrawal.Status}'.",
                409);
        }

        var wallet = await _context.LockWalletByIdAsync(withdrawal.WalletId, cancellationToken)
                     ?? throw new WalletNotFoundException(withdrawal.WalletId.ToString());

        var oldStatus = withdrawal.Status;
        var now = DateTimeOffset.UtcNow;
        string? transactionId = null;
        string? verificationEvidence = null;

        switch (action)
        {
            case WithdrawalAction.Approve:
                if (!string.IsNullOrWhiteSpace(effectiveReference))
                {
                    var approveReference = effectiveReference.Trim();
                    await EnsureUniqueTransferReferenceAsync(withdrawal.Id, approveReference, cancellationToken);
                    withdrawal.GatewayReferenceId = approveReference;
                }

                wallet.CompleteLockedWithdrawal(withdrawal.Amount);
                withdrawal.Status = WithdrawalStatus.Approved;
                withdrawal.ProcessedAt = now;
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
                    Note = $"Withdrawal to {withdrawal.BankName}, {WithdrawalWorkflow.MaskAccount(withdrawal.BankAccountNumber)}; fee {withdrawal.Fee.ToString("0.00", CultureInfo.InvariantCulture)}"
                };
                _context.WalletTransactions.Add(debit);
                transactionId = debit.Id.ToString();
                break;

            case WithdrawalAction.Reject:
                wallet.ReleaseLockedFunds(withdrawal.Amount);
                withdrawal.Status = WithdrawalStatus.Rejected;
                withdrawal.StatusReason = string.IsNullOrWhiteSpace(request.AdminNote)
                    ? null
                    : request.AdminNote.Trim();
                withdrawal.ProcessedAt = now;
                break;

            case WithdrawalAction.StartProcessing:
                withdrawal.Status = WithdrawalStatus.Processing;
                withdrawal.ProcessedAt = now;
                break;

            case WithdrawalAction.Complete:
                if (!request.TransferConfirmed || string.IsNullOrWhiteSpace(effectiveReference))
                {
                    throw new BusinessRuleValidationException(
                        "VAL_INVALID_INPUT",
                        "Transfer confirmation and transfer reference are required when completing a withdrawal.");
                }

                var transferReference = effectiveReference.Trim();
                await EnsureUniqueTransferReferenceAsync(withdrawal.Id, transferReference, cancellationToken);
                withdrawal.GatewayReferenceId = transferReference;
                withdrawal.Status = WithdrawalStatus.Completed;
                withdrawal.CompletedAt = now;
                withdrawal.ProcessedAt = now;
                break;

            case WithdrawalAction.Fail:
            case WithdrawalAction.ResolveFailed:
                var reason = request.AdminNote?.Trim() ?? string.Empty;
                if (reason.Length < WithdrawalWorkflow.MinReasonLength || reason.Length > WithdrawalWorkflow.MaxReasonLength)
                {
                    throw new BusinessRuleValidationException(
                        "VAL_INVALID_INPUT",
                        "A failure reason between 3 and 500 characters is required.");
                }

                if (action == WithdrawalAction.ResolveFailed)
                {
                    verificationEvidence = request.VerificationEvidence?.Trim() ?? string.Empty;
                    if (!request.BankVerifiedNotTransferred ||
                        verificationEvidence.Length < WithdrawalWorkflow.MinReasonLength ||
                        verificationEvidence.Length > WithdrawalWorkflow.MaxReasonLength)
                    {
                        throw new BusinessRuleValidationException(
                            "VAL_INVALID_INPUT",
                            "Bank verification and evidence are required to resolve a processing withdrawal as failed.");
                    }
                }

                var balanceBefore = wallet.Balance;
                wallet.Deposit(withdrawal.Amount);
                var reversal = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    UserId = wallet.UserId.ToString(),
                    Type = WalletTransactionType.WithdrawalReversal,
                    Amount = withdrawal.Amount,
                    Currency = wallet.Currency.ToString(),
                    BalanceBefore = balanceBefore,
                    BalanceAfter = wallet.Balance,
                    ReferenceId = withdrawal.Id,
                    Status = TransactionStatus.Success,
                    Note = $"Withdrawal reversal: {reason}"
                };
                _context.WalletTransactions.Add(reversal);
                transactionId = reversal.Id.ToString();
                withdrawal.Status = WithdrawalStatus.Failed;
                withdrawal.StatusReason = reason;
                withdrawal.ProcessedAt = now;
                break;
        }

        withdrawal.ProcessedBy = adminGuid;
        withdrawal.UpdatedAt = now;

        _context.AddAuditLog(
            action: WithdrawalWorkflow.AuditActionOf(action),
            entityName: nameof(WithdrawalRequest),
            entityId: withdrawal.Id.ToString(),
            oldValues: new { Status = oldStatus.ToString() },
            newValues: new
            {
                Status = withdrawal.Status.ToString(),
                withdrawal.ProcessedBy,
                Reason = withdrawal.StatusReason,
                TransferReference = withdrawal.GatewayReferenceId,
                VerificationEvidence = verificationEvidence
            },
            userId: adminGuid.ToString());

        WithdrawalNotificationEvent? notification = null;
        if (withdrawal.Status is WithdrawalStatus.Approved
            or WithdrawalStatus.Rejected
            or WithdrawalStatus.Completed
            or WithdrawalStatus.Failed)
        {
            notification = new WithdrawalNotificationEvent(
                SourceEventId: $"withdrawal:{withdrawal.Id:D}:{withdrawal.Status}",
                WithdrawalId: withdrawal.Id,
                RecipientUserId: wallet.UserId,
                Status: withdrawal.Status.ToString(),
                ConfirmedAt: withdrawal.CompletedAt ?? withdrawal.ProcessedAt ?? now,
                DisplayReason: withdrawal.StatusReason,
                TransactionId: transactionId ?? withdrawal.GatewayReferenceId,
                TraceId: Activity.Current?.Id);

            // Written in the same SaveChanges as the money change, so a notification that is owed can never be lost.
            if (_notificationClient is not null)
            {
                NotificationDispatchExtensions.QueueWithdrawalEvent(_context, notification);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (notification is not null)
        {
            await NotificationDispatchExtensions.TryDispatchWithdrawalEventAsync(
                _context,
                _notificationClient,
                notification,
                cancellationToken);
        }

        return new ProcessWithdrawalResponse(
            withdrawal.Id.ToString(),
            withdrawal.Status.ToString(),
            withdrawal.ProcessedAt ?? now,
            transactionId,
            withdrawal.GatewayReferenceId,
            withdrawal.StatusReason);
    }

    private async Task EnsureUniqueTransferReferenceAsync(
        Guid withdrawalId,
        string transferReference,
        CancellationToken cancellationToken)
    {
        var duplicateExists = await _context.WithdrawalRequests
            .AsNoTracking()
            .AnyAsync(
                r => r.Id != withdrawalId && r.GatewayReferenceId == transferReference,
                cancellationToken);

        if (duplicateExists)
        {
            throw new BusinessRuleValidationException(
                "TRANSFER_REFERENCE_ALREADY_USED",
                "Transfer reference has already been used for another withdrawal.",
                409);
        }
    }
}
