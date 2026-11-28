namespace Wallet.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wallet.Application.Common.Exceptions;
using Wallet.Infrastructure.Payments;
using Xunit;

public sealed class StripeReconciliationClientTests
{
    [Fact]
    public async Task GetSession_ReturnsProviderStatusWithoutTreatingUnpaidAsSuccess()
    {
        var handler = new ResponseHandler("""
            {"id":"cs_test_123","object":"checkout.session","mode":"payment","livemode":false,
             "status":"complete","payment_status":"unpaid","amount_total":100000,"currency":"vnd",
             "payment_intent":"pi_123","client_reference_id":"attempt-ref",
             "metadata":{"attempt_id":"attempt-ref"}}
            """);
        using var http = new HttpClient(handler);
        var result = await CreateClient(http).GetSessionAsync("cs_test_123", default);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v1/checkout/sessions/cs_test_123", handler.Path);
        Assert.Equal("complete", result.SessionStatus);
        Assert.Equal("unpaid", result.PaymentStatus);
        Assert.Equal(100000, result.AmountVnd);
        Assert.Equal("pi_123", result.PaymentIntentId);
        Assert.Equal("attempt-ref", result.AttemptReference);
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("{\"recovery\":{\"enabled\":true}}", true)]
    [InlineData("{\"recovery\":{\"enabled\":false}}", false)]
    [InlineData("{}", null)]
    public async Task GetSession_PreservesRecoveryConfiguration(string afterExpiration, bool? expected)
    {
        var json = $$"""
            {"id":"cs_test_123","object":"checkout.session","mode":"payment","livemode":false,
             "status":"expired","payment_status":"unpaid","amount_total":100000,"currency":"vnd",
             "after_expiration":{{afterExpiration}}}
            """;
        using var http = new HttpClient(new ResponseHandler(json));
        var result = await CreateClient(http).GetSessionAsync("cs_test_123", default);
        Assert.Equal(expected, result.RecoveryEnabled);
    }

    [Fact]
    public async Task GetPaymentIntent_ReturnsProviderStatusAndAmounts()
    {
        var handler = new ResponseHandler("""
            {"id":"pi_123","object":"payment_intent","livemode":false,"status":"processing",
             "amount":100000,"amount_received":0,"currency":"vnd","metadata":{"attempt_id":"attempt-ref"}}
            """);
        using var http = new HttpClient(handler);
        var result = await CreateClient(http).GetPaymentIntentAsync("pi_123", default);

        Assert.Equal("/v1/payment_intents/pi_123", handler.Path);
        Assert.Equal("processing", result.Status);
        Assert.Equal(0, result.AmountReceived);
        Assert.Equal("attempt-ref", result.AttemptReference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cs_test_123/other")]
    [InlineData("pi_123")]
    public async Task GetSession_InvalidIdentifierFailsBeforeNetworkCall(string identifier)
    {
        var handler = new ResponseHandler("{}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateClient(http).GetSessionAsync(identifier, default));
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("true", "vnd")]
    [InlineData("false", "usd")]
    public async Task GetSession_RejectsLiveModeAndWrongCurrency(string liveMode, string currency)
    {
        var json = $$"""
            {"id":"cs_test_123","object":"checkout.session","mode":"payment","livemode":{{liveMode}},
             "status":"open","payment_status":"unpaid","amount_total":100000,"currency":"{{currency}}"}
            """;
        using var http = new HttpClient(new ResponseHandler(json));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateClient(http).GetSessionAsync("cs_test_123", default));
    }

    [Fact]
    public async Task GetSession_ProviderErrorRemainsVisible()
    {
        using var http = new HttpClient(new ResponseHandler(
            """{"error":{"message":"Unavailable","type":"api_error"}}""", HttpStatusCode.ServiceUnavailable));
        var exception = await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() =>
            CreateClient(http).GetSessionAsync("cs_test_123", default));
        Assert.IsType<Stripe.StripeException>(exception.InnerException);
    }

    [Fact]
    public async Task GetPaymentIntent_ProviderErrorRemainsVisible()
    {
        using var http = new HttpClient(new ResponseHandler(
            """{"error":{"message":"Unavailable","type":"api_error"}}""", HttpStatusCode.ServiceUnavailable));
        var exception = await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() =>
            CreateClient(http).GetPaymentIntentAsync("pi_123", default));
        Assert.IsType<Stripe.StripeException>(exception.InnerException);
    }

    [Theory]
    [InlineData("pi_other", false, "vnd")]
    [InlineData("pi_123", true, "vnd")]
    [InlineData("pi_123", false, "usd")]
    public async Task GetPaymentIntent_RejectsMismatchedOrUnsupportedResponse(
        string returnedId, bool liveMode, string currency)
    {
        var json = $$"""
            {"id":"{{returnedId}}","object":"payment_intent","livemode":{{liveMode.ToString().ToLowerInvariant()}},
             "status":"processing","amount":100000,"amount_received":0,"currency":"{{currency}}"}
            """;
        using var http = new HttpClient(new ResponseHandler(json));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateClient(http).GetPaymentIntentAsync("pi_123", default));
    }

    [Fact]
    public async Task GetPaymentIntent_InvalidIdentifierFailsBeforeNetworkCall()
    {
        var handler = new ResponseHandler("{}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateClient(http).GetPaymentIntentAsync("pi_123?other=1", default));
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task GetSession_CancellationPropagates()
    {
        using var http = new HttpClient(new BlockingHandler());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(http).GetSessionAsync("cs_test_123", cancellation.Token));
    }

    private static StripeCheckoutClient CreateClient(HttpClient http) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_not-a-real-key",
            ["Stripe:SuccessUrl"] = "https://example.test/success",
            ["Stripe:CancelUrl"] = "https://example.test/cancel"
        }).Build());

    private sealed class ResponseHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
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
}
