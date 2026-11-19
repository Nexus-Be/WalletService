namespace Wallet.Application.Common.Extensions;

using System;
using System.Threading;
using System.Threading.Tasks;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

internal static class NotificationDispatchExtensions
{
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

        if (!result.Succeeded)
        {
            await RecordFailureAsync(
                context,
                nameof(WithdrawalRequest),
                payload.WithdrawalId,
                payload.RecipientUserId,
                "api/v1/notifications/withdrawal-events",
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
