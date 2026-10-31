namespace Wallet.Application.Common.Exceptions;

using global::Wallet.Domain.Exceptions;

public sealed class RequestValidationException : DomainException
{
    public RequestValidationException(string message)
        : base("VAL_INVALID_INPUT", message, 400)
    {
    }
}
