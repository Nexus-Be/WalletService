namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class AuctionHoldDebitRaceTests
{
    [DisposablePostgresTheory]
    [InlineData("checkout")]
    [InlineData("transfer")]
    [InlineData("admin")]
    public async Task CompetingDebitAndHold_OnlyOneCanSpendAvailableFunds(string path)
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var buyer = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.AddRange(
                new WalletAccount { Id = Guid.NewGuid(), UserId = buyer, Balance = 100000, Currency = CurrencyCode.Vnd },
                new WalletAccount { Id = Guid.NewGuid(), UserId = recipient, Balance = 0, Currency = CurrencyCode.Vnd });
            await setup.SaveChangesAsync();
        }
        var hold = new CreateHoldCommand(buyer, "AUCTION_DEPOSIT", Guid.NewGuid().ToString(), 70000, "VND", null)
            { IdempotencyKey = $"hold:{buyer}" };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        async Task Debit()
        {
            switch (path)
            {
                case "checkout":
                    await new PayOrderWithWalletCommandHandler(b).Handle(new PayOrderWithWalletCommand(70000)
                        { OrderId = Guid.NewGuid(), UserId = buyer.ToString() }, default);
                    break;
                case "transfer":
                    await new TransferCommandHandler(b).Handle(new TransferCommand(recipient.ToString(), 70000)
                        { SenderUserId = buyer.ToString() }, default);
                    break;
                case "admin":
                    await new AdjustWalletBalanceCommandHandler(b).Handle(new AdjustWalletBalanceCommand(70000, "Debit", "Test")
                        { UserId = buyer, AdminUserId = Guid.NewGuid().ToString() }, default);
                    break;
                default: throw new InvalidOperationException("Unknown test path.");
            }
        }
        var outcomes = await Task.WhenAll(Attempt(() => new CreateHoldCommandHandler(a).Handle(hold, default)), Attempt(Debit));
        Assert.Single(outcomes, succeeded => succeeded);
        await using var verify = new WalletDbContext(options);
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == buyer);
        Assert.True(wallet.LockedBalance <= wallet.Balance);
        Assert.Equal(30000, wallet.AvailableBalance);
    }

    private static async Task<bool> Attempt(Func<Task> action)
    {
        try { await action(); return true; }
        catch (InsufficientBalanceException) { return false; }
    }

    private sealed class DisposablePostgresTheoryAttribute : TheoryAttribute
    {
        public DisposablePostgresTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }
}
