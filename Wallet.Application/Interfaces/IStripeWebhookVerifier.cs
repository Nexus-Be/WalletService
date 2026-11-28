namespace Wallet.Application.Interfaces;

public sealed class StripeWebhookSignatureException : Exception
{
    public StripeWebhookSignatureException(Exception innerException)
        : base("Stripe webhook signature is invalid.", innerException)
    {
    }
}

public sealed class StripeWebhookVersionException : Exception
{
    public StripeWebhookVersionException(Exception innerException)
        : base("Stripe webhook API version is incompatible with the configured SDK.", innerException)
    {
    }
}

public sealed class VerifiedStripeWebhookEvent
{
    public VerifiedStripeWebhookEvent(string eventId, string eventType, string rawJson)
    {
        EventId = eventId;
        EventType = eventType;
        RawJson = rawJson;
    }

    public string EventId { get; }
    public string EventType { get; }
    public string RawJson { get; }
}

public interface IStripeWebhookVerifier
{
    VerifiedStripeWebhookEvent Verify(ReadOnlyMemory<byte> payload, string signatureHeader);
}
