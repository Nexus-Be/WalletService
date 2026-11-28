namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;
using Wallet.Domain.Enums;

public class StripePaymentAttempt : BaseEntity<Guid>
{
    public StripePaymentPurpose Purpose { get; init; }
    public Guid OwnerId { get; init; }
    public Guid? WalletTopupId { get; init; }
    public Guid? DirectPaymentOrderId { get; init; }
    public decimal Amount { get; init; }
    public required string RequestFingerprint { get; init; }
    public required string ProviderIdempotencyKey { get; init; }
    public string? ClientIdempotencyKey { get; init; }
    public string? CheckoutSessionId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTimeOffset? SessionExpiresAt { get; set; }
    public StripePaymentAttemptStatus Status { get; set; } = StripePaymentAttemptStatus.CreationPending;
    public bool BlocksReplacement { get; set; } = true;
}
