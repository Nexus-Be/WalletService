namespace Wallet.Application.Features.Transfers.Commands.Transfer;

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

internal sealed class TransferCommandHandler : IRequestHandler<TransferCommand, TransferResponse>
{
    private readonly IWalletDbContext _context;

    public TransferCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<TransferResponse> Handle(TransferCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.SenderUserId, out var senderGuid) || senderGuid == Guid.Empty)
            throw new ArgumentException("Sender user ID is invalid.", nameof(request));
        if (!Guid.TryParse(request.RecipientUserId, out var recipientGuid) || recipientGuid == Guid.Empty)
            throw new ArgumentException("Recipient user ID is invalid.", nameof(request));
        if (senderGuid == recipientGuid)
            throw new BusinessRuleValidationException("Cannot transfer funds to yourself.");

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();
        if (idempotencyKey is not null && !WithdrawalWorkflow.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_IDEMPOTENCY_KEY",
                "Idempotency-Key header must be 1 to 100 ASCII letters, digits, hyphens, or underscores.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var fingerprint = idempotencyKey is null
            ? null
            : ComputeFingerprint(senderGuid, recipientGuid, request.Amount, trimmedNote ?? string.Empty);

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var firstUserId = senderGuid.CompareTo(recipientGuid) < 0 ? senderGuid : recipientGuid;
        var secondUserId = firstUserId == senderGuid ? recipientGuid : senderGuid;
        var firstWallet = await _context.LockWalletByUserIdAsync(firstUserId, cancellationToken);
        var secondWallet = await _context.LockWalletByUserIdAsync(secondUserId, cancellationToken);
        var senderWallet = firstUserId == senderGuid ? firstWallet : secondWallet;
        var recipientWallet = firstUserId == recipientGuid ? firstWallet : secondWallet;

        if (senderWallet is null)
            throw new WalletNotFoundException(request.SenderUserId);
        if (recipientWallet is null)
            throw new WalletNotFoundException(request.RecipientUserId);

        if (idempotencyKey is not null)
        {
            var existing = await _context.WalletTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.WalletId == senderWallet.Id &&
                         t.Type == WalletTransactionType.TransferOut &&
                         t.ClientIdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new BusinessRuleValidationException(
                        "IDEMPOTENCY_KEY_REUSED",
                        "The idempotency key was already used for a different transfer request.",
                        409);
                }

                await dbTransaction.CommitAsync(cancellationToken);
                return new TransferResponse(
                    existing.Id.ToString(),
                    existing.Amount,
                    existing.Currency ?? senderWallet.Currency.ToString(),
                    existing.Status.ToString(),
                    existing.CreatedAt);
            }
        }

        if (senderWallet.Currency != recipientWallet.Currency)
            throw new BusinessRuleValidationException("Wallet currencies do not match.");

        senderWallet.EnsureActive();
        recipientWallet.EnsureActive();

        var senderBalanceBefore = senderWallet.Balance;
        var recipientBalanceBefore = recipientWallet.Balance;
        senderWallet.Deduct(request.Amount);
        recipientWallet.Deposit(request.Amount);

        var transferId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var senderTx = new WalletTransaction
        {
            WalletId = senderWallet.Id,
            UserId = senderWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferOut,
            Amount = request.Amount,
            Currency = senderWallet.Currency.ToString(),
            BalanceBefore = senderBalanceBefore,
            BalanceAfter = senderWallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = transferId,
            Note = trimmedNote ?? $"Transfer to {recipientGuid}",
            ClientIdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.WalletTransactions.Add(senderTx);

        var recipientTx = new WalletTransaction
        {
            WalletId = recipientWallet.Id,
            UserId = recipientWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferIn,
            Amount = request.Amount,
            Currency = recipientWallet.Currency.ToString(),
            BalanceBefore = recipientBalanceBefore,
            BalanceAfter = recipientWallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = transferId,
            Note = trimmedNote ?? $"Transfer from {senderGuid}",
            ClientIdempotencyKey = null,
            RequestFingerprint = null,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.WalletTransactions.Add(recipientTx);

        _context.AddAuditLog(
            action: AuditAction.Transfer,
            entityName: nameof(WalletTransaction),
            entityId: transferId.ToString(),
            oldValues: new
            {
                SenderBalance = senderBalanceBefore,
                RecipientBalance = recipientBalanceBefore
            },
            newValues: new
            {
                TransferId = transferId,
                SenderWalletId = senderWallet.Id,
                RecipientWalletId = recipientWallet.Id,
                request.Amount,
                Currency = senderWallet.Currency.ToString(),
                SenderBalance = senderWallet.Balance,
                RecipientBalance = recipientWallet.Balance,
                SenderTransactionId = senderTx.Id,
                RecipientTransactionId = recipientTx.Id
            },
            userId: senderGuid.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new TransferResponse(
            senderTx.Id.ToString(),
            senderTx.Amount,
            senderTx.Currency,
            senderTx.Status.ToString(),
            senderTx.CreatedAt
        );
    }

    private static string ComputeFingerprint(Guid senderId, Guid recipientId, decimal amount, string note)
    {
        var canonical = string.Join(
            '\n',
            senderId.ToString("D"),
            recipientId.ToString("D"),
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            note);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
