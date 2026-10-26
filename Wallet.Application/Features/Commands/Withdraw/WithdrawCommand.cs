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
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId không được để trống.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Số tiền rút phải lớn hơn 0.");
        RuleFor(x => x.BankAccount).NotEmpty().WithMessage("Tài khoản ngân hàng không được để trống.");
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