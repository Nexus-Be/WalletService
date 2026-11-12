namespace Wallet.Application.Features.Topups.Queries.GetTopupStatus;

using MediatR;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;

internal sealed class GetTopupStatusQueryHandler(IWalletDbContext context)
    : IRequestHandler<GetTopupStatusQuery, TopupStatusResponse>
{
    public async Task<TopupStatusResponse> Handle(GetTopupStatusQuery request, CancellationToken cancellationToken)
    {
        var result = await StripeTopupStatus.GetAsync(context, request.UserId, request.TopupId, cancellationToken);
        return new TopupStatusResponse(
            result.TransactionId, result.Amount, result.Currency, result.Status, result.CreatedAt, result.UpdatedAt);
    }
}
