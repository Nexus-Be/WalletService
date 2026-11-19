namespace Wallet.Application.Features.AdminWallets.Queries.ListAdminWallets;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;

public sealed record ListAdminWalletsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    string? Status = null,
    Guid? UserId = null,
    string? Search = null) : IRequest<PagedData<WalletResponse>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (UserId.HasValue && UserId.Value == Guid.Empty)
        {
            yield return new ValidationResult("User ID must be a valid UUID.", new[] { nameof(UserId) });
        }

        if (!string.IsNullOrWhiteSpace(Status))
        {
            var normalized = Status.Trim().ToUpperInvariant();
            if (normalized is not ("ACTIVE" or "FROZEN"))
            {
                yield return new ValidationResult(
                    "Wallet status filter must be 'Active' or 'Frozen'.",
                    new[] { nameof(Status) });
            }
        }
    }
}
