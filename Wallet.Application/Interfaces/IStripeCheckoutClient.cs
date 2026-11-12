namespace Wallet.Application.Interfaces;

public static class StripeAmountLimits
{
    public const long MaximumVnd = 99999999;
}

public sealed record StripeCheckoutRequest(Guid AttemptId, long AmountVnd, string IdempotencyKey);

public sealed record StripeCheckoutSession(string SessionId, string PaymentUrl, DateTimeOffset ExpiresAt);

public sealed record StripeSessionSnapshot(
    string SessionId, string SessionStatus, string PaymentStatus, long AmountVnd,
    string? PaymentIntentId, string? ClientReference, string? AttemptReference, bool? RecoveryEnabled);

public sealed record StripePaymentIntentSnapshot(
    string PaymentIntentId, string Status, long AmountVnd, long AmountReceived, string? AttemptReference);

public interface IStripeCheckoutClient
{
    Task<StripeCheckoutSession> CreateSessionAsync(
        StripeCheckoutRequest request,
        CancellationToken cancellationToken);

    Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken);
}
