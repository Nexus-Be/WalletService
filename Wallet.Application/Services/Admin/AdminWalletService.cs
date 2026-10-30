namespace Wallet.Application.Services.Admin;

using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

public sealed class AdminWalletService : IAdminWalletService
{
    private readonly IWalletDbContext _context;

    public AdminWalletService(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<ProcessWithdrawalResponse> ProcessWithdrawalAsync(
        Guid id,
        string adminUserId,
        ProcessWithdrawalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(adminUserId, out var adminGuid) || adminGuid == Guid.Empty)
            throw new ArgumentException("Admin user ID is invalid.", nameof(adminUserId));

        var approve = string.Equals(request.Action, "APPROVE", StringComparison.OrdinalIgnoreCase);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var withdrawal = await _context.LockWithdrawalRequestAsync(id, cancellationToken);

        if (withdrawal is null)
            throw new EntityNotFoundException(nameof(WithdrawalRequest), id);
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
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            UserId = adminGuid.ToString(),
            Action = approve ? AuditAction.WithdrawalApprove : AuditAction.WithdrawalReject,
            EntityName = nameof(WithdrawalRequest),
            EntityId = withdrawal.Id.ToString(),
            OldValues = JsonSerializer.Serialize(new { Status = oldStatus.ToString() }),
            NewValues = JsonSerializer.Serialize(new
            {
                Status = withdrawal.Status.ToString(),
                withdrawal.ProcessedBy,
                Reason = approve ? null : request.AdminNote
            })
        });

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProcessWithdrawalResponse(
            withdrawal.Id.ToString(),
            withdrawal.Status.ToString().ToUpperInvariant(),
            withdrawal.ProcessedAt ?? DateTimeOffset.UtcNow,
            transactionId
        );
    }

    public async Task<BalanceAdjustmentResponse> AdjustBalanceAsync(
        Guid walletId,
        string adminUserId,
        BalanceAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(adminUserId, out var adminGuid) || adminGuid == Guid.Empty)
            throw new ArgumentException("Admin user ID is invalid.", nameof(adminUserId));

        var credit = string.Equals(request.AdjustmentType, "CREDIT", StringComparison.OrdinalIgnoreCase);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByIdAsync(walletId, cancellationToken)
                     ?? throw new WalletNotFoundException(walletId.ToString());

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
        _context.AuditLogs.Add(new AuditLog
        {
            ServiceName = "Wallet.Api",
            UserId = adminGuid.ToString(),
            Action = AuditAction.BalanceAdjustment,
            EntityName = nameof(WalletAccount),
            EntityId = wallet.Id.ToString(),
            OldValues = JsonSerializer.Serialize(new { Balance = balanceBefore }),
            NewValues = JsonSerializer.Serialize(new { wallet.Balance, walletTransaction.Amount, walletTransaction.Type })
        });

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
