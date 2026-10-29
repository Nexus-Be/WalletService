using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

namespace Wallet.Application.Features.Commands.Deposit;

public record DepositCommand(string UserId, decimal Amount, string Currency, string PaymentMethod)
    : IRequest<WalletTransactionDto>;

public class DepositCommandValidator : AbstractValidator<DepositCommand>
{
    public DepositCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId không được để trống.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Số tiền nạp phải lớn hơn 0.");
    }
}

public class DepositCommandHandler : IRequestHandler<DepositCommand, WalletTransactionDto>
{
    private readonly IWalletDbContext _context;

    public DepositCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletTransactionDto> Handle(DepositCommand request, CancellationToken cancellationToken)
    {
        _ = Guid.TryParse(request.UserId, out var uid);

        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == uid, cancellationToken);

        if (wallet == null)
        {
            wallet = new WalletAccount
            {
                UserId = uid != Guid.Empty ? uid : Guid.NewGuid(),
                Balance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE"
            };
            _context.Wallets.Add(wallet);
        }

        var balanceBefore = wallet.Balance;
        wallet.Deposit(request.Amount);

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.TopupVnpay,
            Amount = request.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            Note = $"Nạp tiền qua {request.PaymentMethod}"
        };

        _context.WalletTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return new WalletTransactionDto
        {
            Id = transaction.Id.ToString(),
            WalletId = transaction.WalletId.ToString(),
            UserId = transaction.UserId,
            Type = transaction.Type.ToString(),
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            BalanceAfter = transaction.BalanceAfter,
            Status = transaction.Status.ToString(),
            Note = transaction.Note ?? string.Empty,
            CreatedAt = transaction.CreatedAt
        };
    }
}