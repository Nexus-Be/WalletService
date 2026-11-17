namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class AuctionHoldWithdrawalRaceTests
{
    [DisposablePostgresFact]
    public async Task ConcurrentHoldAndWithdrawal_CannotOverreserve()
    {
        var options = await Setup();
        var owner = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            setup.Wallets.Add(new WalletAccount
                { Id = Guid.NewGuid(), UserId = owner, Balance = 100000, Currency = CurrencyCode.Vnd });
            await setup.SaveChangesAsync();
        }
        var reference = Guid.NewGuid().ToString();
        var hold = new CreateHoldCommand(owner, "AUCTION_DEPOSIT", reference, 70000, "VND", null)
            { IdempotencyKey = $"hold:{reference}" };
        var withdrawal = new RequestWithdrawalCommand(70000, "VND", "bank", "123", "Holder")
            { UserId = owner.ToString() };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        var results = await Task.WhenAll(
            Succeeds(() => new CreateHoldCommandHandler(a).Handle(hold, default)),
            Succeeds(() => new RequestWithdrawalCommandHandler(b).Handle(withdrawal, default)));
        Assert.Single(results, value => value);
        await using var verify = new WalletDbContext(options);
        Assert.Equal(70000, (await verify.Wallets.SingleAsync(wallet => wallet.UserId == owner)).LockedBalance);
    }

    [DisposablePostgresFact]
    public async Task FrozenWallet_CannotCreateButCanReleaseExistingHold()
    {
        var options = await Setup();
        var owner = Guid.NewGuid();
        await using var context = new WalletDbContext(options);
        var wallet = new WalletAccount
            { Id = Guid.NewGuid(), UserId = owner, Balance = 100000, Currency = CurrencyCode.Vnd };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        var reference = Guid.NewGuid().ToString();
        var command = new CreateHoldCommand(owner, "AUCTION_DEPOSIT", reference, 30000, "VND", null)
            { IdempotencyKey = $"hold:{reference}" };
        var created = await new CreateHoldCommandHandler(context).Handle(command, default);
        wallet.Status = "SUSPENDED";
        await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new CreateHoldCommandHandler(context).Handle(command with
                { ReferenceId = reference + "-2", IdempotencyKey = $"hold:{reference}:2" }, default));
        Assert.Equal("WALLET_FROZEN", error.ErrorCode);
        var result = await new ReleaseHoldCommandHandler(context).Handle(
            new ReleaseHoldCommand("AUCTION_CANCELLED")
                { HoldId = created.Hold.HoldId, IdempotencyKey = $"release:{reference}" }, default);
        Assert.Equal("Released", result.Status);
        Assert.Equal(0, wallet.LockedBalance);
    }

    private static async Task<bool> Succeeds(Func<Task> action)
    {
        try { await action(); return true; }
        catch (InsufficientBalanceException) { return false; }
    }

    private static async Task<DbContextOptions<WalletDbContext>> Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        return options;
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
