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
            var errors = results
                .SelectMany(result =>
                {
                    var members = result.MemberNames.DefaultIfEmpty("request");
                    return members.Select(member => new
                    {
                        Member = member,
                        Message = result.ErrorMessage ?? "The request is invalid."
                    });
                })
                .GroupBy(error => error.Member, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).Distinct().ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var message = string.Join(" ", errors.Values.SelectMany(messages => messages).Distinct());
            throw new RequestValidationException(message, errors);
        }

        return await next();
    }
}