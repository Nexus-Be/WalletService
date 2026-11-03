namespace Wallet.Tests.Support;

using Wallet.Application.Interfaces;

public sealed class FakeVnPayService : IVnPayService
{
    public bool SignatureIsValid { get; set; } = true;
    public string? LastDirectTransactionReference { get; private set; }
    public int DirectPaymentUrlCalls { get; private set; }

    public string CreatePaymentUrl(
        string transactionReference,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null)
        => $"https://sandbox.vnpay.vn/pay?ref={transactionReference}&amount={amount}";

    public string CreateDirectPaymentUrl(
        string transactionReference,
        Guid orderId,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null)
    {
        LastDirectTransactionReference = transactionReference;
        DirectPaymentUrlCalls++;
        return $"https://sandbox.vnpay.vn/pay?ref={transactionReference}&orderId={orderId:D}&amount={amount}";
    }

    public bool ValidateSignature(IReadOnlyDictionary<string, string> parameters) => SignatureIsValid;
}