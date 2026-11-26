namespace Wallet.Infrastructure.Payments;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Exceptions;

public sealed class StripeCheckoutClient : IStripeCheckoutClient
{
    /// <summary>Universal checkout time to live for every payment type (Stripe's minimum allowed is 30 minutes).</summary>
    private const int SessionExpiryMinutes = 30;

    private readonly SessionService _sessions;
    private readonly PaymentIntentService _paymentIntents;
    private readonly string _successUrl;
    private readonly string _cancelUrl;

    public StripeCheckoutClient(HttpClient httpClient, IConfiguration configuration)
    {
        var key = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A Stripe test-mode secret key is required.");
        }

        var allowLocalHttp = StripeSettings.IsDevelopment(configuration);
        _successUrl = StripeSettings.RequireReturnUrl(configuration["Stripe:SuccessUrl"], "Stripe success URL", allowLocalHttp);
        _cancelUrl = StripeSettings.RequireReturnUrl(configuration["Stripe:CancelUrl"], "Stripe cancel URL", allowLocalHttp);
        var stripeHttp = new SystemNetHttpClient(httpClient, maxNetworkRetries: 0);
        // Test hook for an isolated QA stack: point the SDK at a fake Stripe server. Accepted only in Development, never with a live key
        // (a sk_test_ key is already required above), so a production or shared environment cannot be redirected by configuration.
        var apiBase = configuration["Stripe:ApiBase"];
        StripeClient client;
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            client = new StripeClient(key, httpClient: stripeHttp);
        }
        else
        {
            if (!allowLocalHttp)
                throw new InvalidOperationException("Stripe:ApiBase is only allowed in the Development environment.");
            client = new StripeClient(new StripeClientOptions { ApiKey = key, ApiBase = apiBase.TrimEnd('/'), HttpClient = stripeHttp });
        }
        _sessions = new SessionService(client);
        _paymentIntents = new PaymentIntentService(client);
    }

    public async Task<StripeCheckoutSession> CreateSessionAsync(
        StripeCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AttemptId == Guid.Empty)
            throw new ArgumentException("Payment attempt ID must not be empty.", nameof(request));
        if (request.AmountVnd <= 0 || request.AmountVnd > StripeAmountLimits.MaximumVnd)
            throw new ArgumentOutOfRangeException(nameof(request), "Amount is outside the Stripe payment range.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 255 ||
            request.IdempotencyKey.Any(char.IsWhiteSpace))
            throw new ArgumentException("Provider idempotency key is invalid.", nameof(request));

        var expiresAt = request.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(SessionExpiryMinutes);
        var metadata = new Dictionary<string, string> { ["attempt_id"] = request.AttemptId.ToString() };
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = AddReturnContext(_successUrl, request.TopupId, request.OrderId),
            CancelUrl = AddReturnContext(_cancelUrl, request.TopupId, request.OrderId),
            ExpiresAt = expiresAt.UtcDateTime,
            ClientReferenceId = request.AttemptId.ToString(),
            Metadata = metadata,
            AfterExpiration = new SessionAfterExpirationOptions
            {
                Recovery = new SessionAfterExpirationRecoveryOptions { Enabled = false }
            },
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "vnd",
                        UnitAmount = request.AmountVnd,
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = "Wallet payment" }
                    }
                }
            ]
        };
        var session = await RequestProviderAsync(() => _sessions.CreateAsync(
            options, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, cancellationToken), cancellationToken);
        var reference = request.AttemptId.ToString();
        if (string.IsNullOrWhiteSpace(session.Id) ||
            !session.Id.StartsWith("cs_test_", StringComparison.Ordinal) ||
            session.Id.Length <= "cs_test_".Length || session.Id.Length > 255 ||
            session.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
            session.Livemode || session.Mode != "payment" || session.Currency != "vnd" ||
            session.AmountTotal != request.AmountVnd || session.ClientReferenceId != reference ||
            session.Metadata is null || !session.Metadata.TryGetValue("attempt_id", out var attemptReference) ||
            attemptReference != reference || GetRecoveryEnabled(session.StripeResponse.Content) != false ||
            !Uri.TryCreate(session.Url, UriKind.Absolute, out var paymentUrl) ||
            paymentUrl.Scheme != Uri.UriSchemeHttps || session.ExpiresAt == default)
            throw new InvalidOperationException("Stripe returned an invalid checkout session.");

        return new StripeCheckoutSession(session.Id, session.Url, new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero));
    }

    public async Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        ValidateProviderId(sessionId, "cs_test_", nameof(sessionId));
        var session = await RequestProviderAsync(
            () => _sessions.GetAsync(sessionId, cancellationToken: cancellationToken), cancellationToken);
        return MapSessionSnapshot(sessionId, session);
    }

    public async Task<StripeSessionSnapshot> ExpireSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        ValidateProviderId(sessionId, "cs_test_", nameof(sessionId));
        var session = await RequestProviderAsync(
            () => _sessions.ExpireAsync(sessionId, cancellationToken: cancellationToken), cancellationToken);
        return MapSessionSnapshot(sessionId, session);
    }

    private static StripeSessionSnapshot MapSessionSnapshot(string sessionId, Session session)
    {
        if (session.Id != sessionId || session.Livemode || session.Mode != "payment" ||
            session.Currency != "vnd" || session.AmountTotal is null or <= 0 ||
            string.IsNullOrWhiteSpace(session.Status) || string.IsNullOrWhiteSpace(session.PaymentStatus))
            throw new InvalidOperationException("Stripe returned an invalid checkout session.");

        return new StripeSessionSnapshot(
            session.Id, session.Status, session.PaymentStatus, session.AmountTotal.Value,
            session.PaymentIntentId, session.ClientReferenceId,
            session.Metadata is not null && session.Metadata.TryGetValue("attempt_id", out var reference)
                ? reference : null,
            GetRecoveryEnabled(session.StripeResponse.Content));
    }

    public async Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(
        string paymentIntentId, CancellationToken cancellationToken)
    {
        ValidateProviderId(paymentIntentId, "pi_", nameof(paymentIntentId));
        var intent = await RequestProviderAsync(
            () => _paymentIntents.GetAsync(paymentIntentId, cancellationToken: cancellationToken), cancellationToken);
        if (intent.Id != paymentIntentId || intent.Livemode || intent.Currency != "vnd" ||
            intent.Amount <= 0 || intent.AmountReceived < 0 || string.IsNullOrWhiteSpace(intent.Status))
            throw new InvalidOperationException("Stripe returned an invalid payment intent.");

        return new StripePaymentIntentSnapshot(
            intent.Id, intent.Status, intent.Amount, intent.AmountReceived,
            intent.Metadata is not null && intent.Metadata.TryGetValue("attempt_id", out var reference)
                ? reference : null);
    }

    private static async Task<T> RequestProviderAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken)
    {
        try
        {
            return await request();
        }
        catch (StripeException exception)
        {
            if (exception.HttpStatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.PaymentRequired
                or HttpStatusCode.NotFound
                or HttpStatusCode.UnprocessableEntity)
            {
                throw new BusinessRuleValidationException(
                    "STRIPE_REQUEST_REJECTED",
                    "The payment provider rejected the payment request.",
                    400);
            }

            throw new PaymentProviderUnavailableException(exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PaymentProviderUnavailableException(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentProviderUnavailableException(exception);
        }
    }

    private static void ValidateProviderId(string? value, string prefix, string parameter)
    {
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            value.Length <= prefix.Length || value.Length > 255 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Stripe provider ID is invalid.", parameter);
    }

    private static bool? GetRecoveryEnabled(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("after_expiration", out var afterExpiration))
            return null;
        if (afterExpiration.ValueKind == JsonValueKind.Null)
            return false;
        if (afterExpiration.ValueKind != JsonValueKind.Object ||
            !afterExpiration.TryGetProperty("recovery", out var recovery))
            return null;
        if (recovery.ValueKind == JsonValueKind.Null)
            return false;
        if (recovery.ValueKind != JsonValueKind.Object ||
            !recovery.TryGetProperty("enabled", out var enabled))
            return null;
        return enabled.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string AddReturnContext(string returnUrl, Guid? topupId, Guid? orderId)
    {
        if (topupId is not null)
        {
            if (topupId == Guid.Empty) throw new ArgumentException("Topup ID must not be empty.", nameof(topupId));
            return ReplaceReturnQueryParams(
                returnUrl,
                ("kind", "topup"),
                ("transactionId", topupId.Value.ToString("D")));
        }

        if (orderId is not null)
        {
            if (orderId == Guid.Empty) throw new ArgumentException("Order ID must not be empty.", nameof(orderId));
            return ReplaceReturnQueryParams(
                returnUrl,
                ("kind", "direct"),
                ("orderId", orderId.Value.ToString("D")));
        }

        return returnUrl;
    }

    private static string ReplaceReturnQueryParams(string returnUrl, params (string Key, string Value)[] replacements)
    {
        var keys = replacements.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uri = new UriBuilder(returnUrl);
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
            {
                var name = Uri.UnescapeDataString(parameter.Split('=', 2)[0].Replace('+', ' '));
                return !keys.Contains(name);
            }).ToList();
        foreach (var (key, value) in replacements)
        {
            parameters.Add($"{key}={value}");
        }

        uri.Query = string.Join("&", parameters);
        return uri.Uri.AbsoluteUri;
    }
}
