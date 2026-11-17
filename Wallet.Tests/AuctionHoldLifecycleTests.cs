namespace Wallet.Tests;

using System.Text.Json;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class AuctionHoldLifecycleTests
{
    [Fact]
    public async Task CreateAndRelease_AreIdempotentAndRespectExistingReservations()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(), UserId = owner, Balance = 100, LockedBalance = 20, Currency = CurrencyCode.Vnd
        };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        var command = new CreateHoldCommand(owner, "AUCTION_DEPOSIT", "deposit-1", 50, "VND",
            JsonSerializer.SerializeToElement(new { auctionId = "auction-1" })) { IdempotencyKey = "hold:deposit-1" };
        var create = new CreateHoldCommandHandler(context);

        var first = await create.Handle(command, default);
        var retry = await create.Handle(command, default);
        Assert.True(first.Created);
        Assert.False(retry.Created);
        Assert.Equal(first.Hold, retry.Hold);
        Assert.Equal(70m, wallet.LockedBalance);
        Assert.Equal(30m, wallet.AvailableBalance);
        Assert.Single(context.AuctionHolds);

        var release = new ReleaseHoldCommandHandler(context);
        var releaseCommand = new ReleaseHoldCommand("AUCTION_ENDED_NOT_WINNER")
            { HoldId = first.Hold.HoldId, IdempotencyKey = "release:deposit-1" };
        var released = await release.Handle(releaseCommand, default);
        var repeated = await release.Handle(releaseCommand, default);
        Assert.Equal(released, repeated);
        Assert.Equal("Released", released.Status);
        Assert.Equal(20m, wallet.LockedBalance);
        Assert.Equal(80m, wallet.AvailableBalance);
        Assert.Equal(2, context.AuditLogs.Count());
    }

    [Fact]
    public async Task Create_RejectsReusedKeyAndInsufficientAvailableBalance()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(), UserId = owner, Balance = 100, LockedBalance = 40, Currency = CurrencyCode.Vnd
        };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        var handler = new CreateHoldCommandHandler(context);
        var command = new CreateHoldCommand(owner, "AUCTION_DEPOSIT", "deposit-1", 50, "VND", null)
            { IdempotencyKey = "hold:deposit-1" };
        await handler.Handle(command, default);
        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => handler.Handle(command with { Amount = 49 }, default));
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.ErrorCode);
        await Assert.ThrowsAsync<InsufficientBalanceException>(() => handler.Handle(
            command with { ReferenceId = "deposit-2", IdempotencyKey = "hold:deposit-2", Amount = 20 }, default));
        Assert.Equal(90m, wallet.LockedBalance);
    }
}
