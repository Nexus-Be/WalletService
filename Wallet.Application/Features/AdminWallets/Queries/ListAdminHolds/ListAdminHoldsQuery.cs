namespace Wallet.Application.Features.AdminWallets.Queries.ListAdminHolds;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Enums;

public sealed record ListAdminHoldsQuery(
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20,
    Guid? UserId = null,
    string? Status = null) : IRequest<PagedData<AdminHoldListItem>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (UserId.HasValue && UserId.Value == Guid.Empty)
        {
            yield return new ValidationResult("User ID must be a valid UUID.", new[] { nameof(UserId) });
        }

        if (!string.IsNullOrWhiteSpace(Status) && !Status.TryToEnum<AuctionHoldStatus>(out _))
        {
            yield return new ValidationResult("Hold status filter is invalid.", new[] { nameof(Status) });
        }
    }
}

public sealed record AdminHoldListItem(
    Guid HoldId,
    Guid WalletId,
    Guid OwnerUserId,
    string ReferenceType,
    string ReferenceId,
    string? AuctionId,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);
