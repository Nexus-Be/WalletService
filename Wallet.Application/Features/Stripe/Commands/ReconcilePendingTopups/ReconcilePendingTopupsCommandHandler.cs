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
        var now = DateTimeOffset.UtcNow;
        var staleCreationCutoff = now.AddMinutes(-10);
        var processed = await PromoteStaleCreationPendingAsync(staleCreationCutoff, cancellationToken);

        var cutoff = now.AddMinutes(-1);

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

        foreach (var candidate in topupCandidates.Concat(directCandidates))
        {
            try
            {
                if (await ReconcileCandidateAsync(candidate.Id, candidate.SessionId!, cancellationToken))
                {
                    processed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (context is DbContext dbContext)
                {
                    dbContext.ChangeTracker.Clear();
                }

                logger.LogError(ex, "Failed to reconcile Stripe payment attempt {AttemptId}.", candidate.Id);
            }
        }

        return processed;
    }

    private async Task<int> PromoteStaleCreationPendingAsync(
        DateTimeOffset staleCutoff,
        CancellationToken cancellationToken)
    {
        var staleAttempts = await context.StripePaymentAttempts
            .AsNoTracking()
            .Where(item => item.Status == StripePaymentAttemptStatus.CreationPending &&
                           item.CheckoutSessionId == null &&
                           item.BlocksReplacement &&
                           item.CreatedAt < staleCutoff)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.Purpose,
                item.WalletTopupId,
                item.DirectPaymentOrderId
            })
            .Take(30)
            .ToArrayAsync(cancellationToken);

        var promoted = 0;
        foreach (var candidate in staleAttempts)
        {
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                if (candidate.Purpose == StripePaymentPurpose.Topup && candidate.WalletTopupId is Guid topupId)
                {
                    var topup = await context.LockTopupByIdAsync(topupId, cancellationToken);
                    if (topup is not null)
                    {
                        await context.ReloadUnmodifiedAsync(topup, cancellationToken);
                    }
                }
                else if (candidate.Purpose == StripePaymentPurpose.DirectOrder &&
                         candidate.DirectPaymentOrderId is Guid directOrderId)
                {
                    var order = await context.LockDirectPaymentOrderByIdAsync(directOrderId, cancellationToken);
                    if (order is not null)
                    {
                        await context.ReloadUnmodifiedAsync(order, cancellationToken);
                    }
                }

                var attempt = await context.LockStripePaymentAttemptAsync(candidate.Id, cancellationToken);
                if (attempt is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                await context.ReloadUnmodifiedAsync(attempt, cancellationToken);
                if (attempt.Status != StripePaymentAttemptStatus.CreationPending ||
                    attempt.CheckoutSessionId is not null ||
                    !attempt.BlocksReplacement)
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                attempt.Status = StripePaymentAttemptStatus.ReconciliationRequired;
                attempt.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                logger.LogCritical(
                    "Stale CreationPending Stripe payment attempt {AttemptId} was promoted to ReconciliationRequired.",
                    attempt.Id);
                promoted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (context is DbContext dbContext)
                {
                    dbContext.ChangeTracker.Clear();
                }

                logger.LogError(ex, "Failed to promote stale CreationPending Stripe payment attempt {AttemptId}.", candidate.Id);
            }
        }

        return promoted;
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
