namespace Wallet.Application.Interfaces;

public interface IVnPayService
{
    string CreatePaymentUrl(
        string transactionReference,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null);

    string CreateDirectPaymentUrl(
        string transactionReference,
        Guid orderId,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null);

    bool ValidateSignature(IReadOnlyDictionary<string, string> parameters);
}
