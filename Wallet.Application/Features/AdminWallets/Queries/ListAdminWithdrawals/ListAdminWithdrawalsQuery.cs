namespace Wallet.Application.Features.AdminWallets.Queries.ListAdminWithdrawals;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Withdrawals;
using Wallet.Domain.Enums;

public sealed record ListAdminWithdrawalsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    string? Status = null,
    Guid? UserId = null,
    string? TransferReference = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<PagedData<AdminWithdrawalResponse>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (!string.IsNullOrWhiteSpace(Status) && !Status.TryToEnum<WithdrawalStatus>(out _))
        {
            yield return new ValidationResult(
                "Withdrawal status filter is invalid.",
                new[] { nameof(Status) });
        }

        if (UserId == Guid.Empty)
        {
            yield return new ValidationResult(
                "User ID must be a valid UUID.",
                new[] { nameof(UserId) });
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value > ToDate.Value)
        {
            yield return new ValidationResult(
                "From date must be earlier than or equal to to date.",
                new[] { nameof(FromDate), nameof(ToDate) });
        }
    }
}
