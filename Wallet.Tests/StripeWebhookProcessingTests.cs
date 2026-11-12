namespace Wallet.Tests;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stripe;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Payments;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeWebhookProcessingTests
{
    private const string Secret = "whsec_test_only_not_real";

    [Fact]
    public async Task SignedPaidEvent_RetrievesProviderStateBeforeSettlement()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(context);
        var provider = new Provider(attempt);
        var body = Payload(attempt, "checkout.session.completed");

        var outcome = await StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body), Signature(body), default);

        Assert.Equal(StripeWebhookProcessingOutcome.Settled, outcome);
        Assert.Equal(1, provider.SessionReads);
        Assert.Equal(1, provider.IntentReads);
        Assert.Equal(100100m, wallet.Balance);
        Assert.Single(context.StripeWebhookEvents);
    }

    [Fact]
    public async Task InvalidSignature_DoesNotReadProviderOrChangeBalance()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(context);
        var provider = new Provider(attempt);
        var body = Payload(attempt, "checkout.session.completed");
        await Assert.ThrowsAsync<StripeWebhookSignatureException>(() => StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body + " "), Signature(body), default));
        Assert.Equal(0, provider.SessionReads);
        Assert.Equal(100m, wallet.Balance);
        Assert.Empty(context.StripeWebhookEvents);
    }

    [Fact]
    public async Task EventCannotOverrideUnpaidProviderState()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(context);
        var provider = new Provider(attempt) { Unpaid = true };
        var body = Payload(attempt, "checkout.session.async_payment_succeeded");

        var outcome = await StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body), Signature(body), default);

        Assert.Equal(StripeWebhookProcessingOutcome.RetryRequired, outcome);
        Assert.Equal(100m, wallet.Balance);
        Assert.Empty(context.StripeWebhookEvents);
    }

    [Fact]
    public async Task UnrelatedSignedEvent_IsIgnoredWithoutProviderRead()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, attempt) = await Seed(context);
        var provider = new Provider(attempt);
        var body = Payload(attempt, "customer.created");
        var outcome = await StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body), Signature(body), default);
        Assert.Equal(StripeWebhookProcessingOutcome.Ignored, outcome);
        Assert.Equal(0, provider.SessionReads);
    }

    [Fact]
    public async Task SignedEvent_WithInvalidSessionId_FailsWithoutProviderReadOrFinancialWrites()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(context);
        var provider = new Provider(attempt);
        var body = Payload(attempt, "checkout.session.completed").Replace("cs_test_123", "invalid");

        await Assert.ThrowsAsync<InvalidOperationException>(() => StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body), Signature(body), default));

        Assert.Equal(0, provider.SessionReads);
        Assert.Equal(100m, wallet.Balance);
        Assert.Empty(context.StripeWebhookEvents);
    }

    [Fact]
    public async Task ProviderSession_WithInvalidIntentId_FailsWithoutIntentReadOrFinancialWrites()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(context);
        var provider = new Provider(attempt) { IntentId = "invalid" };
        var body = Payload(attempt, "checkout.session.completed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body), Signature(body), default));

        Assert.Equal(1, provider.SessionReads);
        Assert.Equal(0, provider.IntentReads);
        Assert.Equal(100m, wallet.Balance);
        Assert.Empty(context.StripeWebhookEvents);
    }

    private static async Task<(WalletAccount, StripePaymentAttempt)> Seed(TestWalletDbContext context)
    {
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 100, Currency = CurrencyCode.Vnd };
        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000, Status = TopupStatus.Pending };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.Topup, WalletTopupId = topup.Id,
            Amount = 100000, RequestFingerprint = "fingerprint", ProviderIdempotencyKey = "key",
            CheckoutSessionId = "cs_test_123"
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return (wallet, attempt);
    }

    private static string Payload(StripePaymentAttempt attempt, string type) =>
        JsonSerializer.Serialize(new
        {
            id = "evt_test_123",
            @object = "event",
            api_version = StripeConfiguration.ApiVersion,
            type,
            livemode = false,
            created = 1900000000,
            data = new { @object = new
            {
                id = "cs_test_123", @object = "checkout.session", metadata = new { attempt_id = attempt.Id.ToString() }
            } }
        });

    private static string Signature(string body)
    {
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"t={time},v1={EventUtility.ComputeSignature(Secret, time, body)}";
    }

    private static StripeWebhookVerifier Verifier() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = Secret }).Build());

    private sealed class Provider(StripePaymentAttempt attempt) : IStripeCheckoutClient
    {
        public int SessionReads { get; private set; }
        public int IntentReads { get; private set; }
        public bool Unpaid { get; init; }
        public string IntentId { get; init; } = "pi_123";
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken token)
        {
            SessionReads++;
            return Task.FromResult(new StripeSessionSnapshot(sessionId, "complete", Unpaid ? "unpaid" : "paid",
                100000, IntentId, attempt.Id.ToString(), attempt.Id.ToString(), false));
        }

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token)
        {
            IntentReads++;
            return Task.FromResult(new StripePaymentIntentSnapshot(id, Unpaid ? "processing" : "succeeded",
                100000, Unpaid ? 0 : 100000, attempt.Id.ToString()));
        }
    }
}
