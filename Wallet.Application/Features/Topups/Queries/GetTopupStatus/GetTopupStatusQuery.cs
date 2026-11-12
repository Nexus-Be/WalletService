namespace Wallet.Application.Features.Topups.Queries.GetTopupStatus;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record GetTopupStatusQuery(
    [NonEmptyGuid(ErrorMessage = "Topup ID must be a valid UUID.")] Guid TopupId)
    : IRequest<TopupStatusResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null)
            yield return error;
    }
}

public sealed record TopupStatusResponse(
    Guid TransactionId, decimal Amount, string Currency, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
