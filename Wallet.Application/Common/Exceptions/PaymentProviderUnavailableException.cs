namespace Wallet.Application.Common.Exceptions;

public sealed class PaymentProviderUnavailableException : Exception
{
    public PaymentProviderUnavailableException(Exception innerException)
        : base("The payment provider is unavailable. Please try again later.", innerException)
    {
    }
}
