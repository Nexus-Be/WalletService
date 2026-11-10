namespace Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;

public sealed record GetWalletReconciliationQuery(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    Guid? WalletId = null) : IRequest<PagedData<WalletReconciliationResult>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (WalletId == Guid.Empty)
        {
            yield return new ValidationResult("Wallet ID must be a non-empty UUID.", new[] { nameof(WalletId) });
        }
    }
}

public sealed record WalletReconciliationResult(
    string WalletId,
    string Currency,
    decimal CurrentBalance,
    decimal? LatestSuccessfulBalance,
    decimal? Difference,
    int SuccessfulTransactionCount,
    DateTimeOffset? LatestTransactionAt,
    string Result);
