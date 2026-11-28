namespace Wallet.Application.Features.Stripe;

using Wallet.Application.Interfaces;

internal static class StripeCheckoutSessionValidation
{
    internal static void Validate(StripeCheckoutSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.SessionId) ||
            !session.SessionId.StartsWith("cs_test_", StringComparison.Ordinal) ||
            session.SessionId.Length <= "cs_test_".Length || session.SessionId.Length > 255 ||
            session.SessionId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
            !Uri.TryCreate(session.PaymentUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Stripe returned an invalid checkout session.");
    }
}
