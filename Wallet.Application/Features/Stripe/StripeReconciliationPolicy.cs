namespace Wallet.Application.Features.Stripe;

using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;

public enum StripeReconciliationOutcome
{
    Pending,
    ConfirmedPaid,
    CannotSucceed,
    IntegrityMismatch
}

public static class StripeReconciliationPolicy
{
    public static StripeReconciliationOutcome Evaluate(
        StripePaymentAttempt attempt,
        StripeSessionSnapshot session,
        StripePaymentIntentSnapshot? intent)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(session);

        var reference = attempt.Id.ToString("D");
        if (attempt.Id == Guid.Empty || attempt.Amount <= 0 ||
            attempt.Amount != decimal.Truncate(attempt.Amount) ||
            !IsProviderId(session.SessionId, "cs_test_") ||
            !string.Equals(session.AttemptReference, reference, StringComparison.Ordinal) ||
            !string.Equals(session.ClientReference, reference, StringComparison.Ordinal) ||
            session.AmountVnd != attempt.Amount ||
            (attempt.CheckoutSessionId is not null &&
             !string.Equals(attempt.CheckoutSessionId, session.SessionId, StringComparison.Ordinal)) ||
            (attempt.PaymentIntentId is not null &&
             !string.Equals(attempt.PaymentIntentId, session.PaymentIntentId, StringComparison.Ordinal)))
            return StripeReconciliationOutcome.IntegrityMismatch;

        if (session.PaymentIntentId is null)
        {
            if (intent is not null || session.PaymentStatus == "paid")
                return StripeReconciliationOutcome.IntegrityMismatch;
            return session.SessionStatus == "expired" && session.PaymentStatus == "unpaid" &&
                   session.RecoveryEnabled == false
                ? StripeReconciliationOutcome.CannotSucceed
                : StripeReconciliationOutcome.Pending;
        }

        if (intent is null)
            return StripeReconciliationOutcome.Pending;

        if (!IsProviderId(intent.PaymentIntentId, "pi_") ||
            !string.Equals(session.PaymentIntentId, intent.PaymentIntentId, StringComparison.Ordinal) ||
            !string.Equals(intent.AttemptReference, reference, StringComparison.Ordinal) ||
            intent.AmountVnd != attempt.Amount || intent.AmountReceived < 0 ||
            intent.AmountReceived > intent.AmountVnd)
            return StripeReconciliationOutcome.IntegrityMismatch;

        if (session.SessionStatus == "complete" && session.PaymentStatus == "paid")
        {
            return intent.Status == "succeeded" && intent.AmountReceived == intent.AmountVnd
                ? StripeReconciliationOutcome.ConfirmedPaid
                : StripeReconciliationOutcome.IntegrityMismatch;
        }

        if (session.PaymentStatus == "unpaid" && intent.Status == "canceled" &&
            intent.AmountReceived == 0 && session.RecoveryEnabled == false &&
            (session.SessionStatus == "complete" || session.SessionStatus == "expired"))
            return StripeReconciliationOutcome.CannotSucceed;

        return StripeReconciliationOutcome.Pending;
    }

    internal static bool IsProviderId(string? value, string prefix) =>
        value is not null && value.StartsWith(prefix, StringComparison.Ordinal) &&
        value.Length > prefix.Length && value.Length <= 255 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
