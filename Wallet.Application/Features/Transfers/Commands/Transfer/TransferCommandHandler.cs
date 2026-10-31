namespace Wallet.Application.Features.Transfers.Commands.Transfer;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
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
        if (senderWallet.Currency != recipientWallet.Currency)
            throw new BusinessRuleValidationException("Wallet currencies do not match.");

        senderWallet.Deduct(request.Amount);
        recipientWallet.Deposit(request.Amount);

        var senderTx = new WalletTransaction
        {
            WalletId = senderWallet.Id,
            UserId = senderWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferOut,
            Amount = request.Amount,
            Currency = senderWallet.Currency.ToString(),
            BalanceBefore = senderWallet.Balance + request.Amount,
            BalanceAfter = senderWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note) ? $"Transfer to {recipientGuid}" : request.Note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(senderTx);

        var recipientTx = new WalletTransaction
        {
            WalletId = recipientWallet.Id,
            UserId = recipientWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferIn,
            Amount = request.Amount,
            Currency = recipientWallet.Currency.ToString(),
            BalanceBefore = recipientWallet.Balance - request.Amount,
            BalanceAfter = recipientWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note) ? $"Transfer from {senderGuid}" : request.Note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.WalletTransactions.Add(recipientTx);

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
}
