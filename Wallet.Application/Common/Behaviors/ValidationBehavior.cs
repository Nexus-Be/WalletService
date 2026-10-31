namespace Wallet.Application.Common.Behaviors;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Common.Validation;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var results = new List<ValidationResult>();

        if (!RequestValidation.TryValidate(request, results))
        {
            var message = results.FirstOrDefault()?.ErrorMessage
                          ?? "One or more validation errors occurred.";
            throw new RequestValidationException(message);
        }

        return await next();
    }
}
