namespace Wallet.Application.Features.Commands.Transfer;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

public record TransferCommand(string SenderUserId, string RecipientUserId, decimal Amount, string Note) : IRequest<WalletTransactionDto>;

public class TransferCommandValidator : AbstractValidator<TransferCommand>
{
    public TransferCommandValidator()
    {
        RuleFor(x => x.SenderUserId).NotEmpty().WithMessage("Người gửi không được để trống.");
        RuleFor(x => x.RecipientUserId).NotEmpty().WithMessage("Người nhận không được để trống.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Số tiền chuyển phải lớn hơn 0.");
        RuleFor(x => x).Must(x => x.SenderUserId != x.RecipientUserId).WithMessage("Không thể chuyển tiền cho chính mình.");
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
        _ = Guid.TryParse(request.SenderUserId, out var senderGuid);
        _ = Guid.TryParse(request.RecipientUserId, out var recipientGuid);

        var senderWallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == senderGuid, cancellationToken);

        if (senderWallet == null)
            throw new WalletNotFoundException(request.SenderUserId);

        var recipientWallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == recipientGuid, cancellationToken);

        if (recipientWallet == null)
        {
            recipientWallet = new WalletAccount
            {
                UserId = recipientGuid != Guid.Empty ? recipientGuid : Guid.NewGuid(),
                Balance = 0m,
                Currency = senderWallet.Currency,
                Status = "ACTIVE"
            };
            _context.Wallets.Add(recipientWallet);
        }

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
            Type = WalletTransactionType.AdjustmentDebit,
            Amount = request.Amount,
            Currency = senderWallet.Currency.ToString(),
            BalanceBefore = senderBefore,
            BalanceAfter = senderWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note) ? $"Chuyển tiền tới user {request.RecipientUserId}" : request.Note
        };

        var recipientTx = new WalletTransaction
        {
            WalletId = recipientWallet.Id,
            UserId = recipientWallet.UserId.ToString(),
            Type = WalletTransactionType.AdjustmentCredit,
            Amount = request.Amount,
            Currency = recipientWallet.Currency.ToString(),
            BalanceBefore = recipientBefore,
            BalanceAfter = recipientWallet.Balance,
            Status = TransactionStatus.Success,
            Note = string.IsNullOrWhiteSpace(request.Note) ? $"Nhận tiền từ user {request.SenderUserId}" : request.Note
        };

        _context.WalletTransactions.Add(senderTx);
        _context.WalletTransactions.Add(recipientTx);

        await _context.SaveChangesAsync(cancellationToken);

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
