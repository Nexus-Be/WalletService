namespace Wallet.Application.Features.Commands.Withdraw;

using FluentValidation;
using MediatR;
using Wallet.Application.DTOs;
using Wallet.Domain.Exceptions;

public record WithdrawCommand(string UserId, decimal Amount, string Currency, string BankAccount)
    : IRequest<WalletTransactionDto>;

public class WithdrawCommandValidator : AbstractValidator<WithdrawCommand>
{
    public WithdrawCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId must not be empty.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Withdrawal amount must be greater than zero.");
        RuleFor(x => x.BankAccount).NotEmpty().WithMessage("Bank account must not be empty.");
    }
}

public class WithdrawCommandHandler : IRequestHandler<WithdrawCommand, WalletTransactionDto>
{
    public Task<WalletTransactionDto> Handle(WithdrawCommand request, CancellationToken cancellationToken)
    {
        throw new BusinessRuleValidationException(
            "Direct wallet withdrawal is disabled. Submit a withdrawal request for staff approval.");
    }
}