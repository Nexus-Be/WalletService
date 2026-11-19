namespace Wallet.Application.Features.Stripe;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal static class StripeDirectLifecycle
{
    internal static async Task<DirectPaymentLifecycleResponse> CancelOrExpireAsync(
        IWalletDbContext context,
        IStripeCheckoutClient provider,
        Guid orderId,
        Guid? ownerId,
        bool isInternalExpire,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);

        context.EnsureNoPendingChanges();

        var order = await context.DirectPaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.VnpTxnRef == null, cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", orderId);

        var attempt = await context.StripePaymentAttempts
            .AsNoTracking()
            .Where(item => item.DirectPaymentOrderId == order.Id &&
                           item.Purpose == StripePaymentPurpose.DirectOrder)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", orderId);

        if (!isInternalExpire && attempt.OwnerId != ownerId)
        {
            throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", orderId);
        }

        if (order.Status == TopupStatus.Success || attempt.Status == StripePaymentAttemptStatus.Successful)
        {
            throw AlreadyCompleted();
        }

        if (!attempt.BlocksReplacement &&
            attempt.Status is StripePaymentAttemptStatus.Expired or StripePaymentAttemptStatus.Failed)
        {
            return new DirectPaymentLifecycleResponse(
                orderId,
                order.Status.ToString(),
                attempt.Status.ToString(),
                attempt.UpdatedAt);
        }

        if (attempt.Status == StripePaymentAttemptStatus.CreationPending ||
            attempt.Status == StripePaymentAttemptStatus.ReconciliationRequired ||
            string.IsNullOrWhiteSpace(attempt.CheckoutSessionId))
        {
            throw PendingCancellation();
        }

        var session = await provider.GetSessionAsync(attempt.CheckoutSessionId, cancellationToken);
        if (!string.Equals(session.SessionId, attempt.CheckoutSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stripe returned a mismatched checkout session.");
        }

        if (string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw AlreadyCompleted();
        }

        if (string.Equals(session.SessionStatus, "open", StringComparison.OrdinalIgnoreCase))
        {
            session = await provider.ExpireSessionAsync(attempt.CheckoutSessionId, cancellationToken);
            if (!string.Equals(session.SessionId, attempt.CheckoutSessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Stripe returned a mismatched checkout session.");
            }
        }

        if (string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw AlreadyCompleted();
        }

        if (session.PaymentIntentId is null)
        {
            if (!string.Equals(session.SessionStatus, "expired", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(session.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase) ||
                session.RecoveryEnabled != false)
            {
                throw PendingCancellation();
            }
        }
        else
        {
            var intent = await provider.GetPaymentIntentAsync(session.PaymentIntentId, cancellationToken);
            if (!string.Equals(intent.PaymentIntentId, session.PaymentIntentId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Stripe returned a mismatched payment intent.");
            }

            if (string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
                intent.AmountReceived > 0)
            {
                throw AlreadyCompleted();
            }

            if (!string.Equals(session.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(intent.Status, "canceled", StringComparison.OrdinalIgnoreCase) ||
                intent.AmountReceived != 0 ||
                session.RecoveryEnabled != false ||
                !(string.Equals(session.SessionStatus, "expired", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(session.SessionStatus, "complete", StringComparison.OrdinalIgnoreCase)))
            {
                throw PendingCancellation();
            }
        }

        context.EnsureNoPendingChanges();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockOrderPaymentAsync(orderId, cancellationToken);

        var lockedOrder = await context.LockDirectPaymentOrderByIdAsync(order.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", orderId);
        await context.ReloadUnmodifiedAsync(lockedOrder, cancellationToken);

        var lockedAttempt = await context.LockStripePaymentAttemptAsync(attempt.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", orderId);
        await context.ReloadUnmodifiedAsync(lockedAttempt, cancellationToken);

        if (lockedOrder.Status == TopupStatus.Success ||
            lockedAttempt.Status == StripePaymentAttemptStatus.Successful)
        {
            throw AlreadyCompleted();
        }

        if (!lockedAttempt.BlocksReplacement &&
            lockedAttempt.Status is StripePaymentAttemptStatus.Expired or StripePaymentAttemptStatus.Failed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DirectPaymentLifecycleResponse(
                orderId,
                lockedOrder.Status.ToString(),
                lockedAttempt.Status.ToString(),
                lockedAttempt.UpdatedAt);
        }

        var now = DateTimeOffset.UtcNow;
        var oldStatus = lockedAttempt.Status;
        lockedAttempt.Status = StripePaymentAttemptStatus.Expired;
        lockedAttempt.BlocksReplacement = false;
        lockedAttempt.UpdatedAt = now;

        lockedOrder.Status = TopupStatus.Failed;
        lockedOrder.UpdatedAt = now;

        var pendingTransactions = await context.WalletTransactions
            .Where(item => item.ReferenceId == orderId &&
                           item.Type == WalletTransactionType.DirectGatewayPayment &&
                           item.Status == TransactionStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var tx in pendingTransactions)
        {
            tx.Status = isInternalExpire ? TransactionStatus.Failed : TransactionStatus.Cancelled;
            tx.UpdatedAt = now;
        }

        context.AddAuditLog(
            isInternalExpire ? AuditAction.DirectPaymentExpire : AuditAction.DirectPaymentCancel,
            nameof(DirectPaymentOrder),
            orderId.ToString(),
            oldValues: new { AttemptStatus = oldStatus.ToString() },
            newValues: new { AttemptStatus = lockedAttempt.Status.ToString(), OrderStatus = lockedOrder.Status.ToString() },
            userId: lockedAttempt.OwnerId.ToString());

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new DirectPaymentLifecycleResponse(
            orderId,
            lockedOrder.Status.ToString(),
            lockedAttempt.Status.ToString(),
            now);
    }

    private static BusinessRuleValidationException AlreadyCompleted() =>
        new("PAYMENT_ALREADY_COMPLETED", "The direct payment has already been completed.", 409);

    private static BusinessRuleValidationException PendingCancellation() =>
        new("PAYMENT_CANCELLATION_PENDING", "The checkout session outcome is still pending and cannot be cancelled yet.", 409);
}
