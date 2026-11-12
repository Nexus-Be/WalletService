namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Entities;
using Wallet.Infrastructure.Persistence;
using Xunit;

public sealed class StripeLegacyReferenceTests
{
    [Theory]
    [InlineData(typeof(WalletTopup))]
    [InlineData(typeof(DirectPaymentOrder))]
    public void LegacyReference_IsOptionalButRemainsUnique(Type entityType)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_model_test;Username=test")
            .Options;
        using var context = new WalletDbContext(options);
        var entity = context.Model.FindEntityType(entityType)!;
        var reference = entity.FindProperty("VnpTxnRef")!;

        Assert.True(reference.IsNullable);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Count == 1 && index.Properties[0] == reference);
    }
}
