namespace Wallet.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Interfaces;
using Wallet.Infrastructure.Payments;
using Xunit;

public sealed class StripeCheckoutClientTests
{
    [Fact]
    public async Task CreateSession_SendsWholeVndAndStableCorrelation()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new StripeCheckoutClient(http, Configuration());
        var attemptId = Guid.NewGuid();

        var result = await client.CreateSessionAsync(
            new StripeCheckoutRequest(attemptId, 100000, "provider-key"), default);

        Assert.Equal("cs_test_123", result.SessionId);
        Assert.Equal("https://checkout.stripe.com/test", result.PaymentUrl);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/checkout/sessions", handler.Path);
        Assert.Equal("provider-key", handler.IdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(handler.ApiVersion));
        var body = WebUtility.UrlDecode(handler.Body);
        Assert.Contains("[unit_amount]=100000", body);
        Assert.Contains("[currency]=vnd", body);
        Assert.Contains(attemptId.ToString(), body);
        Assert.Contains("mode=payment", body);
        Assert.Contains("after_expiration[recovery][enabled]=false", body);
    }

    [Fact]
    public void LiveCredentials_AreRejected()
    {
        using var http = new HttpClient(new RecordingHandler());
        Assert.Throws<InvalidOperationException>(() =>
            new StripeCheckoutClient(http, Configuration("sk_live_not-a-real-key")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100000000)]
    public async Task InvalidAmounts_AreRejectedWithoutProviderCall(long amount)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new StripeCheckoutClient(http, Configuration());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.CreateSessionAsync(new StripeCheckoutRequest(Guid.NewGuid(), amount, "key"), default));
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task ProviderErrors_AreNotTreatedAsSuccess()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler);
        var client = new StripeCheckoutClient(http, Configuration());

        var exception = await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() =>
            client.CreateSessionAsync(new StripeCheckoutRequest(Guid.NewGuid(), 100000, "key"), default));
        Assert.IsType<Stripe.StripeException>(exception.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NetworkFailureOrTimeout_IsProviderUnavailable(bool timeout)
    {
        using var http = new HttpClient(new FailureHandler(timeout));
        var client = new StripeCheckoutClient(http, Configuration());

        var exception = await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() =>
            client.CreateSessionAsync(new StripeCheckoutRequest(Guid.NewGuid(), 100000, "key"), default));

        Assert.NotNull(exception.InnerException);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("live")]
    [InlineData("mode")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("reference")]
    [InlineData("metadata")]
    [InlineData("recovery")]
    [InlineData("missing-recovery")]
    public async Task MismatchedSession_IsNotReturned(string mismatch)
    {
        var handler = new RecordingHandler(mismatch: mismatch);
        using var http = new HttpClient(handler);
        var client = new StripeCheckoutClient(http, Configuration());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CreateSessionAsync(new StripeCheckoutRequest(Guid.NewGuid(), 100000, "key"), default));
    }

    [Fact]
    public async Task Cancellation_StopsTheProviderCall()
    {
        using var http = new HttpClient(new BlockingHandler());
        var client = new StripeCheckoutClient(http, Configuration());
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CreateSessionAsync(new StripeCheckoutRequest(Guid.NewGuid(), 100000, "key"), cancellation.Token));
    }

    [Fact]
    public void NonHttpsRedirect_IsRejected()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_not-a-real-key",
            ["Stripe:SuccessUrl"] = "http://example.test/success",
            ["Stripe:CancelUrl"] = "https://example.test/cancel"
        }).Build();
        using var http = new HttpClient(new RecordingHandler());
        Assert.Throws<InvalidOperationException>(() => new StripeCheckoutClient(http, configuration));
    }

    private static IConfiguration Configuration(string key = "sk_test_not-a-real-key") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = key,
            ["Stripe:SuccessUrl"] = "https://example.test/success",
            ["Stripe:CancelUrl"] = "https://example.test/cancel"
        }).Build();

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string? mismatch = null) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? ApiVersion { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            ApiVersion = request.Headers.GetValues("Stripe-Version").Single();
            Body = await request.Content!.ReadAsStringAsync(token);
            var body = WebUtility.UrlDecode(Body);
            var reference = body.Split('&')
                .Single(part => part.StartsWith("client_reference_id=", StringComparison.Ordinal))
                .Split('=', 2)[1];
            var json = status == HttpStatusCode.OK
                ? JsonSerializer.Serialize(new
                {
                    id = mismatch == "id" ? "cs_live_123" : "cs_test_123",
                    @object = "checkout.session",
                    url = "https://checkout.stripe.com/test",
                    expires_at = 1900000000,
                    livemode = mismatch == "live",
                    mode = mismatch == "mode" ? "setup" : "payment",
                    currency = mismatch == "currency" ? "usd" : "vnd",
                    amount_total = mismatch == "amount" ? 1 : 100000,
                    client_reference_id = mismatch == "reference" ? Guid.NewGuid().ToString() : reference,
                    after_expiration = mismatch == "missing-recovery"
                        ? new { recovery = (object)new { } }
                        : new { recovery = (object)new { enabled = mismatch == "recovery" } },
                    metadata = new Dictionary<string, string>
                    {
                        ["attempt_id"] = mismatch == "metadata" ? Guid.NewGuid().ToString() : reference
                    }
                })
                : """{"error":{"message":"Test provider error","type":"api_error"}}""";
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The delay should have been cancelled.");
        }
    }

    private sealed class FailureHandler(bool timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            timeout
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated timeout."))
                : Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated network failure."));
    }
}
