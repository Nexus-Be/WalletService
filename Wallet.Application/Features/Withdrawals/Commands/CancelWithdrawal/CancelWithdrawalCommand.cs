namespace Wallet.Application.Features.Withdrawals.Commands.CancelWithdrawal;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record CancelWithdrawalCommand(
    [NonEmptyGuid] Guid WithdrawalId) : IRequest<WithdrawalSummaryResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var withdrawalError = RequestValidation.ValidateGuid(WithdrawalId, nameof(WithdrawalId), "Withdrawal ID");
        if (withdrawalError is not null)
        {
            yield return withdrawalError;
        }

        var userError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userError is not null)
        {
            yield return userError;
        }
    }
}
