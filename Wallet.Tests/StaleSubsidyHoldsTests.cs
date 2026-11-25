namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Wallet.Application.Features.Subsidies.Queries.ListStaleSubsidyHolds;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

/// <summary>The list Commerce uses to find subsidy holds whose sub-order was never saved: only holds that are still Held and old enough.</summary>
public sealed class StaleSubsidyHoldsTests
{
    private static PlatformSubsidyReservation Reservation(int minutesAgo, SubsidyReservationStatus status = SubsidyReservationStatus.Held) => new()
    {
        Id = Guid.NewGuid(), BudgetId = Guid.NewGuid(), SubOrderId = Guid.NewGuid(), OrderId = Guid.NewGuid(), Amount = 10000m,
        Status = status, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)
    };

    [Fact]
    public async Task Only_old_holds_that_are_still_held_are_listed_oldest_first()
    {
        using var db = new TestWalletDbContext(nameof(Only_old_holds_that_are_still_held_are_listed_oldest_first));
        var fresh = Reservation(2);
        var old = Reservation(90);
        var older = Reservation(300);
        var used = Reservation(400, SubsidyReservationStatus.Used);
        var released = Reservation(400, SubsidyReservationStatus.Released);
        db.PlatformSubsidyReservations.AddRange(fresh, old, older, used, released);
        await db.SaveChangesAsync();

        var result = await new ListStaleSubsidyHoldsQueryHandler(db).Handle(new ListStaleSubsidyHoldsQuery(30, 50), default);

        Assert.Equal(new[] { older.SubOrderId, old.SubOrderId }, result.Select(item => item.SubOrderId).ToArray());
        Assert.DoesNotContain(result, item => item.SubOrderId == fresh.SubOrderId);
    }

    [Fact]
    public async Task The_list_is_capped_by_the_limit()
    {
        using var db = new TestWalletDbContext(nameof(The_list_is_capped_by_the_limit));
        for (var i = 0; i < 5; i++) db.PlatformSubsidyReservations.Add(Reservation(100 + i));
        await db.SaveChangesAsync();

        var result = await new ListStaleSubsidyHoldsQueryHandler(db).Handle(new ListStaleSubsidyHoldsQuery(30, 3), default);

        Assert.Equal(3, result.Count);
    }
}
