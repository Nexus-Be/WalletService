namespace Wallet.Application.Common.Exceptions;

using System.Collections.Generic;
using global::Wallet.Domain.Exceptions;

public sealed class RequestValidationException : DomainException
{
    public RequestValidationException(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base("VAL_INVALID_INPUT", message, 400)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}