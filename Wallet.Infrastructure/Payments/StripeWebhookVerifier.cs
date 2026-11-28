namespace Wallet.Infrastructure.Payments;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stripe;
using Wallet.Application.Interfaces;

public sealed class StripeWebhookVerifier : IStripeWebhookVerifier
{
    public const int MaximumPayloadBytes = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _secret;

    public StripeWebhookVerifier(IConfiguration configuration)
    {
        _secret = configuration["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe webhook signing secret is required.");
        if (!_secret.StartsWith("whsec_", StringComparison.Ordinal) ||
            _secret.Length <= "whsec_".Length || _secret.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("Stripe webhook signing secret is invalid.");
        }
    }

    public VerifiedStripeWebhookEvent Verify(ReadOnlyMemory<byte> payload, string signatureHeader)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadBytes)
            throw new ArgumentException("Stripe webhook payload size is invalid.", nameof(payload));
        if (string.IsNullOrWhiteSpace(signatureHeader) || signatureHeader.Length > 8192)
            throw new StripeWebhookSignatureException(
                new ArgumentException("Stripe signature header is invalid.", nameof(signatureHeader)));

        var json = StrictUtf8.GetString(payload.Span);
        try
        {
            EventUtility.ValidateSignature(json, signatureHeader, _secret);
        }
        catch (StripeException exception)
        {
            throw new StripeWebhookSignatureException(exception);
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(json);
        }
        catch (StripeException exception)
        {
            throw new StripeWebhookVersionException(exception);
        }
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("livemode", out var liveMode) ||
            liveMode.ValueKind != JsonValueKind.False)
        {
            throw new InvalidOperationException("Stripe webhook must be a test-mode event.");
        }
        if (string.IsNullOrWhiteSpace(stripeEvent.Id) || string.IsNullOrWhiteSpace(stripeEvent.Type))
            throw new InvalidOperationException("Stripe webhook event ID or type is missing.");

        return new VerifiedStripeWebhookEvent(stripeEvent.Id, stripeEvent.Type, json);
    }
}
