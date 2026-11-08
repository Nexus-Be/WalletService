namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Wallet.Infrastructure.Persistence;
using Wallet.Infrastructure.Persistence.Migrations;
using Wallet.Infrastructure.Persistence.Seeds;
using Xunit;

public class SeedDataTests
{
    private static WalletDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new WalletDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void Model_HasNoSeedData()
    {
        using var context = NewContext(nameof(Model_HasNoSeedData));

        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.All(model.GetEntityTypes(), entityType => Assert.Empty(entityType.GetSeedData()));
    }

    [Fact]
    public void Migrations_DoNotInsertData()
    {
        var migration = new InitialCreate();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is InsertDataOperation);
    }

    [Fact]
    public void FreshDatabase_HasNoWalletsOrFunds()
    {
        using var context = NewContext(nameof(FreshDatabase_HasNoWalletsOrFunds));

        Assert.Empty(context.Wallets);
        Assert.Empty(context.WalletTransactions);
        Assert.Empty(context.WithdrawalRequests);
    }

    [Fact]
    public void DevelopmentSeed_PopulatesDemoWallets()
    {
        using var context = NewContext(nameof(DevelopmentSeed_PopulatesDemoWallets));

        WalletSeedData.Seed(context);

        Assert.NotEmpty(context.Wallets);
        Assert.NotEmpty(context.BankAccounts);
    }

    [Fact]
    public void DevelopmentSeed_IsIdempotentAndDoesNotOverwriteChangedRows()
    {
        using var context = NewContext(nameof(DevelopmentSeed_IsIdempotentAndDoesNotOverwriteChangedRows));
        WalletSeedData.Seed(context);
        var walletCount = context.Wallets.Count();
        var transactionCount = context.WalletTransactions.Count();
        var wallet = context.Wallets.First();
        wallet.Balance = 123m;
        context.SaveChanges();

        WalletSeedData.Seed(context);

        Assert.Equal(walletCount, context.Wallets.Count());
        Assert.Equal(transactionCount, context.WalletTransactions.Count());
        Assert.Equal(123m, context.Wallets.Single(w => w.Id == wallet.Id).Balance);
    }
}
