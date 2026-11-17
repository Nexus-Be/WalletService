namespace Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
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
            orderby attempt.UpdatedAt, attempt.CreatedAt, attempt.Id
            select new { attempt.Id, SessionId = attempt.CheckoutSessionId }).Take(30).ToArrayAsync(cancellationToken);
        var processed = 0;
        foreach (var candidate in candidates)
        {
            var tracked = await context.StripePaymentAttempts.SingleAsync(
                item => item.Id == candidate.Id, cancellationToken);
            if (tracked.Status != StripePaymentAttemptStatus.AwaitingPayment)
                continue;
            tracked.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            var session = await provider.GetSessionAsync(candidate.SessionId!, cancellationToken);
            if (session.SessionId != candidate.SessionId)
                throw new InvalidOperationException("Stripe returned a mismatched checkout session.");
            var intent = session.PaymentIntentId is null ? null :
                await provider.GetPaymentIntentAsync(session.PaymentIntentId, cancellationToken);
            var eventData = $"{candidate.Id:N}\n{session.SessionStatus}\n{session.PaymentStatus}\n{session.PaymentIntentId}";
            var eventId = $"reconcile_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventData)))}";
            var outcome = await StripeSettlement.ApplyAsync(context, candidate.Id, eventId,
                "reconciliation.poll", session, intent, cancellationToken);
            if (outcome == StripeSettlementOutcome.ReconciliationRequired)
                logger.LogCritical("Stripe topup attempt {AttemptId} requires manual reconciliation.", candidate.Id);
            if (outcome != StripeSettlementOutcome.Pending) processed++;
        }
        return processed;
    }
}
