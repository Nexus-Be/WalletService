namespace Wallet.Application.Features.Topups.Queries.ListTopups;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Enums;

public sealed record ListTopupsQuery(
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 10,
    string? Status = null)
    : IRequest<PagedData<TopupListItem>>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null) yield return error;

        if (!string.IsNullOrWhiteSpace(Status) && !Status.TryToEnum<TopupStatus>(out _))
        {
            yield return new ValidationResult(
                "Topup status filter is invalid.",
                new[] { nameof(Status) });
        }
    }
}

public sealed record TopupListItem(
    Guid TransactionId, decimal Amount, string Currency, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    bool CanResume, DateTimeOffset? SessionExpiresAt);
