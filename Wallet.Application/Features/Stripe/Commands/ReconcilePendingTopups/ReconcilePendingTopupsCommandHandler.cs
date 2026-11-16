namespace Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;

internal sealed class ReconcilePendingTopupsCommandHandler(
    IWalletDbContext context, IStripeCheckoutClient provider,
    ILogger<ReconcilePendingTopupsCommandHandler> logger)
    : IRequestHandler<ReconcilePendingTopupsCommand, int>
{
    public async Task<int> Handle(ReconcilePendingTopupsCommand request, CancellationToken cancellationToken)
    {
        var candidates = await (from attempt in context.StripePaymentAttempts.AsNoTracking()
            join topup in context.WalletTopups.AsNoTracking() on attempt.WalletTopupId equals topup.Id
            where attempt.Purpose == StripePaymentPurpose.Topup &&
                  attempt.Status == StripePaymentAttemptStatus.AwaitingPayment &&
                  topup.Status == TopupStatus.Pending && attempt.CheckoutSessionId != null &&
                  attempt.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-1)
            orderby attempt.UpdatedAt, attempt.CreatedAt
            select new { attempt.Id, SessionId = attempt.CheckoutSessionId }).Take(30).ToArrayAsync(cancellationToken);
        var processed = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var session = await provider.GetSessionAsync(candidate.SessionId!, cancellationToken);
                if (session.SessionId != candidate.SessionId) continue;
                var intent = session.PaymentIntentId is null ? null :
                    await provider.GetPaymentIntentAsync(session.PaymentIntentId, cancellationToken);
                var eventId = $"reconcile_{candidate.Id:N}_{session.SessionStatus}_{session.PaymentStatus}_{session.PaymentIntentId ?? "none"}";
                var outcome = await StripeSettlement.ApplyAsync(context, candidate.Id, eventId,
                    "reconciliation.poll", session, intent, cancellationToken);
                if (outcome == StripeSettlementOutcome.Pending)
                {
                    var tracked = await context.StripePaymentAttempts.SingleAsync(
                        item => item.Id == candidate.Id, cancellationToken);
                    tracked.UpdatedAt = DateTimeOffset.UtcNow;
                    await context.SaveChangesAsync(cancellationToken);
                }
                if (outcome != StripeSettlementOutcome.Pending) processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogError(error, "Unable to reconcile Stripe topup attempt {AttemptId}", candidate.Id);
            }
        }
        return processed;
    }
}
