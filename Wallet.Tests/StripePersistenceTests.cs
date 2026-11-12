namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripePersistenceTests
{
    [Fact]
    public void AttemptModel_EnforcesIdentityAndBlockingAttemptUniqueness()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StripePaymentAttempt))!;
        var indexes = entity.GetIndexes().ToArray();

        AssertUnique(indexes, nameof(StripePaymentAttempt.ProviderIdempotencyKey));
        AssertUnique(indexes, nameof(StripePaymentAttempt.CheckoutSessionId));
        AssertUnique(indexes, nameof(StripePaymentAttempt.PaymentIntentId));
        AssertUnique(indexes, nameof(StripePaymentAttempt.OwnerId), nameof(StripePaymentAttempt.ClientIdempotencyKey));
        Assert.Contains(indexes, index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(StripePaymentAttempt.DirectPaymentOrderId) }) &&
            index.GetFilter()!.Contains("\"BlocksReplacement\""));
        Assert.Contains(indexes, index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(StripePaymentAttempt.WalletTopupId) }) &&
            index.GetFilter()!.Contains("\"BlocksReplacement\""));
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Sql.Contains("trunc("));
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Sql.Contains("\"WalletTopupId\"") &&
            constraint.Sql.Contains("\"DirectPaymentOrderId\""));
    }

    [Fact]
    public void EventModel_EnforcesUniqueProviderEventId()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StripeWebhookEvent))!;
        AssertUnique(entity.GetIndexes(), nameof(StripeWebhookEvent.ProviderEventId));
    }

    [Theory]
    [InlineData(nameof(StripePaymentAttempt.RequestFingerprint), "changed")]
    [InlineData(nameof(StripePaymentAttempt.ProviderIdempotencyKey), "other-key")]
    [InlineData(nameof(StripePaymentAttempt.ClientIdempotencyKey), "other-client-key")]
    [InlineData(nameof(StripePaymentAttempt.Amount), 200000)]
    [InlineData(nameof(StripePaymentAttempt.OwnerId), "00000000-0000-0000-0000-000000000001")]
    [InlineData(nameof(StripePaymentAttempt.DirectPaymentOrderId), "00000000-0000-0000-0000-000000000002")]
    [InlineData(nameof(StripePaymentAttempt.WalletTopupId), "00000000-0000-0000-0000-000000000003")]
    [InlineData(nameof(StripePaymentAttempt.Purpose), 0)]
    public async Task AttemptIdentity_CannotBeChangedAfterSaving(string property, object value)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new WalletDbContext(options);
        var attempt = CreateAttempt();
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();

        var newValue = property switch
        {
            nameof(StripePaymentAttempt.Amount) => Convert.ToDecimal(value),
            nameof(StripePaymentAttempt.OwnerId) or nameof(StripePaymentAttempt.DirectPaymentOrderId)
                or nameof(StripePaymentAttempt.WalletTopupId) => Guid.Parse((string)value),
            nameof(StripePaymentAttempt.Purpose) => (StripePaymentPurpose)(int)value,
            _ => value
        };
        context.Entry(attempt).Property(property).CurrentValue = newValue;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AttemptState_CanChangeAfterSaving()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new WalletDbContext(options);
        var attempt = CreateAttempt();
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();

        attempt.Status = StripePaymentAttemptStatus.AwaitingPayment;
        attempt.CheckoutSessionId = "cs_test_example";
        await context.SaveChangesAsync();

        Assert.Equal(StripePaymentAttemptStatus.AwaitingPayment, attempt.Status);
    }

    [Fact]
    public async Task TestContext_LockHelpersFindMatchingRows()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var attempt = CreateAttempt();
        var order = new DirectPaymentOrder
        {
            Id = attempt.DirectPaymentOrderId!.Value,
            OrderId = Guid.NewGuid(),
            VnpTxnRef = "test-reference",
            Amount = 100000,
            Status = TopupStatus.Pending
        };
        context.StripePaymentAttempts.Add(attempt);
        context.DirectPaymentOrders.Add(order);
        await context.SaveChangesAsync();

        Assert.Equal(attempt.Id, (await context.LockStripePaymentAttemptAsync(attempt.Id, default))?.Id);
        Assert.Equal(order.Id, (await context.LockDirectPaymentOrderByIdAsync(order.Id, default))?.Id);
        Assert.Null(await context.LockStripePaymentAttemptAsync(Guid.NewGuid(), default));
    }

    private static StripePaymentAttempt CreateAttempt() => new()
    {
        Id = Guid.NewGuid(),
        Purpose = StripePaymentPurpose.DirectOrder,
        OwnerId = Guid.NewGuid(),
        DirectPaymentOrderId = Guid.NewGuid(),
        Amount = 100000,
        RequestFingerprint = "fingerprint",
        ProviderIdempotencyKey = "provider-key",
        ClientIdempotencyKey = "client-key"
    };

    private static WalletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_model_test;Username=test;Password=test")
            .Options;
        return new WalletDbContext(options);
    }

    private static void AssertUnique(IEnumerable<IIndex> indexes, params string[] properties)
    {
        Assert.Contains(indexes, index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(properties));
    }
}
