namespace Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

internal sealed class AdjustWalletBalanceCommandHandler
    : IRequestHandler<AdjustWalletBalanceCommand, BalanceAdjustmentResponse>
{
    private const decimal MaxAdjustmentAmount = 500_000_000m;
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

        if (request.Amount <= 0m || request.Amount > MaxAdjustmentAmount)
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_AMOUNT",
                "Amount must be between 0.01 and 500,000,000.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_INPUT",
                "Reason is required and must not exceed 500 characters.");
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();
        if (idempotencyKey is not null && !WithdrawalWorkflow.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_IDEMPOTENCY_KEY",
                "Idempotency-Key header must be 1 to 100 ASCII letters, digits, hyphens, or underscores.");
        }

        var credit = adjustmentType == BalanceAdjustmentType.Credit;
        var fingerprint = idempotencyKey is null
            ? null
            : ComputeFingerprint(request.UserId, adjustmentType.ToString(), request.Amount, reason);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(request.UserId, cancellationToken)
                     ?? throw new WalletNotFoundException(request.UserId.ToString());

        if (idempotencyKey is not null)
        {
            var existing = await _context.WalletTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.WalletId == wallet.Id &&
                         (t.Type == WalletTransactionType.AdjustmentCredit ||
                          t.Type == WalletTransactionType.AdjustmentDebit) &&
                         t.ClientIdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new BusinessRuleValidationException(
                        "IDEMPOTENCY_KEY_REUSED",
                        "The idempotency key was already used for a different balance adjustment.",
                        409);
                }

                await transaction.CommitAsync(cancellationToken);
                return new BalanceAdjustmentResponse(
                    existing.Id.ToString(),
                    wallet.Id.ToString(),
                    existing.Amount,
                    existing.BalanceAfter,
                    existing.CreatedAt,
                    existing.Type == WalletTransactionType.AdjustmentCredit ? nameof(BalanceAdjustmentType.Credit) : nameof(BalanceAdjustmentType.Debit),
                    existing.Currency ?? wallet.Currency.ToString(),
                    wallet.AvailableBalance);
            }
        }

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
            Note = $"Admin adjustment ({adjustmentType}): {reason}",
            ClientIdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint
        };
        _context.WalletTransactions.Add(walletTransaction);
        _context.AddAuditLog(
            action: AuditAction.BalanceAdjustment,
            entityName: nameof(WalletAccount),
            entityId: wallet.Id.ToString(),
            oldValues: new { Balance = balanceBefore },
            newValues: new
            {
                wallet.Balance,
                walletTransaction.Amount,
                AdjustmentType = adjustmentType.ToString(),
                Reason = reason,
                TransactionId = walletTransaction.Id
            },
            userId: adminGuid.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BalanceAdjustmentResponse(
            walletTransaction.Id.ToString(),
            wallet.Id.ToString(),
            request.Amount,
            wallet.Balance,
            walletTransaction.CreatedAt,
            adjustmentType.ToString(),
            wallet.Currency.ToString(),
            wallet.AvailableBalance);
    }

    private static string ComputeFingerprint(Guid userId, string adjustmentType, decimal amount, string reason)
    {
        var canonical = string.Join(
            '\n',
            userId.ToString("D"),
            adjustmentType,
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
