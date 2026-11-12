namespace Wallet.Tests;

using System.Text;
using Microsoft.Extensions.Configuration;
using Stripe;
using Wallet.Application.Interfaces;
using Wallet.Infrastructure.Payments;
using Xunit;

public sealed class StripeWebhookVerifierTests
{
    private const string Secret = "whsec_test_only_not_real";

    [Fact]
    public void ValidSignature_ReturnsVerifiedEventWithoutChangingPayload()
    {
        var body = Payload();
        var result = Verifier().Verify(Encoding.UTF8.GetBytes(body), Signature(body));

        Assert.Equal("evt_test_123", result.EventId);
        Assert.Equal("checkout.session.completed", result.EventType);
        Assert.Equal(body, result.RawJson);
    }

    [Fact]
    public void ChangedBytes_AreRejected()
    {
        var body = Payload();
        Assert.Throws<StripeWebhookSignatureException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(body + " "), Signature(body)));
    }

    [Fact]
    public void StaleSignature_IsRejected()
    {
        var body = Payload();
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        Assert.Throws<StripeWebhookSignatureException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(body), Signature(body, oldTimestamp)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingSignature_IsRejectedAsSignatureFailure(string signature)
    {
        Assert.Throws<StripeWebhookSignatureException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(Payload()), signature));
    }

    [Fact]
    public void OversizeSignature_IsRejectedAsSignatureFailure()
    {
        Assert.Throws<StripeWebhookSignatureException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(Payload()), new string('x', 8193)));
    }

    [Theory]
    [InlineData("not-a-signature")]
    [InlineData("t=abc,v1=bad")]
    [InlineData("t=1")]
    public void MalformedSignature_IsRejectedAsSignatureFailure(string signature)
    {
        Assert.Throws<StripeWebhookSignatureException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(Payload()), signature));
    }

    [Theory]
    [InlineData("true")]
    public void NonTestMode_IsRejected(string liveMode)
    {
        var body = Payload(liveMode);
        Assert.Throws<InvalidOperationException>(() => Verifier().Verify(Encoding.UTF8.GetBytes(body), Signature(body)));
    }

    [Fact]
    public void MissingLiveMode_IsRejected()
    {
        var body = Payload().Replace("\"livemode\":false,", "");
        Assert.Throws<InvalidOperationException>(() => Verifier().Verify(Encoding.UTF8.GetBytes(body), Signature(body)));
    }

    [Fact]
    public void UnsupportedEventVersion_IsRejected()
    {
        var body = Payload().Replace(StripeConfiguration.ApiVersion, "2019-01-01");
        Assert.Throws<StripeWebhookVersionException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(body), Signature(body)));
    }

    [Fact]
    public void OversizePayload_IsRejectedBeforeParsing()
    {
        var body = new byte[StripeWebhookVerifier.MaximumPayloadBytes + 1];
        Assert.Throws<ArgumentException>(() => Verifier().Verify(body, "test"));
    }

    [Fact]
    public void SignatureForAnotherSecret_IsRejected()
    {
        var body = Payload();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = EventUtility.ComputeSignature("whsec_other_test_key", timestamp, body);
        Assert.Throws<StripeWebhookSignatureException>(() =>
            Verifier().Verify(Encoding.UTF8.GetBytes(body), $"t={timestamp},v1={signature}"));
    }

    [Fact]
    public void SignedMalformedEvent_IsNotMisclassifiedAsSignatureFailure()
    {
        const string body = "{\"object\":\"v2.core.event\"}";
        var exception = Record.Exception(() => Verifier().Verify(Encoding.UTF8.GetBytes(body), Signature(body)));
        Assert.NotNull(exception);
        Assert.IsNotType<StripeWebhookSignatureException>(exception);
    }

    [Fact]
    public void InvalidUtf8_IsRejected()
    {
        Assert.Throws<DecoderFallbackException>(() => Verifier().Verify(new byte[] { 0xff }, "t=1,v1=00"));
    }

    [Fact]
    public void MissingSigningSecret_IsRejected()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.Throws<InvalidOperationException>(() => new StripeWebhookVerifier(configuration));
    }

    private static StripeWebhookVerifier Verifier() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:WebhookSecret"] = Secret
        }).Build());

    private static string Payload(string liveMode = "false") =>
        $$$$"""{"id":"evt_test_123","object":"event","api_version":"{{{{StripeConfiguration.ApiVersion}}}}","type":"checkout.session.completed","livemode":{{{{liveMode}}}},"created":1900000000,"data":{"object":{"id":"cs_test_123","object":"checkout.session"}}}""";

    private static string Signature(string payload, long? timestamp = null)
    {
        var time = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        return $"t={time},v1={EventUtility.ComputeSignature(Secret, time, payload)}";
    }
}
