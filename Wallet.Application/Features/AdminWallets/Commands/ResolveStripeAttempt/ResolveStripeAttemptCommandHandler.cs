namespace Wallet.Application.Features.AdminWallets.Commands.ResolveStripeAttempt;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ResolveStripeAttemptCommandHandler(IWalletDbContext context)
    : IRequestHandler<ResolveStripeAttemptCommand, ResolveStripeAttemptResponse>
{
    public async Task<ResolveStripeAttemptResponse> Handle(
        ResolveStripeAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.AdminUserId, out var adminGuid) || adminGuid == Guid.Empty)
        {
            throw new ArgumentException("Admin user ID is invalid.", nameof(request));
        }

        if (!request.Resolution.TryToEnum<StripePaymentAttemptStatus>(out var targetStatus) ||
            targetStatus is not (StripePaymentAttemptStatus.Failed or StripePaymentAttemptStatus.Expired))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_RESOLUTION",
                "Resolution must be either 'Failed' or 'Expired'.");
        }

        var reason = request.Reason.Trim();
        var identity = await context.StripePaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.AttemptId, cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_ATTEMPT_NOT_FOUND", nameof(StripePaymentAttempt), request.AttemptId);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        WalletTopup? topup = null;
        DirectPaymentOrder? directOrder = null;
        if (identity.Purpose == StripePaymentPurpose.Topup && identity.WalletTopupId is Guid topupId)
        {
            topup = await context.LockTopupByIdAsync(topupId, cancellationToken);
        }
        else if (identity.Purpose == StripePaymentPurpose.DirectOrder && identity.DirectPaymentOrderId is Guid directOrderId)
        {
            directOrder = await context.LockDirectPaymentOrderByIdAsync(directOrderId, cancellationToken);
        }

        var attempt = await context.LockStripePaymentAttemptAsync(request.AttemptId, cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_ATTEMPT_NOT_FOUND", nameof(StripePaymentAttempt), request.AttemptId);

        if (attempt.Status != StripePaymentAttemptStatus.ReconciliationRequired)
        {
            throw new BusinessRuleValidationException(
                "INVALID_ATTEMPT_STATE",
                "Only payment attempts requiring reconciliation can be manually resolved.",
                409);
        }

        var now = DateTimeOffset.UtcNow;
        var oldStatus = attempt.Status;
        var oldBlocksReplacement = attempt.BlocksReplacement;
        var parentStatus = targetStatus == StripePaymentAttemptStatus.Expired
            ? TopupStatus.Expired
            : TopupStatus.Failed;
        attempt.Status = targetStatus;
        attempt.BlocksReplacement = false;
        attempt.UpdatedAt = now;

        if (topup is not null && topup.Status == TopupStatus.Pending)
        {
            topup.Status = parentStatus;
            topup.UpdatedAt = now;
        }

        if (directOrder is not null && directOrder.Status == TopupStatus.Pending)
        {
            directOrder.Status = parentStatus;
            directOrder.UpdatedAt = now;

            var pendingDirectTxs = await context.WalletTransactions
                .Where(item => item.ReferenceId == directOrder.OrderId &&
                               item.Type == WalletTransactionType.DirectGatewayPayment &&
                               item.Status == TransactionStatus.Pending)
                .ToListAsync(cancellationToken);

            foreach (var tx in pendingDirectTxs)
            {
                tx.Status = TransactionStatus.Failed;
                tx.UpdatedAt = now;
            }
        }

        context.AddAuditLog(
            AuditAction.StripeAttemptResolve,
            nameof(StripePaymentAttempt),
            attempt.Id.ToString(),
            oldValues: new { Status = oldStatus.ToString(), BlocksReplacement = oldBlocksReplacement },
            newValues: new { Status = attempt.Status.ToString(), attempt.BlocksReplacement, Reason = reason },
            userId: adminGuid.ToString());

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ResolveStripeAttemptResponse(
            attempt.Id,
            attempt.Status.ToString(),
            attempt.BlocksReplacement,
            reason,
            now);
    }
}
