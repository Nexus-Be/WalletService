namespace Wallet.Application.Common.Extensions;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

internal static class NotificationDispatchExtensions
{
    internal const string WithdrawalEndpoint = "api/v1/notifications/withdrawal-events";
    internal const string WalletTopupEndpoint = "api/v1/notifications/wallet-topups";

    internal static string? NormalizeTraceId(string? traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return null;
        }

        var trimmed = traceId.Trim();
        return trimmed.Length <= 100 ? trimmed : trimmed[..100];
    }

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

    /// <summary>
    /// Records, in the same SaveChanges as the Stripe topup credit, that this topup notification must be delivered.
    /// </summary>
    internal static void QueueWalletTopupEvent(IWalletDbContext context, WalletTopupNotificationEvent payload)
    {
        context.AddAuditLog(
            AuditAction.NotificationQueued,
            nameof(WalletTopup),
            payload.TopupId.ToString(),
            newValues: new { payload.SourceEventId, Endpoint = WalletTopupEndpoint, Payload = payload },
            userId: payload.RecipientUserId.ToString(),
            traceId: payload.TraceId);
    }

    internal static Task MarkDeliveredAsync(
        IWalletDbContext context,
        AuditAction action,
        Guid withdrawalId,
        Guid recipientUserId,
        string sourceEventId,
        CancellationToken cancellationToken) =>
        MarkEntityDeliveredAsync(
            context,
            action,
            nameof(WithdrawalRequest),
            withdrawalId,
            recipientUserId,
            sourceEventId,
            reconciliationRequired: false,
            statusCode: null,
            cancellationToken);

    internal static async Task MarkEntityDeliveredAsync(
        IWalletDbContext context,
        AuditAction action,
        string entityName,
        Guid entityId,
        Guid recipientUserId,
        string sourceEventId,
        bool reconciliationRequired,
        int? statusCode,
        CancellationToken cancellationToken)
    {
        try
        {
            object newValues = reconciliationRequired
                ? new { SourceEventId = sourceEventId, ReconciliationRequired = true, StatusCode = statusCode }
                : new { SourceEventId = sourceEventId };
            context.AddAuditLog(
                action,
                entityName,
                entityId.ToString(),
                newValues: newValues,
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

    internal static Task RecordWalletTopupFailureAsync(
        IWalletDbContext context,
        WalletTopupNotificationEvent payload,
        NotificationDispatchResult result,
        CancellationToken cancellationToken) =>
        RecordFailureAsync(
            context, nameof(WalletTopup), payload.TopupId, payload.RecipientUserId,
            WalletTopupEndpoint, payload, result, payload.TraceId, cancellationToken);

    internal static async Task TryDispatchWalletTopupEventAsync(
        IWalletDbContext context,
        INotificationClient? client,
        WalletTopupNotificationEvent payload,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        if (client is null)
        {
            return;
        }

        NotificationDispatchResult result;
        try
        {
            result = await client.SendWalletTopupEventAsync(payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new NotificationDispatchResult(false, Error: ex.Message);
        }

        if (result.Succeeded)
        {
            await MarkEntityDeliveredAsync(
                context,
                AuditAction.NotificationDelivered,
                nameof(WalletTopup),
                payload.TopupId,
                payload.RecipientUserId,
                payload.SourceEventId,
                reconciliationRequired: false,
                statusCode: result.StatusCode,
                cancellationToken);
        }
        else if (result.StatusCode == 409)
        {
            logger?.LogError(
                "[REST Notification] Wallet topup notification {SourceEventId} for topup {TopupId} returned HTTP 409; marked for reconciliation without modifying sourceEventId.",
                payload.SourceEventId,
                payload.TopupId);
            await RecordWalletTopupFailureAsync(context, payload, result, cancellationToken);
            await MarkEntityDeliveredAsync(
                context,
                AuditAction.NotificationAbandoned,
                nameof(WalletTopup),
                payload.TopupId,
                payload.RecipientUserId,
                payload.SourceEventId,
                reconciliationRequired: true,
                statusCode: result.StatusCode,
                cancellationToken);
        }
        else
        {
            await RecordWalletTopupFailureAsync(context, payload, result, cancellationToken);
        }
    }

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
                    result.Error,
                    ReconciliationRequired = result.StatusCode == 409
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
