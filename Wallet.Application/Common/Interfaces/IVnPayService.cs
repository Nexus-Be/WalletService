namespace Wallet.Application.Common.Interfaces;

public interface IVnPayService
{
    string CreatePaymentUrl(
        string transactionReference,
        decimal amount,
        string ipAddress,
        string? bankCode = null,
        string? returnUrl = null);

    bool ValidateSignature(IReadOnlyDictionary<string, string> parameters);
}