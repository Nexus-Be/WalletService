namespace Wallet.Application.Common.Interfaces;

public interface IVnPayService
{
    string CreatePaymentUrl(string transactionReference, decimal amount, string ipAddress);

    bool ValidateSignature(IReadOnlyDictionary<string, string> parameters);
}