namespace Wallet.Infrastructure.Payments;

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wallet.Application.Interfaces;

public sealed class VnPayService : IVnPayService
{
    private readonly IConfiguration _configuration;

    public VnPayService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreatePaymentUrl(
        string transactionReference,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null)
    {
        var effectiveReturnUrl = returnUrl ?? GetRequired("ReturnUrl");
        if (!Uri.TryCreate(effectiveReturnUrl, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("VNPay return URL must use HTTPS.");

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = GetRequired("Version"),
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = GetRequired("TmnCode"),
            ["vnp_Amount"] = checked(amount * 100m).ToString("0", CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = transactionReference,
            ["vnp_OrderInfo"] = $"Wallet topup {transactionReference}",
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn",
            ["vnp_ReturnUrl"] = effectiveReturnUrl,
            ["vnp_IpAddr"] = ipAddress,
            ["vnp_CreateDate"] = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(bankCode))
            parameters["vnp_BankCode"] = bankCode;

        var signedData = BuildQuery(parameters);
        var signature = ComputeSignature(signedData, GetRequired("HashSecret"));
        var paymentUrl = GetRequired("PaymentUrl");
        var separator = paymentUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{paymentUrl}{separator}{signedData}&vnp_SecureHashType=HmacSHA512&vnp_SecureHash={signature}";
    }

    public bool ValidateSignature(IReadOnlyDictionary<string, string> parameters)
    {
        var suppliedSignature = GetParameter(parameters, "vnp_SecureHash");
        if (string.IsNullOrWhiteSpace(suppliedSignature))
            return false;

        var signedParameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (!parameter.Key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase) &&
                !parameter.Key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            {
                signedParameters[parameter.Key] = parameter.Value;
            }
        }

        var expectedSignature = ComputeSignature(BuildQuery(signedParameters), GetRequired("HashSecret"));
        try
        {
            var expectedBytes = Convert.FromHexString(expectedSignature);
            var suppliedBytes = Convert.FromHexString(suppliedSignature);
            return expectedBytes.Length == suppliedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string GetRequired(string key)
    {
        var envKeyUpper = $"VNPAY_{key.ToUpperInvariant()}";
        var envKeySnake = $"VNPAY_{System.Text.RegularExpressions.Regex.Replace(key, "(\\B[A-Z])", "_$1").ToUpperInvariant()}";
        var value = _configuration[$"VnPay:{key}"]
            ?? _configuration[envKeyUpper]
            ?? _configuration[envKeySnake]
            ?? Environment.GetEnvironmentVariable(envKeyUpper)
            ?? Environment.GetEnvironmentVariable(envKeySnake);

        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal))
            throw new InvalidOperationException($"VNPay configuration value 'VnPay:{key}' or environment variable '{envKeySnake}' is missing.");

        return value;
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join("&", parameters.Select(parameter =>
            $"{WebUtility.UrlEncode(parameter.Key)}={WebUtility.UrlEncode(parameter.Value)}"));
    }

    private static string ComputeSignature(string data, string secret)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    private static string? GetParameter(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value)
            ? value
            : parameters.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }
}