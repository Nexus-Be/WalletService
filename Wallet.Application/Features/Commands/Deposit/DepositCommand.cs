namespace Wallet.Application.Features.Commands.Deposit;

using FluentValidation;
using MediatR;
using Wallet.Application.DTOs;
using Wallet.Domain.Exceptions;

public record DepositCommand(string UserId, decimal Amount, string Currency, string PaymentMethod)
    : IRequest<WalletTransactionDto>;

public class DepositCommandValidator : AbstractValidator<DepositCommand>
{
    public DepositCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId must not be empty.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Deposit amount must be greater than zero.");
    }
}

public class DepositCommandHandler : IRequestHandler<DepositCommand, WalletTransactionDto>
{
    public Task<WalletTransactionDto> Handle(DepositCommand request, CancellationToken cancellationToken)
    {
        throw new BusinessRuleValidationException(
            "Direct wallet credit is disabled. Initiate a VNPay top-up and wait for its signed IPN.");
    }
}