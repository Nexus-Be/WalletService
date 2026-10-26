namespace Wallet.Infrastructure.Payments;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wallet.Application.Common.Interfaces;

public sealed class ConfiguredDirectPaymentGateway : IDirectPaymentGateway
{
    private readonly IConfiguration _configuration;

    public ConfiguredDirectPaymentGateway(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreatePaymentUrl(string gatewayCode, Guid orderId, decimal amount, string currency)
    {
        var configurationPath = $"PaymentGateways:{gatewayCode}";
        var baseUrl = _configuration[$"{configurationPath}:PaymentUrl"];
        var secret = _configuration[$"{configurationPath}:SigningSecret"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException($"Payment gateway '{gatewayCode}' is not configured.");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Payment gateway '{gatewayCode}' must use an HTTPS payment URL.");

        var amountText = amount.ToString("0.00", CultureInfo.InvariantCulture);
        var canonicalData = $"{orderId:D}|{amountText}|{currency}";
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalData))).ToLowerInvariant();

        var builder = new UriBuilder(uri);
        var query = string.IsNullOrEmpty(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = $"{query}orderId={Uri.EscapeDataString(orderId.ToString("D"))}" +
                        $"&amount={Uri.EscapeDataString(amountText)}" +
                        $"&currency={Uri.EscapeDataString(currency)}" +
                        $"&signature={signature}";
        return builder.Uri.ToString();
    }
}