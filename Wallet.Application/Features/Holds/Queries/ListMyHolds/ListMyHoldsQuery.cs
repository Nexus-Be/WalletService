namespace Wallet.Application.Features.Holds.Queries.ListMyHolds;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Enums;

public sealed record ListMyHoldsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 10,
    string? Status = null) : IRequest<PagedData<HoldListItem>>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (UserId == Guid.Empty || Page < 1 || PageSize is < 1 or > 100 ||
            (long)(Page - 1) * PageSize > int.MaxValue ||
            (!string.IsNullOrWhiteSpace(Status) && !Status.TryToEnum<AuctionHoldStatus>(out _)))
            yield return new ValidationResult("The hold list request is invalid.");
    }
}

public sealed record HoldListItem(Guid HoldId, string ReferenceType, string ReferenceId, string? AuctionId,
    decimal Amount, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt = null);
