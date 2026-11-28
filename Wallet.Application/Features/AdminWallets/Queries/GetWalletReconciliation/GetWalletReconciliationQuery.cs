namespace Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;

public sealed record GetWalletReconciliationQuery(
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    Guid? WalletId = null,
    Guid? UserId = null,
    string? Result = null)
    : IRequest<PagedData<WalletReconciliationResult>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (WalletId == Guid.Empty)
        {
            yield return new ValidationResult("Wallet ID must be a valid UUID.", new[] { nameof(WalletId) });
        }

        if (UserId == Guid.Empty)
        {
            yield return new ValidationResult("User ID must be a valid UUID.", new[] { nameof(UserId) });
        }

        if (!string.IsNullOrWhiteSpace(Result) &&
            !string.Equals(Result.Trim(), "Balanced", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Result.Trim(), "Discrepancy", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Result.Trim(), "InsufficientHistory", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "Reconciliation result filter must be 'Balanced', 'Discrepancy', or 'InsufficientHistory'.",
                new[] { nameof(Result) });
        }
    }
}

public sealed record WalletReconciliationResult(
    Guid WalletId,
    string Currency,
    decimal CurrentBalance,
    decimal? LatestSuccessfulBalance,
    decimal? Difference,
    int SuccessfulTransactionCount,
    DateTimeOffset? LatestTransactionAt,
    string Result,
    Guid UserId = default,
    decimal LockedBalance = 0m,
    decimal ExpectedLockedBalance = 0m,
    decimal LockedDifference = 0m);
