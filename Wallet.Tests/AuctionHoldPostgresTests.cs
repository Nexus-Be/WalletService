namespace Wallet.Tests;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Application.Features.Holds.Queries.GetHold;
using Wallet.Application.Features.Holds.Queries.ListMyHolds;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class AuctionHoldPostgresTests
{
    [DisposablePostgresFact]
    public async Task ConcurrentDuplicateCreateAndRelease_ExecuteOnce()
    {
        var options = await Setup();
        var owner = Guid.NewGuid();
        await AddWallet(options, owner, 100, 10);
        var reference = Guid.NewGuid().ToString();
        var request = new CreateHoldCommand(owner, "AUCTION_DEPOSIT", reference, 50, "VND",
            JsonSerializer.SerializeToElement(new { auctionId = "auction", version = 3 }))
            { IdempotencyKey = $"hold:{reference}" };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        var created = await Task.WhenAll(new CreateHoldCommandHandler(a).Handle(request, default),
            new CreateHoldCommandHandler(b).Handle(request, default));
        Assert.Single(created, item => item.Created);
        Assert.Equal(created[0].Hold, created[1].Hold);

        await using (var differentKey = new WalletDbContext(options))
        {
            var replay = await new CreateHoldCommandHandler(differentKey).Handle(request with
                { IdempotencyKey = $"hold:alternate:{reference}" }, default);
            Assert.False(replay.Created);
            Assert.Equal(created[0].Hold, replay.Hold);
        }

        var release = new ReleaseHoldCommand("AUCTION_CANCELLED")
            { HoldId = created[0].Hold.HoldId, IdempotencyKey = $"release:{reference}" };
        await using var c = new WalletDbContext(options);
        await using var d = new WalletDbContext(options);
        var released = await Task.WhenAll(new ReleaseHoldCommandHandler(c).Handle(release, default),
            new ReleaseHoldCommandHandler(d).Handle(release, default));
        Assert.Equal(released[0], released[1]);
        await using var verify = new WalletDbContext(options);
        var wallet = await verify.Wallets.SingleAsync(item => item.UserId == owner);
        Assert.Equal(10, wallet.LockedBalance);
        Assert.Equal(1, await verify.AuctionHolds.CountAsync(item => item.OwnerUserId == owner));
        Assert.Equal(2, await verify.AuditLogs.CountAsync(item =>
            item.EntityId == created[0].Hold.HoldId.ToString()));
    }

    [DisposablePostgresFact]
    public async Task ConcurrentHolds_CannotSpendExistingReservations()
    {
        var options = await Setup();
        var owner = Guid.NewGuid();
        await AddWallet(options, owner, 100, 20);
        var reference = Guid.NewGuid().ToString();
        var first = new CreateHoldCommand(owner, "AUCTION_DEPOSIT", reference + "-1", 60, "VND", null)
            { IdempotencyKey = $"hold:{reference}:1" };
        var second = first with { ReferenceId = reference + "-2", IdempotencyKey = $"hold:{reference}:2" };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        var results = await Task.WhenAll(TryCreate(a, first), TryCreate(b, second));
        Assert.Single(results, value => value);
        await using var verify = new WalletDbContext(options);
        Assert.Equal(80, (await verify.Wallets.SingleAsync(item => item.UserId == owner)).LockedBalance);
        Assert.Equal(1, await verify.AuctionHolds.CountAsync(item => item.OwnerUserId == owner));
    }

    [DisposablePostgresFact]
    public async Task LookupAndList_RespectReferenceOwnershipAndPagination()
    {
        var options = await Setup();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await AddWallet(options, owner, 100, 0);
        await AddWallet(options, other, 100, 0);
        var reference = Guid.NewGuid().ToString();
        await using var context = new WalletDbContext(options);
        var created = await new CreateHoldCommandHandler(context).Handle(
            new CreateHoldCommand(owner, "AUCTION_DEPOSIT", reference, 10, "VND",
                JsonSerializer.SerializeToElement(new { auctionId = "auction-123" }))
                { IdempotencyKey = $"hold:{reference}" }, default);
        var found = await new GetHoldQueryHandler(context).Handle(new GetHoldQuery(null, "AUCTION_DEPOSIT", reference), default);
        Assert.Equal(created.Hold.HoldId, found.HoldId);
        var own = await new ListMyHoldsQueryHandler(context).Handle(new ListMyHoldsQuery(1, 1, "Held") { UserId = owner }, default);
        var foreign = await new ListMyHoldsQueryHandler(context).Handle(new ListMyHoldsQuery { UserId = other }, default);
        Assert.Equal("auction-123", Assert.Single(own.Items).AuctionId);
        Assert.Empty(foreign.Items);
        Assert.Equal(1, own.Pagination.TotalCount);
    }

    private static async Task<bool> TryCreate(WalletDbContext context, CreateHoldCommand request)
    {
        try { await new CreateHoldCommandHandler(context).Handle(request, default); return true; }
        catch (InsufficientBalanceException) { return false; }
    }

    private static async Task<DbContextOptions<WalletDbContext>> Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        await using var setup = new WalletDbContext(options);
        await setup.Database.MigrateAsync();
        return options;
    }

    private static async Task AddWallet(DbContextOptions<WalletDbContext> options, Guid user, decimal balance, decimal locked)
    {
        await using var context = new WalletDbContext(options);
        context.Wallets.Add(new WalletAccount
            { Id = Guid.NewGuid(), UserId = user, Currency = CurrencyCode.Vnd, Balance = balance, LockedBalance = locked });
        await context.SaveChangesAsync();
    }

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }
}
