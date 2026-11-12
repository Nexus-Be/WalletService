namespace Wallet.Tests;

using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeDirectPreparationTests
{
    [Fact]
    public async Task ActiveDirectAttempt_ReusesSessionWithoutChangingWallet()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider();
        var order = Guid.NewGuid();
        var first = await StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, order, 100000, default);
        var replay = await StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, order, 100000, default);
        Assert.Equal(first, replay);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(300000m, wallet.Balance);
        Assert.Single(context.DirectPaymentOrders);
        Assert.Single(context.StripePaymentAttempts);
        Assert.Single(context.WalletTransactions);
    }

    [Fact]
    public async Task ProviderFailure_KeepsOrderBlockingAndRejectsRetry()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider { Fail = true };
        var order = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, order, 100000, default));
        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, order, 100000, default));
        Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        Assert.Equal(1, provider.Calls);
        Assert.True(context.StripePaymentAttempts.Single().BlocksReplacement);
    }

    [Fact]
    public async Task ConfirmedFailure_AllowsReplacementButChangedAmountDoesNot()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider();
        var orderId = Guid.NewGuid();
        await StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, orderId, 100000, default);
        var first = context.StripePaymentAttempts.Single();
        var reference = first.Id.ToString();
        var session = new StripeSessionSnapshot(first.CheckoutSessionId!, "expired", "unpaid", 100000,
            null, reference, reference, false);
        Assert.Equal(StripeSettlementOutcome.FinalizedFailure, await StripeSettlement.ApplyAsync(
            context, first.Id, "evt_expired", "checkout.session.expired", session, null, default));

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, orderId, 200000, default));
        Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        var replacement = await StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, orderId, 100000, default);
        Assert.NotEmpty(replacement.PaymentUrl);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, context.StripePaymentAttempts.Count());
        Assert.Single(context.StripePaymentAttempts.Where(item => item.BlocksReplacement));
        Assert.Equal(TransactionStatus.Pending, context.WalletTransactions.Single().Status);
        Assert.Equal(300000m, wallet.Balance);
    }

    [Fact]
    public async Task LegacyDirectOrder_IsNotReplacedByStripe()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var orderId = Guid.NewGuid();
        context.DirectPaymentOrders.Add(new DirectPaymentOrder
        {
            Id = Guid.NewGuid(), OrderId = orderId, VnpTxnRef = "legacy", Amount = 100000, Status = TopupStatus.Pending
        });
        await context.SaveChangesAsync();
        var provider = new Provider();
        await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, orderId, 100000, default));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task DirectReplay_FromDifferentOwner_IsRejected()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = await AddWallet(context);
        var other = await AddWallet(context);
        var provider = new Provider();
        var order = Guid.NewGuid();
        await StripeDirectPreparation.CreateAsync(context, provider, owner.UserId, order, 100000, default);
        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeDirectPreparation.CreateAsync(context, provider, other.UserId, order, 100000, default));
        Assert.Equal("PAYMENT_CONFLICT", exception.ErrorCode);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(300000m, other.Balance);
    }

    [Theory]
    [InlineData(99999999, true)]
    [InlineData(100000000, false)]
    public async Task Amount_RespectsStripeMaximum(decimal amount, bool allowed)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var wallet = await AddWallet(context);
        var provider = new Provider();
        if (allowed)
        {
            var result = await StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, Guid.NewGuid(), amount, default);
            Assert.Equal(amount, result.Amount);
        }
        else
        {
            await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                StripeDirectPreparation.CreateAsync(context, provider, wallet.UserId, Guid.NewGuid(), amount, default));
            Assert.Empty(context.StripePaymentAttempts);
        }
    }

    private static async Task<WalletAccount> AddWallet(TestWalletDbContext context)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Currency = CurrencyCode.Vnd, Balance = 300000
        };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        return wallet;
    }

    private sealed class Provider : IStripeCheckoutClient
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("Simulated provider failure.");
            return Task.FromResult(new StripeCheckoutSession($"cs_test_{request.AttemptId:N}",
                "https://checkout.stripe.com/test", DateTimeOffset.UtcNow.AddHours(1)));
        }
        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
}
