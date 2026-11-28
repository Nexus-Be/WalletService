namespace Wallet.Application.Common.Extensions;

using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

/// <summary>
/// Sends again the withdrawal and Stripe topup notifications that were queued with the money operation but never confirmed. Each pass looks at the
/// queued rows that have neither a "delivered" nor an "abandoned" row. The wait before each new attempt doubles (1, 2, 4 ... minutes, at
/// most one hour) and after <see cref="MaxAttempts"/> failed attempts the event is marked abandoned and left for a person; nothing is
/// retried forever. The Notification service answers a repeated <c>sourceEventId</c> with the first result, so a retry never sends twice.
/// </summary>
public static class WithdrawalNotificationRetry
{
    public const int MaxAttempts = 8;
    public static readonly TimeSpan FirstAttemptDelay = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TimeSpan Backoff(int failedAttempts) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, failedAttempts - 1))));

    public static async Task<int> RetryPendingAsync(
        IWalletDbContext context,
        INotificationClient client,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        if (batchSize <= 0)
        {
            return 0;
        }

        var cutoff = now - FirstAttemptDelay;
        var scanBatchSize = Math.Max(batchSize * 4, 1);
        var offset = 0;
        var handled = 0;

        while (handled < batchSize)
        {
            var queued = await context.AuditLogs
                .AsNoTracking()
                .Where(a => a.Action == AuditAction.NotificationQueued && a.CreatedAt <= cutoff)
                .OrderBy(a => a.CreatedAt)
                .ThenBy(a => a.Id)
                .Skip(offset)
                .Take(scanBatchSize)
                .ToListAsync(cancellationToken);
            if (queued.Count == 0)
            {
                break;
            }

            offset += queued.Count;

            var entityIds = queued.Select(q => q.EntityId).Distinct().ToList();
            var related = await context.AuditLogs
                .AsNoTracking()
                .Where(a => entityIds.Contains(a.EntityId)
                    && (a.Action == AuditAction.NotificationDelivered
                        || a.Action == AuditAction.NotificationAbandoned
                        || a.Action == AuditAction.NotificationDispatchFailed))
                .ToListAsync(cancellationToken);

            foreach (var row in queued)
            {
                if (handled >= batchSize)
                {
                    break;
                }

                if (string.Equals(row.EntityName, nameof(WalletTopup), StringComparison.Ordinal))
                {
                    if (await TryRetryWalletTopupRowAsync(context, client, row, related, now, cancellationToken, logger))
                    {
                        handled++;
                    }
                    continue;
                }

                if (string.Equals(row.EntityName, nameof(WithdrawalRequest), StringComparison.Ordinal))
                {
                    if (await TryRetryWithdrawalRowAsync(context, client, row, related, now, cancellationToken))
                    {
                        handled++;
                    }
                }
            }

            if (queued.Count < scanBatchSize)
            {
                break;
            }
        }

        return handled;
    }

    private static async Task<bool> TryRetryWithdrawalRowAsync(
        IWalletDbContext context,
        INotificationClient client,
        AuditLog row,
        System.Collections.Generic.List<AuditLog> related,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        WithdrawalNotificationEvent? payload;
        try
        {
            using var doc = JsonDocument.Parse(row.NewValues ?? "{}");
            payload = doc.RootElement.TryGetProperty("Payload", out var p)
                ? p.Deserialize<WithdrawalNotificationEvent>(Json)
                : null;
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.SourceEventId))
        {
            return false;
        }

        var mine = related.Where(r => r.EntityId == row.EntityId && (r.NewValues ?? string.Empty).Contains(payload.SourceEventId)).ToList();
        if (mine.Any(r => r.Action is AuditAction.NotificationDelivered or AuditAction.NotificationAbandoned))
        {
            return false;
        }

        var failures = mine.Where(r => r.Action == AuditAction.NotificationDispatchFailed).ToList();
        if (failures.Count >= MaxAttempts)
        {
            await NotificationDispatchExtensions.MarkDeliveredAsync(
                context, AuditAction.NotificationAbandoned, payload.WithdrawalId, payload.RecipientUserId, payload.SourceEventId, cancellationToken);
            return true;
        }

        var lastAttemptAt = failures.Count == 0 ? row.CreatedAt : failures.Max(f => f.CreatedAt);
        var wait = failures.Count == 0 ? FirstAttemptDelay : Backoff(failures.Count);
        if (now < lastAttemptAt + wait)
        {
            return false;
        }

        NotificationDispatchResult result;
        try
        {
            result = await client.SendWithdrawalEventAsync(payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new NotificationDispatchResult(false, Error: ex.Message);
        }

        if (result.Succeeded)
        {
            await NotificationDispatchExtensions.MarkDeliveredAsync(
                context, AuditAction.NotificationDelivered, payload.WithdrawalId, payload.RecipientUserId, payload.SourceEventId, cancellationToken);
        }
        else
        {
            await NotificationDispatchExtensions.RecordWithdrawalFailureAsync(context, payload, result, cancellationToken);
        }

        return true;
    }

    private static async Task<bool> TryRetryWalletTopupRowAsync(
        IWalletDbContext context,
        INotificationClient client,
        AuditLog row,
        System.Collections.Generic.List<AuditLog> related,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        ILogger? logger)
    {
        WalletTopupNotificationEvent? payload;
        try
        {
            using var doc = JsonDocument.Parse(row.NewValues ?? "{}");
            payload = doc.RootElement.TryGetProperty("Payload", out var p)
                ? p.Deserialize<WalletTopupNotificationEvent>(Json)
                : null;
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.SourceEventId))
        {
            return false;
        }

        var mine = related.Where(r => r.EntityId == row.EntityId && (r.NewValues ?? string.Empty).Contains(payload.SourceEventId)).ToList();
        if (mine.Any(r => r.Action is AuditAction.NotificationDelivered or AuditAction.NotificationAbandoned))
        {
            return false;
        }

        var failures = mine.Where(r => r.Action == AuditAction.NotificationDispatchFailed).ToList();
        if (failures.Count >= MaxAttempts)
        {
            await NotificationDispatchExtensions.MarkEntityDeliveredAsync(
                context,
                AuditAction.NotificationAbandoned,
                nameof(WalletTopup),
                payload.TopupId,
                payload.RecipientUserId,
                payload.SourceEventId,
                reconciliationRequired: false,
                statusCode: null,
                cancellationToken);
            return true;
        }

        var lastAttemptAt = failures.Count == 0 ? row.CreatedAt : failures.Max(f => f.CreatedAt);
        var wait = failures.Count == 0 ? FirstAttemptDelay : Backoff(failures.Count);
        if (now < lastAttemptAt + wait)
        {
            return false;
        }

        // Preserve business payload & sourceEventId; traceId may be refreshed on retry.
        var retryPayload = payload with
        {
            TraceId = NotificationDispatchExtensions.NormalizeTraceId(Activity.Current?.Id) ?? payload.TraceId
        };

        await NotificationDispatchExtensions.TryDispatchWalletTopupEventAsync(
            context, client, retryPayload, cancellationToken, logger);

        return true;
    }
}
