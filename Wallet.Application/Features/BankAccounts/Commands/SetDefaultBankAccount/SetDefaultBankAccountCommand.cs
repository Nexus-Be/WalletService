namespace Wallet.Application.Features.BankAccounts.Commands.SetDefaultBankAccount;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record SetDefaultBankAccountCommand(
    [NonEmptyGuid] Guid BankAccountId) : IRequest<BankAccountResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var idError = RequestValidation.ValidateGuid(BankAccountId, nameof(BankAccountId), "Bank account ID");
        if (idError is not null)
        {
            yield return idError;
        }

        var userError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userError is not null)
        {
            yield return userError;
        }
    }
}
