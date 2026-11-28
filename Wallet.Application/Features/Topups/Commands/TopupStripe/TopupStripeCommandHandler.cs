namespace Wallet.Application.Features.Topups.Commands.TopupStripe;

using MediatR;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;

internal sealed class TopupStripeCommandHandler(IWalletDbContext context, IStripeCheckoutClient provider)
    : IRequestHandler<TopupStripeCommand, TopupStripeResponse>
{
    public async Task<TopupStripeResponse> Handle(TopupStripeCommand request, CancellationToken cancellationToken)
    {
        var result = await StripeTopupPreparation.CreateAsync(
            context, provider, request.UserId, request.Amount, request.IdempotencyKey, cancellationToken);
        return new TopupStripeResponse(result.TopupId, result.PaymentUrl, result.Amount, "Vnd", "Pending", result.CreatedAt);
    }
}
