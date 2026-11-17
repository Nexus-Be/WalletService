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
public sealed class AuctionHoldOperationPersistenceTests
{
    [Fact]
    public void OperationModel_RequiresUniqueKeyAndRestrictedHold()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuctionHoldOperation))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[] { nameof(AuctionHoldOperation.IdempotencyKey) }));
        Assert.Equal(DeleteBehavior.Restrict, entity.GetForeignKeys().Single().DeleteBehavior);
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Sql.Contains("jsonb_typeof"));
    }

    [Fact]
    public async Task OperationResult_CannotBeChangedAfterSaving()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new WalletDbContext(options);
        var operation = new AuctionHoldOperation
        {
            Id = Guid.NewGuid(), HoldId = Guid.NewGuid(), IdempotencyKey = "hold:test",
            Operation = "create", RequestFingerprint = new string('A', 64), ResponseJson = "{}",
            HttpStatus = 201
        };
        context.AuctionHoldOperations.Add(operation);
        await context.SaveChangesAsync();
        context.Entry(operation).Property(item => item.ResponseJson).CurrentValue = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [DisposablePostgresFact]
    public async Task Database_RejectsReusedOperationKey()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var walletId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        var key = $"hold:{Guid.NewGuid():N}";
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = walletId, UserId = owner, Currency = CurrencyCode.Vnd, Balance = 100
            });
            setup.AuctionHolds.Add(new AuctionHold
            {
                Id = holdId, WalletId = walletId, OwnerUserId = owner, Amount = 10,
                ReferenceType = "AUCTION_DEPOSIT", ReferenceId = Guid.NewGuid().ToString(), MetadataJson = "{}"
            });
            setup.AuctionHoldOperations.Add(NewOperation(holdId, key));
            await setup.SaveChangesAsync();
        }

        await using var duplicate = new WalletDbContext(options);
        duplicate.AuctionHoldOperations.Add(NewOperation(holdId, key));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [DisposablePostgresFact]
    public async Task Database_RollsBackOperationWithBalanceAndHold()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        var key = $"hold:{Guid.NewGuid():N}";
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = walletId, UserId = owner, Currency = CurrencyCode.Vnd, Balance = 100
            });
            await setup.SaveChangesAsync();
        }
        await using (var transactionContext = new WalletDbContext(options))
        {
            await using var transaction = await transactionContext.Database.BeginTransactionAsync();
            var wallet = await transactionContext.LockWalletByIdAsync(walletId, default);
            wallet!.LockFunds(10);
            transactionContext.AuctionHolds.Add(new AuctionHold
            {
                Id = holdId, WalletId = walletId, OwnerUserId = owner, Amount = 10,
                ReferenceType = "AUCTION_DEPOSIT", ReferenceId = Guid.NewGuid().ToString(), MetadataJson = "{}"
            });
            transactionContext.AuctionHoldOperations.Add(NewOperation(holdId, key));
            await transactionContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using var verify = new WalletDbContext(options);
        Assert.Equal(0m, (await verify.Wallets.SingleAsync(wallet => wallet.Id == walletId)).LockedBalance);
        Assert.False(await verify.AuctionHolds.AnyAsync(hold => hold.Id == holdId));
        Assert.False(await verify.AuctionHoldOperations.AnyAsync(operation => operation.IdempotencyKey == key));
    }

    private static AuctionHoldOperation NewOperation(Guid holdId, string key) => new()
    {
        Id = Guid.NewGuid(), HoldId = holdId, IdempotencyKey = key, Operation = "create",
        RequestFingerprint = new string('A', 64), ResponseJson = "{}", HttpStatus = 201
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
            .UseNpgsql("Host=localhost;Database=wallet_model_test;Username=test").Options;
        return new WalletDbContext(options);
    }
}
