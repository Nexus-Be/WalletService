namespace Wallet.Application.Features.Commands.Transfer;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

public record TransferCommand(string SenderUserId, string RecipientUserId, decimal Amount, string Note)
    : IRequest<WalletTransactionDto>;

public class TransferCommandValidator : AbstractValidator<TransferCommand>
{
    public TransferCommandValidator()
    {
        RuleFor(x => x.SenderUserId).NotEmpty().WithMessage("Sender user ID must not be empty.");
        RuleFor(x => x.RecipientUserId).NotEmpty().WithMessage("Recipient user ID must not be empty.");
        RuleFor(x => x.SenderUserId).Must(x => Guid.TryParse(x, out var userId) && userId != Guid.Empty)
            .WithMessage("Sender user ID is invalid.");
        RuleFor(x => x.RecipientUserId).Must(x => Guid.TryParse(x, out var userId) && userId != Guid.Empty)
            .WithMessage("Recipient user ID is invalid.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Transfer amount must be greater than zero.");
        RuleFor(x => x.Amount).Must(x => decimal.Round(x, 2) == x)
            .WithMessage("Transfer amount must have no more than two decimal places.");
        RuleFor(x => x).Must(x => x.SenderUserId != x.RecipientUserId)
            .WithMessage("Cannot transfer funds to yourself.");
    }
}

public class TransferCommandHandler : IRequestHandler<TransferCommand, WalletTransactionDto>
{
    private readonly IWalletDbContext _context;

    public TransferCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletTransactionDto> Handle(TransferCommand request, CancellationToken cancellationToken)
    {
        var senderGuid = Guid.Parse(request.SenderUserId);
        var recipientGuid = Guid.Parse(request.RecipientUserId);
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

        // Deduct from sender
        var senderBefore = senderWallet.Balance;
        senderWallet.Deduct(request.Amount);

        // Deposit to recipient
        var recipientBefore = recipientWallet.Balance;
        recipientWallet.Deposit(request.Amount);

        var senderTx = new WalletTransaction
        {
            WalletId = senderWallet.Id,
            UserId = senderWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferOut,
            Amount = request.Amount,
            Currency = senderWallet.Currency.ToString(),
            BalanceBefore = senderBefore,
            BalanceAfter = senderWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note)
                ? $"Transfer to user {request.RecipientUserId}"
                : request.Note
        };

        var recipientTx = new WalletTransaction
        {
            WalletId = recipientWallet.Id,
            UserId = recipientWallet.UserId.ToString(),
            Type = WalletTransactionType.TransferIn,
            Amount = request.Amount,
            Currency = recipientWallet.Currency.ToString(),
            BalanceBefore = recipientBefore,
            BalanceAfter = recipientWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note)
                ? $"Received transfer from user {request.SenderUserId}"
                : request.Note
        };

        _context.WalletTransactions.Add(senderTx);
        _context.WalletTransactions.Add(recipientTx);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new WalletTransactionDto
        {
            Id = senderTx.Id.ToString(),
            WalletId = senderTx.WalletId.ToString(),
            UserId = senderTx.UserId,
            Type = senderTx.Type.ToString(),
            Amount = senderTx.Amount,
            Currency = senderTx.Currency,
            BalanceAfter = senderTx.BalanceAfter,
            Status = senderTx.Status.ToString(),
            Note = senderTx.Note ?? string.Empty,
            CreatedAt = senderTx.CreatedAt
        };
    }
}