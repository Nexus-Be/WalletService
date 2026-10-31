namespace Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using global::Wallet.Domain.Exceptions;

internal sealed class RequestWithdrawalCommandHandler : IRequestHandler<RequestWithdrawalCommand, WithdrawalResponse>
{
    public Task<WithdrawalResponse> Handle(RequestWithdrawalCommand request, CancellationToken cancellationToken)
    {
        throw new BusinessRuleValidationException(
            "Direct wallet withdrawal is disabled. Submit a withdrawal request for staff approval.");
    }
}
