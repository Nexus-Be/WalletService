namespace Wallet.Application.Features.Checkout.Commands.ExpireDirectPayment;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;

internal sealed class ExpireDirectPaymentCommandHandler(
    IWalletDbContext context,
    IStripeCheckoutClient provider)
    : IRequestHandler<ExpireDirectPaymentCommand, DirectPaymentLifecycleResponse>
{
    public Task<DirectPaymentLifecycleResponse> Handle(
        ExpireDirectPaymentCommand request,
        CancellationToken cancellationToken) =>
        StripeDirectLifecycle.CancelOrExpireAsync(
            context,
            provider,
            request.OrderId,
            ownerId: null,
            isInternalExpire: true,
            cancellationToken);
}
