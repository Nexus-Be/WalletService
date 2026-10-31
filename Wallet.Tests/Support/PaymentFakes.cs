namespace Wallet.Tests.Support;

using Wallet.Application.Interfaces;

public sealed class FakeVnPayService : IVnPayService
{
    public bool SignatureIsValid { get; set; } = true;

    public string CreatePaymentUrl(
        string transactionReference,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null)
        => $"https://sandbox.vnpay.vn/pay?ref={transactionReference}&amount={amount}";

    public bool ValidateSignature(IReadOnlyDictionary<string, string> parameters) => SignatureIsValid;
}

public sealed class FakeDirectPaymentGateway : IDirectPaymentGateway
{
    public bool IsUnavailable { get; set; }

    public string CreatePaymentUrl(
        string gatewayCode,
        Guid orderId,
        decimal amount,
        string currency,
        string? bankCode = null,
        string? returnUrl = null)
    {
        if (IsUnavailable)
        {
            throw new InvalidOperationException("Gateway is unavailable.");
        }

        return $"https://gateway.test/{gatewayCode}/{orderId}?amount={amount}&currency={currency}";
    }
}
