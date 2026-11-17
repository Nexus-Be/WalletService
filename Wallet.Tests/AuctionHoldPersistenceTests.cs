namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class AuctionHoldPersistenceTests
{
    [Fact]
    public void HoldModel_EnforcesReferenceUniquenessAndWholePositiveAmount()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuctionHold))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[] { nameof(AuctionHold.ReferenceType), nameof(AuctionHold.ReferenceId) }));
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Sql.Contains("trunc("));
        Assert.Equal(DeleteBehavior.Restrict, entity.GetForeignKeys().Single().DeleteBehavior);
    }

    [Fact]
    public void WalletModel_EnforcesLockedBalanceInvariant()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WalletAccount))!;
        Assert.Contains(entity.GetCheckConstraints(), constraint =>
            constraint.Sql.Contains("\"LockedBalance\" >= 0") &&
            constraint.Sql.Contains("\"LockedBalance\" <= \"Balance\""));
    }

    [Fact]
    public async Task HoldIdentity_CannotChangeAfterSaving()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new WalletDbContext(options);
        var hold = NewHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString());
        context.AuctionHolds.Add(hold);
        await context.SaveChangesAsync();
        context.Entry(hold).Property(item => item.Amount).CurrentValue = 2m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [DisposablePostgresFact]
    public async Task Database_RejectsInvalidBalancesAmountsAndDuplicateReferences()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var walletId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var reference = Guid.NewGuid().ToString();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = walletId, UserId = owner, Currency = CurrencyCode.Vnd, Balance = 100
            });
            setup.AuctionHolds.Add(NewHold(walletId, owner, reference));
            await setup.SaveChangesAsync();
        }

        await using (var duplicate = new WalletDbContext(options))
        {
            duplicate.AuctionHolds.Add(NewHold(walletId, owner, reference));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var invalidAmount = new WalletDbContext(options))
        {
            var hold = NewHold(walletId, owner, Guid.NewGuid().ToString(), 1.5m);
            invalidAmount.AuctionHolds.Add(hold);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalidAmount.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var invalidWallet = new WalletDbContext(options))
        {
            var wallet = await invalidWallet.Wallets.SingleAsync(item => item.Id == walletId);
            wallet.LockedBalance = 101;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalidWallet.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }

    private static AuctionHold NewHold(Guid walletId, Guid owner, string reference, decimal amount = 1m) => new()
    {
        Id = Guid.NewGuid(), WalletId = walletId, OwnerUserId = owner,
        ReferenceType = "AUCTION_DEPOSIT", ReferenceId = reference, Amount = amount, MetadataJson = "{}"
    };

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }

    private static WalletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_model_test;Username=test")
            .Options;
        return new WalletDbContext(options);
    }
}
