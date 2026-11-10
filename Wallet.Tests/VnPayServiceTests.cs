namespace Wallet.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wallet.Infrastructure.Payments;
using Xunit;

public sealed class VnPayServiceTests
{
    private const string MerchantCode = "TESTMERCHANT";
    private const string HashSecret = "test-hash-secret";

    [Fact]
    public void ValidateSignature_ValidHmacAndConfiguredMerchant_ReturnsTrue()
    {
        var service = CreateService();
        var parameters = CreateSignedParameters();

        Assert.True(service.ValidateSignature(parameters));
    }

    [Fact]
    public void ValidateSignature_TamperedAmount_ReturnsFalse()
    {
        var service = CreateService();
        var parameters = CreateSignedParameters();
        parameters["vnp_Amount"] = "5000001";

        Assert.False(service.ValidateSignature(parameters));
    }

    [Fact]
    public void ValidateSignature_WrongMerchant_ReturnsFalse()
    {
        var service = CreateService();
        var parameters = CreateSignedParameters();
        parameters["vnp_TmnCode"] = "OTHER-MERCHANT";
        parameters["vnp_SecureHash"] = Sign(parameters);

        Assert.False(service.ValidateSignature(parameters));
    }

    [Fact]
    public void ValidateSignature_MissingHashType_ReturnsTrue()
    {
        var service = CreateService();
        var parameters = CreateSignedParameters();
        parameters.Remove("vnp_SecureHashType");

        Assert.True(service.ValidateSignature(parameters));
    }

    [Fact]
    public void ValidateSignature_UnsupportedHashType_ReturnsFalse()
    {
        var service = CreateService();
        var parameters = CreateSignedParameters();
        parameters["vnp_SecureHashType"] = "HmacSHA256";

        Assert.False(service.ValidateSignature(parameters));
    }

    [Fact]
    public void CreateDirectPaymentUrl_ContainsOrderReferenceAndValidSignature()
    {
        var service = CreateService();
        var orderId = Guid.NewGuid();

        var paymentUrl = service.CreateDirectPaymentUrl(
            "DIRECT-ORDER-1",
            orderId,
            850_000m,
            "203.0.113.10",
            "NCB",
            "https://shop.example/return");

        var uri = new Uri(paymentUrl);
        var parameters = uri.Query.TrimStart('?')
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => WebUtility.UrlDecode(pair[0]),
                pair => WebUtility.UrlDecode(pair[1]),
                StringComparer.Ordinal);

        Assert.Equal("DIRECT-ORDER-1", parameters["vnp_TxnRef"]);
        Assert.Equal(orderId.ToString("D"), parameters["vnp_OrderInfo"].Replace("Direct order checkout ", ""));
        Assert.Equal("85000000", parameters["vnp_Amount"]);
        Assert.Equal("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html", uri.GetLeftPart(UriPartial.Path));
        Assert.True(service.ValidateSignature(parameters));
    }

    private static VnPayService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VnPay:TmnCode"] = MerchantCode,
                ["VnPay:HashSecret"] = HashSecret,
                ["VnPay:Version"] = "2.1.0",
                ["VnPay:PaymentUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
            })
            .Build();

        return new VnPayService(configuration);
    }

    private static Dictionary<string, string> CreateSignedParameters()
    {
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = MerchantCode,
            ["vnp_TxnRef"] = "TOPUP-1",
            ["vnp_Amount"] = "5000000",
            ["vnp_CurrCode"] = "VND",
            ["vnp_ResponseCode"] = "00",
            ["vnp_SecureHashType"] = "HmacSHA512"
        };
        parameters["vnp_SecureHash"] = Sign(parameters);
        return parameters;
    }

    private static string Sign(IReadOnlyDictionary<string, string> parameters)
    {
        var signedData = string.Join("&", parameters
            .Where(parameter =>
                !parameter.Key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase) &&
                !parameter.Key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter =>
                $"{WebUtility.UrlEncode(parameter.Key)}={WebUtility.UrlEncode(parameter.Value)}"));
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HashSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedData))).ToLowerInvariant();
    }
}