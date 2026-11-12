namespace Wallet.Application.Features.Stripe;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

internal enum StripeSettlementOutcome
{
    Settled,
    AlreadySettled,
    DuplicateEvent,
    Pending,
    FinalizedFailure,
    ReconciliationRequired
}

internal static class StripeSettlement
{
    // The caller must verify the webhook and retrieve snapshots from the configured Stripe account.
    internal static async Task<StripeSettlementOutcome> ApplyAsync(
        IWalletDbContext context,
        Guid attemptId,
        string eventId,
        string eventType,
        StripeSessionSnapshot session,
        StripePaymentIntentSnapshot? intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        if (attemptId == Guid.Empty || string.IsNullOrWhiteSpace(eventId) || eventId.Length > 255 ||
            string.IsNullOrWhiteSpace(eventType) || eventType.Length > 128)
            throw new ArgumentException("Stripe event identity is invalid.");

        context.EnsureNoPendingChanges();
        var identity = await context.StripePaymentAttempts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Stripe payment attempt was not found.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        WalletTopup? topup = null;
        DirectPaymentOrder? order = null;
        if (identity.Purpose == StripePaymentPurpose.Topup && identity.WalletTopupId is Guid topupId)
            topup = await context.LockTopupByIdAsync(topupId, cancellationToken)
                ?? throw new InvalidOperationException("Stripe topup was not found.");
        else if (identity.Purpose == StripePaymentPurpose.DirectOrder && identity.DirectPaymentOrderId is Guid orderId)
            order = await context.LockDirectPaymentOrderByIdAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException("Stripe direct payment order was not found.");
        else
            throw new InvalidOperationException("Stripe payment attempt has an invalid parent.");

        if (topup is not null)
            await context.ReloadUnmodifiedAsync(topup, cancellationToken);
        else
            await context.ReloadUnmodifiedAsync(order!, cancellationToken);
        var attempt = await context.LockStripePaymentAttemptAsync(attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Stripe payment attempt was not found.");
        await context.ReloadUnmodifiedAsync(attempt, cancellationToken);
        if (attempt.Purpose != identity.Purpose || attempt.WalletTopupId != identity.WalletTopupId ||
            attempt.DirectPaymentOrderId != identity.DirectPaymentOrderId)
            throw new InvalidOperationException("Stripe payment attempt parent changed.");

        var previousEvent = await context.StripeWebhookEvents.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProviderEventId == eventId, cancellationToken);
        if (previousEvent is not null)
        {
            if (previousEvent.PaymentAttemptId != attemptId || previousEvent.EventType != eventType)
                throw new InvalidOperationException("Stripe event identity conflicts with an existing event.");
            await transaction.CommitAsync(cancellationToken);
            return StripeSettlementOutcome.DuplicateEvent;
        }

        var providerOutcome = StripeReconciliationPolicy.Evaluate(attempt, session, intent);
        if (providerOutcome == StripeReconciliationOutcome.Pending)
            return StripeSettlementOutcome.Pending;

        var outcome = StripeSettlementOutcome.ReconciliationRequired;
        if (providerOutcome == StripeReconciliationOutcome.ConfirmedPaid &&
            attempt.Status != StripePaymentAttemptStatus.ReconciliationRequired)
        {
            outcome = topup is not null
                ? await SettleTopup(context, attempt, topup, cancellationToken)
                : await SettleDirect(context, attempt, order!, cancellationToken);
        }
        else if (providerOutcome == StripeReconciliationOutcome.CannotSucceed &&
                 attempt.Status is not (StripePaymentAttemptStatus.ReconciliationRequired or StripePaymentAttemptStatus.Successful))
        {
            outcome = await FinalizeFailure(context, attempt, topup, order, session.SessionStatus, cancellationToken);
        }

        if (outcome == StripeSettlementOutcome.ReconciliationRequired &&
            attempt.Status != StripePaymentAttemptStatus.Successful)
            attempt.Status = StripePaymentAttemptStatus.ReconciliationRequired;
        if (outcome is StripeSettlementOutcome.Settled or StripeSettlementOutcome.AlreadySettled or
            StripeSettlementOutcome.FinalizedFailure)
        {
            attempt.CheckoutSessionId ??= session.SessionId;
            attempt.PaymentIntentId ??= intent?.PaymentIntentId;
        }
        attempt.UpdatedAt = DateTimeOffset.UtcNow;
        context.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            Id = Guid.NewGuid(),
            ProviderEventId = eventId,
            EventType = eventType,
            PaymentAttemptId = attempt.Id,
            Outcome = outcome.ToString()
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private static async Task<StripeSettlementOutcome> FinalizeFailure(
            IWalletDbContext context, StripePaymentAttempt attempt, WalletTopup? topup,
            DirectPaymentOrder? order, string sessionStatus, CancellationToken token)
    {
        if (!attempt.BlocksReplacement)
            return attempt.Status is StripePaymentAttemptStatus.Failed or StripePaymentAttemptStatus.Expired
                ? StripeSettlementOutcome.FinalizedFailure
                : StripeSettlementOutcome.ReconciliationRequired;
        var target = sessionStatus == "expired" ? TopupStatus.Expired : TopupStatus.Failed;
        if (topup is not null)
        {
            if (topup.VnpTxnRef is not null || topup.Amount != attempt.Amount ||
                topup.Status is not (TopupStatus.Pending or TopupStatus.Failed or TopupStatus.Expired))
                return StripeSettlementOutcome.ReconciliationRequired;
            var wallet = await context.LockWalletByIdAsync(topup.WalletId, token);
            if (wallet is null)
                return StripeSettlementOutcome.ReconciliationRequired;
            await context.ReloadUnmodifiedAsync(wallet, token);
            if (wallet.UserId != attempt.OwnerId || wallet.Currency != CurrencyCode.Vnd ||
                await context.WalletTransactions.AnyAsync(
                    item => item.ReferenceId == topup.Id && item.Type == WalletTransactionType.TopupStripe, token))
                return StripeSettlementOutcome.ReconciliationRequired;
            if (topup.Status == TopupStatus.Pending)
                context.AddAuditLog(AuditAction.TopupFail, nameof(WalletTopup), topup.Id.ToString(),
                    newValues: new { topup.Amount, Status = target, AttemptId = attempt.Id }, userId: wallet.UserId.ToString());
            topup.Status = target;
            topup.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            if (order is null || order.VnpTxnRef is not null || order.Amount != attempt.Amount ||
                order.Status is not (TopupStatus.Pending or TopupStatus.Failed or TopupStatus.Expired))
                return StripeSettlementOutcome.ReconciliationRequired;
            var ledger = await context.WalletTransactions.SingleOrDefaultAsync(
                item => item.ReferenceId == order.OrderId && item.Type == WalletTransactionType.DirectGatewayPayment, token);
            if (ledger is null)
                return StripeSettlementOutcome.ReconciliationRequired;
            await context.ReloadUnmodifiedAsync(ledger, token);
            if (ledger.ReferenceId != order.OrderId || ledger.Type != WalletTransactionType.DirectGatewayPayment ||
                ledger.Amount != attempt.Amount || ledger.UserId != attempt.OwnerId.ToString() ||
                ledger.Status is not (TransactionStatus.Pending or TransactionStatus.Failed))
                return StripeSettlementOutcome.ReconciliationRequired;
            var wallet = await context.LockWalletByIdAsync(ledger.WalletId, token);
            if (wallet is null)
                return StripeSettlementOutcome.ReconciliationRequired;
            await context.ReloadUnmodifiedAsync(wallet, token);
            if (wallet.UserId != attempt.OwnerId || wallet.Currency != CurrencyCode.Vnd)
                return StripeSettlementOutcome.ReconciliationRequired;
            if (order.Status == TopupStatus.Pending)
                context.AddAuditLog(AuditAction.DirectPaymentFail, nameof(DirectPaymentOrder), order.Id.ToString(),
                    newValues: new { order.OrderId, order.Amount, Status = target, AttemptId = attempt.Id },
                    userId: wallet.UserId.ToString());
            order.Status = target;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            ledger.Status = TransactionStatus.Failed;
            ledger.UpdatedAt = DateTimeOffset.UtcNow;
        }

        attempt.Status = target == TopupStatus.Expired
            ? StripePaymentAttemptStatus.Expired : StripePaymentAttemptStatus.Failed;
        attempt.BlocksReplacement = false;
        return StripeSettlementOutcome.FinalizedFailure;
    }
    private static async Task<StripeSettlementOutcome> SettleTopup(
        IWalletDbContext context, StripePaymentAttempt attempt, WalletTopup topup, CancellationToken token)
    {
        if (topup.VnpTxnRef is not null || topup.Amount != attempt.Amount)
            return StripeSettlementOutcome.ReconciliationRequired;
        var wallet = await context.LockWalletByIdAsync(topup.WalletId, token);
        if (wallet is null)
            return StripeSettlementOutcome.ReconciliationRequired;
        await context.ReloadUnmodifiedAsync(wallet, token);
        if (wallet.UserId != attempt.OwnerId || wallet.Currency != CurrencyCode.Vnd)
            return StripeSettlementOutcome.ReconciliationRequired;

        if (topup.Status == TopupStatus.Success)
        {
            var matches = await context.WalletTransactions.AsNoTracking().Where(
                item => item.ReferenceId == topup.Id && item.Type == WalletTransactionType.TopupStripe)
                .ToListAsync(token);
            return attempt.Status == StripePaymentAttemptStatus.Successful && matches.Count == 1 &&
                   matches[0].WalletId == wallet.Id && matches[0].UserId == wallet.UserId.ToString() &&
                   matches[0].Amount == topup.Amount && matches[0].Status == TransactionStatus.Success
                ? StripeSettlementOutcome.AlreadySettled
                : StripeSettlementOutcome.ReconciliationRequired;
        }
        if (topup.Status is not (TopupStatus.Pending or TopupStatus.Failed or TopupStatus.Expired))
            return StripeSettlementOutcome.ReconciliationRequired;
        if (attempt.Status == StripePaymentAttemptStatus.Successful ||
            await context.WalletTransactions.AnyAsync(
                item => item.ReferenceId == topup.Id && item.Type == WalletTransactionType.TopupStripe, token))
            return StripeSettlementOutcome.ReconciliationRequired;

        var before = wallet.Balance;
        wallet.Deposit(topup.Amount);
        topup.Status = TopupStatus.Success;
        topup.UpdatedAt = DateTimeOffset.UtcNow;
        attempt.Status = StripePaymentAttemptStatus.Successful;
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.TopupStripe,
            Amount = topup.Amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = before,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = topup.Id,
            Note = "Stripe topup"
        });
        context.AddAuditLog(AuditAction.TopupSuccess, nameof(WalletTopup), topup.Id.ToString(),
            newValues: new { topup.Amount, topup.Status, AttemptId = attempt.Id }, userId: wallet.UserId.ToString());
        return StripeSettlementOutcome.Settled;
    }

    private static async Task<StripeSettlementOutcome> SettleDirect(
        IWalletDbContext context, StripePaymentAttempt attempt, DirectPaymentOrder order, CancellationToken token)
    {
        if (order.VnpTxnRef is not null || order.Amount != attempt.Amount)
            return StripeSettlementOutcome.ReconciliationRequired;
        var ledger = await context.WalletTransactions.SingleOrDefaultAsync(
            item => item.ReferenceId == order.OrderId && item.Type == WalletTransactionType.DirectGatewayPayment, token);
        if (ledger is null)
            return StripeSettlementOutcome.ReconciliationRequired;
        await context.ReloadUnmodifiedAsync(ledger, token);
        if (ledger.ReferenceId != order.OrderId || ledger.Type != WalletTransactionType.DirectGatewayPayment ||
            ledger.Amount != attempt.Amount || ledger.UserId != attempt.OwnerId.ToString())
            return StripeSettlementOutcome.ReconciliationRequired;
        var wallet = await context.LockWalletByIdAsync(ledger.WalletId, token);
        if (wallet is null)
            return StripeSettlementOutcome.ReconciliationRequired;
        await context.ReloadUnmodifiedAsync(wallet, token);
        if (wallet.UserId != attempt.OwnerId || wallet.Currency != CurrencyCode.Vnd)
            return StripeSettlementOutcome.ReconciliationRequired;

        if (await context.WalletTransactions.AnyAsync(item =>
                item.ReferenceId == order.OrderId && item.Type == WalletTransactionType.PurchaseDebit &&
                item.Status != TransactionStatus.Failed && item.Status != TransactionStatus.Cancelled, token))
            return StripeSettlementOutcome.ReconciliationRequired;

        if (order.Status == TopupStatus.Success)
            return attempt.Status == StripePaymentAttemptStatus.Successful && ledger.Status == TransactionStatus.Success
                ? StripeSettlementOutcome.AlreadySettled
                : StripeSettlementOutcome.ReconciliationRequired;
        if (order.Status is not (TopupStatus.Pending or TopupStatus.Failed or TopupStatus.Expired) ||
            ledger.Status is not (TransactionStatus.Pending or TransactionStatus.Failed))
            return StripeSettlementOutcome.ReconciliationRequired;
        if (attempt.Status == StripePaymentAttemptStatus.Successful || ledger.Status == TransactionStatus.Success)
            return StripeSettlementOutcome.ReconciliationRequired;

        order.Status = TopupStatus.Success;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        ledger.Status = TransactionStatus.Success;
        ledger.UpdatedAt = DateTimeOffset.UtcNow;
        attempt.Status = StripePaymentAttemptStatus.Successful;
        context.AddAuditLog(AuditAction.DirectPaymentSuccess, nameof(DirectPaymentOrder), order.Id.ToString(),
            newValues: new { order.OrderId, order.Amount, order.Status, AttemptId = attempt.Id },
            userId: wallet.UserId.ToString());
        return StripeSettlementOutcome.Settled;
    }
}
