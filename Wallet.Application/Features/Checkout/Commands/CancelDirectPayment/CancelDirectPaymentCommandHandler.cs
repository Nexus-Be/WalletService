namespace Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;

internal sealed class CancelDirectPaymentCommandHandler(
    IWalletDbContext context,
    IStripeCheckoutClient provider)
    : IRequestHandler<CancelDirectPaymentCommand, DirectPaymentLifecycleResponse>
{
    public Task<DirectPaymentLifecycleResponse> Handle(
        CancelDirectPaymentCommand request,
        CancellationToken cancellationToken) =>
        StripeDirectLifecycle.CancelOrExpireAsync(
            context,
            provider,
            request.OrderId,
            request.UserId,
            isInternalExpire: false,
            cancellationToken);
}
