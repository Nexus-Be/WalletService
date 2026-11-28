namespace Wallet.Tests;

using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using Wallet.Api.Controllers;
using Wallet.Api.Middleware;
using Wallet.Application;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Payments;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeWebhookEndpointTests
{
    private const string Secret = "whsec_test_only_not_real";

    [Fact]
    public async Task SignedPaidEvent_IsAcknowledgedAndCreditedOnce()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(db);
        var provider = new Provider(attempt);
        await using var services = Services(db, provider);
        var body = Payload(attempt);

        var first = await Controller(services, body, Signature(body)).StripeWebhook(Signature(body), default);
        var duplicate = await Controller(services, body, Signature(body)).StripeWebhook(Signature(body), default);

        Assert.IsType<OkResult>(first);
        Assert.IsType<OkResult>(duplicate);
        Assert.Equal(100100m, wallet.Balance);
        Assert.Single(db.StripeWebhookEvents);
        Assert.Single(db.WalletTransactions);
    }

    [Fact]
    public async Task UnpaidEvent_ReturnsRetryableServerError()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(db);
        await using var services = Services(db, new Provider(attempt) { Unpaid = true });
        var body = Payload(attempt);
        var controller = Controller(services, body, Signature(body));
        controller.HttpContext.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            async _ => await controller.StripeWebhook(Signature(body), default),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(controller.HttpContext);

        Assert.Equal(500, controller.Response.StatusCode);
        Assert.Equal(100m, wallet.Balance);
        Assert.Empty(db.StripeWebhookEvents);
    }

    [Fact]
    public async Task InvalidSignature_Returns400WithoutProviderRead()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(db);
        var provider = new Provider(attempt);
        await using var services = Services(db, provider);
        var controller = Controller(services, Payload(attempt), "");
        controller.HttpContext.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            async _ => await controller.StripeWebhook("", default),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(controller.HttpContext);

        Assert.Equal(400, controller.Response.StatusCode);
        Assert.Equal(0, provider.Reads);
        Assert.Equal(100m, wallet.Balance);
        Assert.Empty(db.StripeWebhookEvents);
    }

    [Fact]
    public async Task MismatchedProviderAmount_IsRecordedWithoutCreditAndAcknowledged()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, attempt) = await Seed(db);
        await using var services = Services(db, new Provider(attempt) { Amount = 99999 });
        var body = Payload(attempt);

        var response = await Controller(services, body, Signature(body)).StripeWebhook(Signature(body), default);

        Assert.IsType<OkResult>(response);
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal("ReconciliationRequired", Assert.Single(db.StripeWebhookEvents).Outcome);
        Assert.Equal(StripePaymentAttemptStatus.ReconciliationRequired, attempt.Status);
    }

    [Fact]
    public async Task OversizeBody_IsRejectedBeforeDispatch()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, attempt) = await Seed(db);
        var provider = new Provider(attempt);
        await using var services = Services(db, provider);
        var controller = Controller(services, new string('x', StripeWebhookVerifier.MaximumPayloadBytes + 1), "");

        var exception = await Assert.ThrowsAsync<Wallet.Domain.Exceptions.BusinessRuleValidationException>(
            () => controller.StripeWebhook("", default));

        Assert.Equal(413, exception.StatusCode);
        Assert.Equal(0, provider.Reads);
    }

    private static ServiceProvider Services(TestWalletDbContext db, Provider provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddSingleton<IWalletDbContext>(db);
        services.AddSingleton<IStripeCheckoutClient>(provider);
        services.AddSingleton<IStripeWebhookVerifier>(new StripeWebhookVerifier(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = Secret
            }).Build()));
        return services.BuildServiceProvider();
    }

    private static WalletsController Controller(ServiceProvider services, string body, string signature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Headers["Stripe-Signature"] = signature;
        return new WalletsController(services.GetRequiredService<ISender>(), NullLogger<WalletsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static async Task<(WalletAccount, StripePaymentAttempt)> Seed(TestWalletDbContext db)
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
        db.Wallets.Add(wallet);
        db.WalletTopups.Add(topup);
        db.StripePaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return (wallet, attempt);
    }

    private static string Payload(StripePaymentAttempt attempt) => JsonSerializer.Serialize(new
    {
        id = "evt_test_123", @object = "event", api_version = StripeConfiguration.ApiVersion,
        type = "checkout.session.completed", livemode = false, created = 1900000000,
        data = new { @object = new { id = "cs_test_123", @object = "checkout.session",
            metadata = new { attempt_id = attempt.Id.ToString() } } }
    });

    private static string Signature(string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"t={timestamp},v1={EventUtility.ComputeSignature(Secret, timestamp, body)}";
    }

    private sealed class Provider(StripePaymentAttempt attempt) : IStripeCheckoutClient
    {
        public int Reads { get; private set; }
        public bool Unpaid { get; init; }
        public long Amount { get; init; } = 100000;
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token)
        {
            Reads++;
            return Task.FromResult(new StripeSessionSnapshot(id, "complete", Unpaid ? "unpaid" : "paid",
                Amount, "pi_123", attempt.Id.ToString(), attempt.Id.ToString(), false));
        }
        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) =>
            Task.FromResult(new StripePaymentIntentSnapshot(id, Unpaid ? "processing" : "succeeded",
                100000, Unpaid ? 0 : 100000, attempt.Id.ToString()));
    }
}
