namespace Wallet.Tests;

using Wallet.Application.Features.Stripe;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeTopupStatusTests
{
    [Theory]
    [InlineData(TopupStatus.Pending)]
    [InlineData(TopupStatus.Success)]
    [InlineData(TopupStatus.Failed)]
    [InlineData(TopupStatus.Expired)]
    public async Task Owner_CanReadPersistedStatus(TopupStatus status)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup) = await AddTopup(context, status);

        var result = await StripeTopupStatus.GetAsync(context, wallet.UserId, topup.Id, default);

        Assert.Equal(topup.Id, result.TransactionId);
        Assert.Equal(100000m, result.Amount);
        Assert.Equal("Vnd", result.Currency);
        Assert.Equal(status.ToString(), result.Status);
        Assert.Equal(topup.CreatedAt, result.CreatedAt);
        Assert.Equal(topup.UpdatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task OtherOwner_AndUnknownPayment_ReturnNotFound()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup) = await AddTopup(context, TopupStatus.Pending);

        var other = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            StripeTopupStatus.GetAsync(context, Guid.NewGuid(), topup.Id, default));
        var missing = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            StripeTopupStatus.GetAsync(context, wallet.UserId, Guid.NewGuid(), default));

        Assert.Equal("PAYMENT_NOT_FOUND", other.ErrorCode);
        Assert.Equal("PAYMENT_NOT_FOUND", missing.ErrorCode);
        Assert.Equal(404, other.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyIdentifier_IsInvalid(bool emptyOwner)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeTopupStatus.GetAsync(context, emptyOwner ? Guid.Empty : Guid.NewGuid(),
                emptyOwner ? Guid.NewGuid() : Guid.Empty, default));
        Assert.Equal("VAL_INVALID_INPUT", exception.ErrorCode);
    }

    [Fact]
    public async Task LegacyTopup_IsNotExposedAsStripePayment()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup) = await AddTopup(context, TopupStatus.Pending);
        topup.VnpTxnRef = "legacy";
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            StripeTopupStatus.GetAsync(context, wallet.UserId, topup.Id, default));
        Assert.Equal("PAYMENT_NOT_FOUND", exception.ErrorCode);
    }

    private static async Task<(WalletAccount Wallet, WalletTopup Topup)> AddTopup(
        TestWalletDbContext context, TopupStatus status)
    {
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Currency = CurrencyCode.Vnd };
        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000, Status = status };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = wallet.UserId, Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = topup.Id, Amount = topup.Amount, RequestFingerprint = "fingerprint",
            ProviderIdempotencyKey = $"key_{Guid.NewGuid():N}"
        });
        await context.SaveChangesAsync();
        return (wallet, topup);
    }
}
