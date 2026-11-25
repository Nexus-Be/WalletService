namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

/// <summary>
/// The shadow ledger guard on a real database: a save that changes a wallet balance without a successful wallet transaction is refused,
/// nothing is written, and the same change with its transaction goes through and leaves exactly one queue row. Needs WALLET_STRIPE_TEST_DATABASE.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public sealed class ShadowLedgerGuardPostgresTests
{
    [DisposablePostgresFact]
    public async Task ABalanceChangeWithoutASuccessfulTransaction_IsRefused_AndNothingIsWritten()
    {
        var (options, walletId) = await Setup();
        try
        {
            await using (var context = new WalletDbContext(options))
            {
                var wallet = await context.Wallets.SingleAsync(item => item.Id == walletId);
                wallet.Deposit(500m); // balance moves, no WalletTransaction is added
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
                Assert.Contains("Shadow ledger guard", ex.Message);
            }

            await using var verify = new WalletDbContext(options);
            Assert.Equal(0m, (await verify.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
            Assert.Equal(0, await verify.LedgerPostingPending.CountAsync(row => row.WalletId == walletId && !row.IsEpochOpening));
        }
        finally
        {
            await SwitchOff(options);
        }
    }

    [DisposablePostgresFact]
    public async Task TheSameChangeWithItsTransaction_IsSaved_WithExactlyOneQueueRowInTheSameTransaction()
    {
        var (options, walletId) = await Setup();
        try
        {
            await using (var context = new WalletDbContext(options))
            {
                var wallet = await context.Wallets.SingleAsync(item => item.Id == walletId);
                var before = wallet.Balance;
                wallet.Deposit(500m);
                context.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = wallet.Id, UserId = wallet.UserId.ToString(), Type = WalletTransactionType.AdjustmentCredit, Amount = 500m, Currency = "Vnd",
                    BalanceBefore = before, BalanceAfter = wallet.Balance, Status = TransactionStatus.Success
                });
                await context.SaveChangesAsync();
            }

            await using var verify = new WalletDbContext(options);
            Assert.Equal(500m, (await verify.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
            var rows = await verify.LedgerPostingPending.Where(row => row.WalletId == walletId && !row.IsEpochOpening).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(500m, row.Delta);
            Assert.Equal(500m, row.WalletBalanceAfter);
        }
        finally
        {
            await SwitchOff(options);
        }
    }

    private static async Task<(DbContextOptions<WalletDbContext> Options, Guid WalletId)> Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        var settings = await context.LedgerSettings.SingleAsync();
        settings.Enabled = true;
        settings.ShadowEpoch = Math.Max(1, settings.ShadowEpoch + 1);
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Balance = 0m, Currency = CurrencyCode.Vnd };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        return (options, wallet.Id);
    }

    private static async Task SwitchOff(DbContextOptions<WalletDbContext> options)
    {
        await using var context = new WalletDbContext(options);
        var settings = await context.LedgerSettings.SingleAsync();
        settings.Enabled = false;
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
