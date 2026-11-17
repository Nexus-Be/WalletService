namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Infrastructure.Persistence;
using Xunit;

public sealed class AuctionHoldLockTests
{
    [Fact]
    public async Task AdvisoryLock_RequiresTransactionAndNonemptyKey()
    {
        await using var context = CreateContext("Host=localhost;Database=wallet_model_test;Username=test");
        await Assert.ThrowsAsync<ArgumentException>(() => context.LockAuctionKeyAsync("", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockAuctionKeyAsync("reference:test", default));
    }

    [DisposablePostgresFact]
    public async Task AdvisoryLock_SerializesCompetingTransactions()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        await using var first = CreateContext(connectionString);
        await using var second = CreateContext(connectionString);
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var key = $"test:{Guid.NewGuid():N}";
        await first.LockAuctionKeyAsync(key, default);
        var competing = second.LockAuctionKeyAsync(key, default);
        var completed = await Task.WhenAny(competing, Task.Delay(150));
        Assert.NotSame(competing, completed);
        await firstTransaction.CommitAsync();
        await competing.WaitAsync(TimeSpan.FromSeconds(5));
        await secondTransaction.CommitAsync();
    }

    private static WalletDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options);

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }
}
