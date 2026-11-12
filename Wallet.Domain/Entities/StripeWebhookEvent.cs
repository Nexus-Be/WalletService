namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

public class StripeWebhookEvent : BaseEntity<Guid>
{
    public required string ProviderEventId { get; set; }
    public required string EventType { get; set; }
    public Guid? PaymentAttemptId { get; set; }
    public required string Outcome { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
