namespace Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
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

        var idempotencyKey = request.IdempotencyKey.Trim();
        if (!WithdrawalWorkflow.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_IDEMPOTENCY_KEY",
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(userId, cancellationToken)
                     ?? throw new WalletNotFoundException(userId.ToString());

        wallet.EnsureActive();

        var (bankAccountId, bankName, accountNumber, accountHolder) =
            await ResolveBankDetailsAsync(request, userId.ToString(), cancellationToken);

        var fingerprint = WithdrawalWorkflow.Fingerprint(
            wallet.Id,
            request.Amount,
            "VND",
            bankName,
            accountNumber,
            accountHolder);

        var existing = await _context.WithdrawalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.WalletId == wallet.Id && r.ClientIdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new BusinessRuleValidationException(
                    "IDEMPOTENCY_KEY_REUSED",
                    "The idempotency key was already used for a different withdrawal request.",
                    409);
            }

            await transaction.CommitAsync(cancellationToken);
            return new WithdrawalResponse(
                existing.Id.ToString(),
                existing.Amount,
                existing.Fee,
                existing.Status.ToString(),
                existing.CreatedAt)
            {
                IsReplay = true
            };
        }

        var lockedBalanceBefore = wallet.LockedBalance;
        wallet.LockFunds(request.Amount);

        var withdrawal = new WithdrawalRequest
        {
            WalletId = wallet.Id,
            BankAccountId = bankAccountId,
            Amount = request.Amount,
            Fee = 0m,
            NetAmount = request.Amount,
            BankName = bankName,
            BankAccountNumber = accountNumber,
            BankAccountHolder = accountHolder,
            Status = WithdrawalStatus.Pending,
            ClientIdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint
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

    private async Task<(Guid? BankAccountId, string BankName, string AccountNumber, string AccountHolder)>
        ResolveBankDetailsAsync(
            RequestWithdrawalCommand request,
            string userId,
            CancellationToken cancellationToken)
    {
        if (request.BankAccountId is Guid savedId)
        {
            var account = await _context.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == savedId && b.UserId == userId, cancellationToken)
                ?? throw new EntityNotFoundException("BANK_ACCOUNT_NOT_FOUND", nameof(BankAccount), savedId);

            return (account.Id, account.BankName.Trim(), account.AccountNumber.Trim(), account.AccountHolderName.Trim());
        }

        var hasManual = !string.IsNullOrWhiteSpace(request.BankCode)
            || !string.IsNullOrWhiteSpace(request.AccountNumber)
            || !string.IsNullOrWhiteSpace(request.AccountHolderName);

        if (!hasManual)
        {
            var defaultAccount = await _context.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.IsDefault, cancellationToken)
                ?? throw new BusinessRuleValidationException(
                    "BANK_ACCOUNT_REQUIRED",
                    "A saved default bank account or explicit bank details are required.");

            return (
                defaultAccount.Id,
                defaultAccount.BankName.Trim(),
                defaultAccount.AccountNumber.Trim(),
                defaultAccount.AccountHolderName.Trim());
        }

        if (string.IsNullOrWhiteSpace(request.BankCode) ||
            string.IsNullOrWhiteSpace(request.AccountNumber) ||
            string.IsNullOrWhiteSpace(request.AccountHolderName))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_INPUT",
                "Bank code, account number, and account holder name are required.");
        }

        return (
            null,
            request.BankCode.Trim(),
            request.AccountNumber.Trim(),
            request.AccountHolderName.Trim());
    }
}
