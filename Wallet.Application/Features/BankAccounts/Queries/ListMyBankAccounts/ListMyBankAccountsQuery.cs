namespace Wallet.Application.Features.BankAccounts.Queries.ListMyBankAccounts;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record ListMyBankAccountsQuery : IRequest<IReadOnlyList<BankAccountResponse>>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null)
        {
            yield return error;
        }
    }
}
