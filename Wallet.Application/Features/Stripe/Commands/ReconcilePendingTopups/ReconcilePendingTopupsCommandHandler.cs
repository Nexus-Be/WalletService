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
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);

        var topupCandidates = await (from attempt in context.StripePaymentAttempts.AsNoTracking()
            join topup in context.WalletTopups.AsNoTracking() on attempt.WalletTopupId equals topup.Id
            where attempt.Purpose == StripePaymentPurpose.Topup &&
                  attempt.Status == StripePaymentAttemptStatus.AwaitingPayment &&
                  topup.Status == TopupStatus.Pending && attempt.CheckoutSessionId != null &&
                  attempt.CreatedAt < cutoff
            orderby attempt.UpdatedAt, attempt.CreatedAt, attempt.Id
            select new { attempt.Id, SessionId = attempt.CheckoutSessionId }).Take(30).ToArrayAsync(cancellationToken);

        var directCandidates = await (from attempt in context.StripePaymentAttempts.AsNoTracking()
            join order in context.DirectPaymentOrders.AsNoTracking() on attempt.DirectPaymentOrderId equals order.Id
            where attempt.Purpose == StripePaymentPurpose.DirectOrder &&
                  attempt.Status == StripePaymentAttemptStatus.AwaitingPayment &&
                  order.Status == TopupStatus.Pending && attempt.CheckoutSessionId != null &&
                  attempt.CreatedAt < cutoff
            orderby attempt.UpdatedAt, attempt.CreatedAt, attempt.Id
            select new { attempt.Id, SessionId = attempt.CheckoutSessionId }).Take(30).ToArrayAsync(cancellationToken);

        var processed = 0;
        foreach (var candidate in topupCandidates.Concat(directCandidates))
        {
            if (await ReconcileCandidateAsync(candidate.Id, candidate.SessionId!, cancellationToken))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> ReconcileCandidateAsync(
        Guid attemptId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var tracked = await context.StripePaymentAttempts.SingleAsync(
            item => item.Id == attemptId, cancellationToken);
        if (tracked.Status != StripePaymentAttemptStatus.AwaitingPayment)
            return false;

        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var session = await provider.GetSessionAsync(sessionId, cancellationToken);
        if (session.SessionId != sessionId)
            throw new InvalidOperationException("Stripe returned a mismatched checkout session.");

        var intent = session.PaymentIntentId is null
            ? null
            : await provider.GetPaymentIntentAsync(session.PaymentIntentId, cancellationToken);

        var eventData = $"{attemptId:N}\n{session.SessionStatus}\n{session.PaymentStatus}\n{session.PaymentIntentId}";
        var eventId = $"reconcile_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventData)))}";
        var outcome = await StripeSettlement.ApplyAsync(
            context, attemptId, eventId, "reconciliation.poll", session, intent, cancellationToken);

        if (outcome == StripeSettlementOutcome.ReconciliationRequired)
            logger.LogCritical("Stripe payment attempt {AttemptId} requires manual reconciliation.", attemptId);

        return outcome != StripeSettlementOutcome.Pending;
    }
}
