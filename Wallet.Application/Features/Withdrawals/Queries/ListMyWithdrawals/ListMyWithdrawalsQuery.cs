namespace Wallet.Application.Features.Withdrawals.Queries.ListMyWithdrawals;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Enums;

public sealed record ListMyWithdrawalsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    string? Status = null) : IRequest<PagedData<WithdrawalSummaryResponse>>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userError is not null)
        {
            yield return userError;
        }

        if (!string.IsNullOrWhiteSpace(Status) && !Status.TryToEnum<WithdrawalStatus>(out _))
        {
            yield return new ValidationResult(
                "Withdrawal status filter is invalid.",
                new[] { nameof(Status) });
        }
    }
}
