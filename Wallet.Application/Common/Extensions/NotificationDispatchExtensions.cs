namespace Wallet.Application.Common.Extensions;

using System;
using System.Threading;
using System.Threading.Tasks;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

internal static class NotificationDispatchExtensions
{
    internal const string WithdrawalEndpoint = "api/v1/notifications/withdrawal-events";

    /// <summary>
    /// Records, in the same SaveChanges as the money operation, that this withdrawal notification must be delivered. If the process
    /// stops or Notification is down after the commit, <see cref="WithdrawalNotificationRetry"/> still finds this row and sends it.
    /// </summary>
    internal static void QueueWithdrawalEvent(IWalletDbContext context, WithdrawalNotificationEvent payload)
    {
        context.AddAuditLog(
            AuditAction.NotificationQueued,
            nameof(WithdrawalRequest),
            payload.WithdrawalId.ToString(),
            newValues: new { payload.SourceEventId, Endpoint = WithdrawalEndpoint, Payload = payload },
            userId: payload.RecipientUserId.ToString(),
            traceId: payload.TraceId);
    }

    internal static async Task MarkDeliveredAsync(
        IWalletDbContext context,
        AuditAction action,
        Guid withdrawalId,
        Guid recipientUserId,
        string sourceEventId,
        CancellationToken cancellationToken)
    {
        try
        {
            context.AddAuditLog(
                action,
                nameof(WithdrawalRequest),
                withdrawalId.ToString(),
                newValues: new { SourceEventId = sourceEventId },
                userId: recipientUserId.ToString());
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The notification was already sent (or given up); a failure to note it only means one more harmless retry.
        }
    }

    internal static Task RecordWithdrawalFailureAsync(
        IWalletDbContext context,
        WithdrawalNotificationEvent payload,
        NotificationDispatchResult result,
        CancellationToken cancellationToken) =>
        RecordFailureAsync(
            context, nameof(WithdrawalRequest), payload.WithdrawalId, payload.RecipientUserId,
            WithdrawalEndpoint, payload, result, payload.TraceId, cancellationToken);

    internal static async Task TryDispatchWithdrawalEventAsync(
        IWalletDbContext context,
        INotificationClient? client,
        WithdrawalNotificationEvent payload,
        CancellationToken cancellationToken)
    {
        if (client is null)
        {
            return;
        }

        NotificationDispatchResult result;
        try
        {
            result = await client.SendWithdrawalEventAsync(payload, cancellationToken);
        }
        catch (Exception ex)
        {
            result = new NotificationDispatchResult(false, Error: ex.Message);
        }

        if (result.Succeeded)
        {
            await MarkDeliveredAsync(
                context, AuditAction.NotificationDelivered, payload.WithdrawalId, payload.RecipientUserId, payload.SourceEventId, cancellationToken);
        }
        else
        {
            await RecordFailureAsync(
                context,
                nameof(WithdrawalRequest),
                payload.WithdrawalId,
                payload.RecipientUserId,
                WithdrawalEndpoint,
                payload,
                result,
                payload.TraceId,
                cancellationToken);
        }
    }

    internal static async Task TryDispatchAuctionDepositEventAsync(
        IWalletDbContext context,
        INotificationClient? client,
        AuctionDepositNotificationEvent payload,
        Guid holdId,
        CancellationToken cancellationToken)
    {
        if (client is null)
        {
            return;
        }

        NotificationDispatchResult result;
        try
        {
            result = await client.SendAuctionDepositEventAsync(payload, cancellationToken);
        }
        catch (Exception ex)
        {
            result = new NotificationDispatchResult(false, Error: ex.Message);
        }

        if (!result.Succeeded)
        {
            await RecordFailureAsync(
                context,
                nameof(AuctionHold),
                holdId,
                payload.BidderId,
                "api/v1/notifications/auction-deposits",
                payload,
                result,
                payload.TraceId,
                cancellationToken);
        }
    }

    private static async Task RecordFailureAsync(
        IWalletDbContext context,
        string entityName,
        Guid entityId,
        Guid recipientUserId,
        string endpoint,
        object payload,
        NotificationDispatchResult result,
        string? traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            context.AddAuditLog(
                AuditAction.NotificationDispatchFailed,
                entityName,
                entityId.ToString(),
                newValues: new
                {
                    Endpoint = endpoint,
                    Payload = payload,
                    result.StatusCode,
                    result.Error
                },
                userId: recipientUserId.ToString(),
                traceId: traceId);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Notification audit failures must never fail or roll back a committed financial operation.
        }
    }
}
