namespace Wallet.Tests;

using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeTopupPreparationTests
{
    [Fact]
    public async Task Topup_PersistsBeforeProviderCallAndReplaysSameSession()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider(context);
        var result = await StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, "client-key", default);
        var replay = await StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, "client-key", default);

        Assert.Equal(result, replay);
        Assert.Equal(1, provider.Calls);
        Assert.Equal("https://checkout.stripe.com/test", result.PaymentUrl);
        Assert.Single(context.WalletTopups);
        var attempt = Assert.Single(context.StripePaymentAttempts);
        Assert.Equal(StripePaymentAttemptStatus.AwaitingPayment, attempt.Status);
        Assert.Equal(result.TopupId, attempt.WalletTopupId);
        Assert.Single(context.AuditLogs);
    }

    [Fact]
    public async Task ProviderFailure_LeavesBlockingAttemptAndDoesNotRecreateOnRetry()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider(context) { Fail = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, "client-key", default));
        var attempt = Assert.Single(context.StripePaymentAttempts);
        Assert.True(attempt.BlocksReplacement);
        Assert.Equal(StripePaymentAttemptStatus.CreationPending, attempt.Status);

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, "client-key", default));
        Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task SameKeyWithDifferentAmount_IsAConflict()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider(context);
        await StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, "client-key", default);
        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 200000, "client-key", default));
        Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(100000000)]
    [InlineData(10000.5)]
    public async Task InvalidAmounts_DoNotCreatePayment(decimal amount)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider(context);
        await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, amount, "client-key", default));
        Assert.Empty(context.StripePaymentAttempts);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task MaximumStripeAmount_IsAccepted()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var result = await StripeTopupPreparation.CreateAsync(
            context, new Provider(context), wallet.UserId, 99999999, "max", default);
        Assert.Equal(99999999m, result.Amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("client key")]
    [InlineData("client/key")]
    [InlineData("client\nkey")]
    [InlineData("clé")]
    public async Task InvalidIdempotencyKey_DoesNotCreatePayment(string key)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider(context);
        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, key, default));
        Assert.Equal("VAL_INVALID_INPUT", exception.ErrorCode);
        Assert.Empty(context.StripePaymentAttempts);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task IdempotencyKeyLength_IsEnforced(int length, bool valid)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider(context);
        var key = new string('a', length);
        if (valid)
            await StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, key, default);
        else
            await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                StripeTopupPreparation.CreateAsync(context, provider, wallet.UserId, 100000, key, default));

        Assert.Equal(valid ? 1 : 0, provider.Calls);
        Assert.Equal(valid ? 1 : 0, context.StripePaymentAttempts.Count());
    }

    private static async Task<WalletAccount> AddWallet(TestWalletDbContext context)
    {
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Currency = CurrencyCode.Vnd };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        return wallet;
    }

    private sealed class Provider(TestWalletDbContext context) : IStripeCheckoutClient
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }

        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
        {
            Calls++;
            var attempt = context.StripePaymentAttempts.Single(item => item.Id == request.AttemptId);
            Assert.Equal(StripePaymentAttemptStatus.CreationPending, attempt.Status);
            Assert.Equal(attempt.ProviderIdempotencyKey, request.IdempotencyKey);
            if (Fail)
                throw new InvalidOperationException("Simulated provider failure.");
            return Task.FromResult(new StripeCheckoutSession("cs_test_123", "https://checkout.stripe.com/test",
                DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
}
