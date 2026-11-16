namespace Wallet.Application.Features.Topups.Queries.GetTopupStatus;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;

internal sealed class GetTopupStatusQueryHandler(IWalletDbContext context)
    : IRequestHandler<GetTopupStatusQuery, TopupStatusResponse>
{
    public async Task<TopupStatusResponse> Handle(GetTopupStatusQuery request, CancellationToken cancellationToken)
    {
        var result = await StripeTopupStatus.GetAsync(context, request.UserId, request.TopupId, cancellationToken);
        var attempt = await context.StripePaymentAttempts.AsNoTracking()
            .Where(item => item.OwnerId == request.UserId && item.WalletTopupId == request.TopupId &&
                           item.Purpose == StripePaymentPurpose.Topup)
            .OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var canResume = result.Status == "Pending" && attempt?.Status == StripePaymentAttemptStatus.AwaitingPayment &&
                        attempt.SessionExpiresAt > DateTimeOffset.UtcNow && !string.IsNullOrWhiteSpace(attempt.CheckoutUrl);
        return new TopupStatusResponse(
            result.TransactionId, result.Amount, result.Currency, result.Status, result.CreatedAt, result.UpdatedAt,
            canResume, attempt?.SessionExpiresAt);
    }
}
