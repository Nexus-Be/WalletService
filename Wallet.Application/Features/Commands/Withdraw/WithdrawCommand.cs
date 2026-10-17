namespace Wallet.Application.Features.Commands.Withdraw;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;
using Wallet.Domain.Exceptions;

public record WithdrawCommand(string UserId, decimal Amount, string Currency, string BankAccount) : IRequest<WalletTransactionDto>;

public class WithdrawCommandValidator : AbstractValidator<WithdrawCommand>
{
    public WithdrawCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId không được để trống.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Số tiền rút phải lớn hơn 0.");
        RuleFor(x => x.BankAccount).NotEmpty().WithMessage("Tài khoản ngân hàng không được để trống.");
    }
}

public class WithdrawCommandHandler : IRequestHandler<WithdrawCommand, WalletTransactionDto>
{
    private readonly IWalletDbContext _context;

    public WithdrawCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletTransactionDto> Handle(WithdrawCommand request, CancellationToken cancellationToken)
    {
        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

        if (wallet == null)
            throw new WalletNotFoundException(request.UserId);

        wallet.Withdraw(request.Amount);

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId,
            Type = "WITHDRAW",
            Amount = request.Amount,
            Currency = wallet.Currency,
            BalanceAfter = wallet.Balance,
            Status = "SUCCESS",
            Note = $"Rút tiền về tài khoản ngân hàng: {request.BankAccount}"
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
