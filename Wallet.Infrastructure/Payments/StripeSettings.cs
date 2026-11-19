namespace Wallet.Infrastructure.Payments;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public static class StripeSettings
{
    public static bool IsDevelopment(IConfiguration configuration) =>
        string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"],
            "Development",
            StringComparison.OrdinalIgnoreCase);

    public static string RequireReturnUrl(string? value, string name, bool allowLocalHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttps &&
             !(allowLocalHttp && url.Scheme == Uri.UriSchemeHttp &&
               string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
               url.Port == 3000 && string.IsNullOrEmpty(url.UserInfo))))
        {
            throw new InvalidOperationException(
                $"{name} must be an absolute HTTPS URL, or http://localhost:3000 in Development.");
        }

        return url.AbsoluteUri;
    }

    public static bool Validate(IConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var secretKey = configuration["Stripe:SecretKey"];
        var webhookSecret = configuration["Stripe:WebhookSecret"];
        var successUrl = configuration["Stripe:SuccessUrl"];
        var cancelUrl = configuration["Stripe:CancelUrl"];

        var hasSecretKey = !IsBlankOrPlaceholder(secretKey);
        var hasWebhookSecret = !IsBlankOrPlaceholder(webhookSecret);
        var hasSuccessUrl = !IsBlankOrPlaceholder(successUrl);
        var hasCancelUrl = !IsBlankOrPlaceholder(cancelUrl);

        if (!hasSecretKey && !hasWebhookSecret)
        {
            logger?.LogWarning("Stripe is not configured; Stripe payment endpoints will reject requests until configured.");
            return false;
        }

        if (!hasSecretKey || !secretKey!.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A Stripe test-mode secret key is required.");
        }

        if (!hasWebhookSecret)
        {
            throw new InvalidOperationException("Stripe:WebhookSecret is required when Stripe is configured.");
        }

        var allowLocalHttp = IsDevelopment(configuration);
        if (!hasSuccessUrl || !hasCancelUrl)
        {
            throw new InvalidOperationException("Stripe:SuccessUrl and Stripe:CancelUrl are required when Stripe is configured.");
        }

        RequireReturnUrl(successUrl, "Stripe success URL", allowLocalHttp);
        RequireReturnUrl(cancelUrl, "Stripe cancel URL", allowLocalHttp);
        return true;
    }

    public static void EnsureValid(IConfiguration configuration, ILogger logger) =>
        Validate(configuration, logger);

    private static bool IsBlankOrPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENVIRONMENT", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase);
}
