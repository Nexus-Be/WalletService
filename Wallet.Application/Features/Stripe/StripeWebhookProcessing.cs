namespace Wallet.Application.Features.Stripe;

using System.Text.Json;
using Wallet.Application.Interfaces;

internal enum StripeWebhookProcessingOutcome
{
    Ignored,
    Settled,
    AlreadySettled,
    DuplicateEvent,
    FinalizedFailure,
    RetryRequired,
    ReconciliationRequired
}

internal static class StripeWebhookProcessing
{
    internal static async Task<StripeWebhookProcessingOutcome> ProcessAsync(
        IWalletDbContext context,
        IStripeWebhookVerifier verifier,
        IStripeCheckoutClient provider,
        ReadOnlyMemory<byte> payload,
        string signature,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(provider);

        var verified = verifier.Verify(payload, signature);
        if (verified.EventType is not ("checkout.session.completed" or
            "checkout.session.async_payment_succeeded" or "checkout.session.async_payment_failed" or
            "checkout.session.expired"))
            return StripeWebhookProcessingOutcome.Ignored;

        using var document = JsonDocument.Parse(verified.RawJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("object", out var sessionObject) || sessionObject.ValueKind != JsonValueKind.Object ||
            !TryGetString(sessionObject, "object", out var objectType) || objectType != "checkout.session" ||
            !TryGetString(sessionObject, "id", out var sessionId) ||
            !sessionObject.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
            !TryGetString(metadata, "attempt_id", out var reference) ||
            !Guid.TryParseExact(reference, "D", out var attemptId) || attemptId == Guid.Empty)
            throw new InvalidOperationException("Stripe webhook payment correlation is invalid.");

        if (!StripeReconciliationPolicy.IsProviderId(sessionId, "cs_test_"))
            throw new InvalidOperationException("Stripe webhook checkout session ID is invalid.");

        var session = await provider.GetSessionAsync(sessionId, cancellationToken);
        if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Stripe returned a mismatched checkout session.");
        if (session.PaymentIntentId is not null &&
            !StripeReconciliationPolicy.IsProviderId(session.PaymentIntentId, "pi_"))
            throw new InvalidOperationException("Stripe returned an invalid payment intent ID.");
        var intent = session.PaymentIntentId is null
            ? null
            : await provider.GetPaymentIntentAsync(session.PaymentIntentId, cancellationToken);

        var result = await StripeSettlement.ApplyAsync(
            context, attemptId, verified.EventId, verified.EventType, session, intent, cancellationToken);
        return result switch
        {
            StripeSettlementOutcome.Settled => StripeWebhookProcessingOutcome.Settled,
            StripeSettlementOutcome.AlreadySettled => StripeWebhookProcessingOutcome.AlreadySettled,
            StripeSettlementOutcome.DuplicateEvent => StripeWebhookProcessingOutcome.DuplicateEvent,
            StripeSettlementOutcome.FinalizedFailure => StripeWebhookProcessingOutcome.FinalizedFailure,
            StripeSettlementOutcome.Pending => StripeWebhookProcessingOutcome.RetryRequired,
            StripeSettlementOutcome.ReconciliationRequired => StripeWebhookProcessingOutcome.ReconciliationRequired,
            _ => throw new InvalidOperationException("Stripe settlement returned an invalid outcome.")
        };
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String)
            return false;
        value = item.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
