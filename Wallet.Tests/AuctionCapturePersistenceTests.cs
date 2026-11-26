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
public sealed class AuctionCapturePersistenceTests
{
    [Fact]
    public void CaptureAndEscrowModels_EnforceOneRecordPerHold()
    {
        using var context = new WalletDbContext(new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_model_test;Username=test").Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var capture = model.FindEntityType(typeof(AuctionHoldCapture))!;
        var escrow = model.FindEntityType(typeof(AuctionDepositEscrow))!;
        Assert.Contains(capture.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "HoldId" }));
        Assert.Contains(escrow.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "HoldId" }));
        Assert.Contains(escrow.GetCheckConstraints(), constraint => constraint.Sql.Contains("\"RemainingAmount\" >= 0"));
    }

    [DisposablePostgresFact]
    public async Task Database_RejectsDuplicateCapturesAndInvalidEscrowOrTransfer()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var walletId = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var userId = Guid.NewGuid();
            setup.Wallets.Add(new WalletAccount { Id = walletId, UserId = userId, Currency = CurrencyCode.Vnd, Balance = 100 });
            setup.AuctionHolds.Add(new AuctionHold
            {
                Id = holdId, WalletId = walletId, OwnerUserId = userId, ReferenceType = "AUCTION_DEPOSIT",
                ReferenceId = Guid.NewGuid().ToString(), Amount = 50, MetadataJson = "{}"
            });
            setup.AuctionHoldCaptures.Add(new AuctionHoldCapture
            {
                Id = captureId, HoldId = holdId, Purpose = "ORDER_PAYMENT", OrderId = Guid.NewGuid(), RequestFingerprint = "hash"
            });
            await setup.SaveChangesAsync();
        }

        await using (var duplicate = new WalletDbContext(options))
        {
            duplicate.AuctionHoldCaptures.Add(new AuctionHoldCapture
            {
                Id = Guid.NewGuid(), HoldId = holdId, Purpose = "FORFEIT", RequestFingerprint = "hash"
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var invalidEscrow = new WalletDbContext(options))
        {
            invalidEscrow.AuctionDepositEscrows.Add(new AuctionDepositEscrow
            {
                Id = Guid.NewGuid(), HoldId = holdId, OrderId = Guid.NewGuid(), OriginalAmount = 50, RemainingAmount = -1
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalidEscrow.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var invalidTransfer = new WalletDbContext(options))
        {
            invalidTransfer.AuctionCaptureTransfers.Add(new AuctionCaptureTransfer
            {
                Id = Guid.NewGuid(), CaptureId = captureId, RecipientType = "PLATFORM", Amount = 0
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalidTransfer.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }
}
