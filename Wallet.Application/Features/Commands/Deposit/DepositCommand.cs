namespace Wallet.Application.Features.Commands.Deposit;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;

public record DepositCommand(string UserId, decimal Amount, string Currency, string PaymentMethod) : IRequest<WalletTransactionDto>;

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
        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

        if (wallet == null)
        {
            wallet = new WalletAccount
            {
                UserId = request.UserId,
                Balance = 0m,
                Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency,
                Status = "ACTIVE"
            };
            _context.Wallets.Add(wallet);
        }

        wallet.Deposit(request.Amount);

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId,
            Type = "DEPOSIT",
            Amount = request.Amount,
            Currency = wallet.Currency,
            BalanceAfter = wallet.Balance,
            Status = "SUCCESS",
            Note = $"Nạp tiền qua {request.PaymentMethod}"
        };

        _context.WalletTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return new WalletTransactionDto
        {
            Id = transaction.Id.ToString(),
            WalletId = transaction.WalletId.ToString(),
            UserId = transaction.UserId,
            Type = transaction.Type,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            BalanceAfter = transaction.BalanceAfter,
            Status = transaction.Status,
            Note = transaction.Note,
            CreatedAt = transaction.CreatedAt
        };
    }
}
