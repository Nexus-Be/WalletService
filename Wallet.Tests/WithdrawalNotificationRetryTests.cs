namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

/// <summary>T29: a withdrawal notification is queued together with the money change and is sent again until Notification confirms it.</summary>
public sealed class WithdrawalNotificationRetryTests
{
    private sealed class ScriptedClient : INotificationClient
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public List<string> SourceEventIds { get; } = new();

        public Task<NotificationDispatchResult> SendWithdrawalEventAsync(WithdrawalNotificationEvent payload, CancellationToken cancellationToken = default)
        {
            Calls++;
            SourceEventIds.Add(payload.SourceEventId);
            return Task.FromResult(Fail ? new NotificationDispatchResult(false, 503, Error: "down") : new NotificationDispatchResult(true, 200, "n1"));
        }

        public Task<NotificationDispatchResult> SendAuctionDepositEventAsync(AuctionDepositNotificationEvent payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationDispatchResult(true, 200));

        public Task<NotificationDispatchResult> SendWalletTopupEventAsync(WalletTopupNotificationEvent payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationDispatchResult(true, 200));
    }

    private static async Task<(TestWalletDbContext Db, Guid WithdrawalId)> ApprovedWithdrawalAsync(ScriptedClient client)
    {
        var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Balance = 500_000m, LockedBalance = 100_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var withdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100_000m, Fee = 0m, NetAmount = 100_000m,
            BankName = "VCB", BankAccountNumber = "1234567890", BankAccountHolder = "Seller", Status = WithdrawalStatus.Pending
        };
        db.Wallets.Add(wallet);
        db.WithdrawalRequests.Add(withdrawal);
        await db.SaveChangesAsync();
        await new ProcessWithdrawalCommandHandler(db, client).Handle(
            new ProcessWithdrawalCommand("Approve") { WithdrawalId = withdrawal.Id, AdminUserId = Guid.NewGuid().ToString() }, default);
        return (db, withdrawal.Id);
    }

    [Fact]
    public async Task TheNotificationIsQueuedWithTheMoneyChange_AndMarkedDeliveredWhenTheFirstSendWorks()
    {
        var client = new ScriptedClient();
        var (db, _) = await ApprovedWithdrawalAsync(client);
        using var _ = db;

        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditAction.NotificationQueued));
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditAction.NotificationDelivered));
        Assert.Equal(1, client.Calls);

        var handled = await WithdrawalNotificationRetry.RetryPendingAsync(db, client, DateTimeOffset.UtcNow.AddHours(1), 20, default);

        Assert.Equal(0, handled);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task WhenNotificationIsDown_TheRetrySendsLaterUntilItWorks_AndThenStops()
    {
        var client = new ScriptedClient { Fail = true };
        var (db, _) = await ApprovedWithdrawalAsync(client);
        using var _ = db;
        Assert.Equal(1, client.Calls);
        var now = DateTimeOffset.UtcNow;

        var tooEarly = await WithdrawalNotificationRetry.RetryPendingAsync(db, client, now, 20, default);
        Assert.Equal(0, tooEarly);

        client.Fail = false;
        var later = await WithdrawalNotificationRetry.RetryPendingAsync(db, client, now.AddMinutes(5), 20, default);
        var again = await WithdrawalNotificationRetry.RetryPendingAsync(db, client, now.AddMinutes(30), 20, default);

        Assert.Equal(1, later);
        Assert.Equal(0, again);
        Assert.Equal(2, client.Calls);
        Assert.Equal(client.SourceEventIds[0], client.SourceEventIds[1]);
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditAction.NotificationDelivered));
    }

    [Fact]
    public async Task AfterTheMaximumNumberOfFailures_TheEventIsAbandonedAndNeverRetriedAgain()
    {
        var client = new ScriptedClient { Fail = true };
        var (db, _) = await ApprovedWithdrawalAsync(client);
        using var _ = db;
        var clock = DateTimeOffset.UtcNow;

        for (var i = 0; i < 40; i++)
        {
            clock = clock.AddHours(2);
            await WithdrawalNotificationRetry.RetryPendingAsync(db, client, clock, 20, default);
        }

        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditAction.NotificationAbandoned));
        Assert.Equal(WithdrawalNotificationRetry.MaxAttempts, client.Calls);
        Assert.Equal(WithdrawalNotificationRetry.MaxAttempts, db.AuditLogs.Count(a => a.Action == AuditAction.NotificationDispatchFailed));
    }

    [Fact]
    public void TheWaitBeforeEachRetryDoublesAndStopsAtOneHour()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), WithdrawalNotificationRetry.Backoff(1));
        Assert.Equal(TimeSpan.FromMinutes(2), WithdrawalNotificationRetry.Backoff(2));
        Assert.Equal(TimeSpan.FromMinutes(4), WithdrawalNotificationRetry.Backoff(3));
        Assert.Equal(TimeSpan.FromMinutes(60), WithdrawalNotificationRetry.Backoff(12));
    }

    [Fact]
    public async Task NewerPendingNotificationIsStillRetriedAfterMoreThanEightyTerminalNotifications()
    {
        using var db = new TestWalletDbContext(nameof(NewerPendingNotificationIsStillRetriedAfterMoreThanEightyTerminalNotifications));
        var client = new ScriptedClient();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-3);

        for (var i = 0; i < 85; i++)
        {
            var withdrawalId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var sourceEventId = $"wd-terminal-{i}";
            var payload = new WithdrawalNotificationEvent(
                sourceEventId, withdrawalId, userId, "Approved", baseTime.AddSeconds(i), null, null, null);
            db.AuditLogs.Add(new AuditLog
            {
                ServiceName = "wallet-service",
                Action = AuditAction.NotificationQueued,
                EntityName = nameof(WithdrawalRequest),
                EntityId = withdrawalId.ToString(),
                UserId = userId.ToString(),
                NewValues = System.Text.Json.JsonSerializer.Serialize(new { SourceEventId = sourceEventId, Payload = payload }),
                CreatedAt = baseTime.AddSeconds(i)
            });
            db.AuditLogs.Add(new AuditLog
            {
                ServiceName = "wallet-service",
                Action = i % 2 == 0 ? AuditAction.NotificationDelivered : AuditAction.NotificationAbandoned,
                EntityName = nameof(WithdrawalRequest),
                EntityId = withdrawalId.ToString(),
                UserId = userId.ToString(),
                NewValues = System.Text.Json.JsonSerializer.Serialize(new { SourceEventId = sourceEventId }),
                CreatedAt = baseTime.AddSeconds(i + 1)
            });
        }

        var pendingWithdrawalId = Guid.NewGuid();
        var pendingUserId = Guid.NewGuid();
        var pendingEventId = "wd-pending-86";
        var pendingPayload = new WithdrawalNotificationEvent(
            pendingEventId, pendingWithdrawalId, pendingUserId, "Approved", baseTime.AddMinutes(5), null, null, null);
        db.AuditLogs.Add(new AuditLog
        {
            ServiceName = "wallet-service",
            Action = AuditAction.NotificationQueued,
            EntityName = nameof(WithdrawalRequest),
            EntityId = pendingWithdrawalId.ToString(),
            UserId = pendingUserId.ToString(),
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { SourceEventId = pendingEventId, Payload = pendingPayload }),
            CreatedAt = baseTime.AddMinutes(5)
        });
        await db.SaveChangesAsync();

        var handled = await WithdrawalNotificationRetry.RetryPendingAsync(db, client, DateTimeOffset.UtcNow, 20, default);

        Assert.Equal(1, handled);
        Assert.Single(client.SourceEventIds, pendingEventId);
        Assert.Contains(db.AuditLogs, a => a.Action == AuditAction.NotificationDelivered && a.EntityId == pendingWithdrawalId.ToString());
    }
}
