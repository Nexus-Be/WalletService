namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Wallet.Application.Features.Audit.Commands.MarkAuditLogsSynced;
using Wallet.Application.Features.Audit.Queries.GetUnsyncedAuditLogs;
using Wallet.Domain.Entities;
using Wallet.Tests.Support;
using Xunit;

public sealed class AuditSyncSourceTests
{
    private static AuditLog Row(DateTimeOffset at, bool synced = false) => new()
    {
        Id = Guid.NewGuid(), ServiceName = "wallet-service", Action = default, EntityName = "WalletAccount", EntityId = "w1", CreatedAt = at, IsSynced = synced
    };

    [Fact]
    public async Task Unsynced_records_come_oldest_first_with_the_total()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var now = DateTimeOffset.UtcNow;
        var a = Row(now.AddMinutes(-3));
        var b = Row(now.AddMinutes(-2));
        db.AuditLogs.AddRange(Row(now.AddMinutes(-1)), b, a, Row(now.AddMinutes(-9), synced: true));
        await db.SaveChangesAsync();

        var page = await new GetUnsyncedAuditLogsQueryHandler(db).Handle(new GetUnsyncedAuditLogsQuery(2), default);

        Assert.Equal(new[] { a.Id, b.Id }, page.Items.Select(i => i.Id).ToArray());
        Assert.Equal(3, page.UnsyncedCount);
    }

    [Fact]
    public async Task Marking_is_idempotent_and_ignores_unknown_ids()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var a = Row(DateTimeOffset.UtcNow.AddMinutes(-2));
        db.AuditLogs.Add(a);
        await db.SaveChangesAsync();
        var handler = new MarkAuditLogsSyncedCommandHandler(db);

        Assert.Equal(1, (await handler.Handle(new MarkAuditLogsSyncedCommand(new[] { a.Id, a.Id, Guid.NewGuid() }), default)).Updated);
        Assert.Equal(0, (await handler.Handle(new MarkAuditLogsSyncedCommand(new[] { a.Id }), default)).Updated);
        Assert.Equal(0, (await handler.Handle(new MarkAuditLogsSyncedCommand(null), default)).Updated);
    }
}
