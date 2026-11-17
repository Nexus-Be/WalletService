namespace Wallet.Application.Features.Holds.Queries.GetHold;

using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record GetHoldQuery(Guid? HoldId, string? ReferenceType, string? ReferenceId)
    : IRequest<HoldStatusResponse>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var byId = HoldId.HasValue && HoldId.Value != Guid.Empty && ReferenceType is null && ReferenceId is null;
        var byReference = HoldId is null && ReferenceType == "AUCTION_DEPOSIT" &&
            !string.IsNullOrWhiteSpace(ReferenceId) && ReferenceId.Length <= 128;
        if (!byId && !byReference) yield return new ValidationResult("The hold lookup is invalid.");
    }
}

public sealed record HoldStatusResponse(Guid HoldId, Guid OwnerUserId, string ReferenceType, string ReferenceId,
    decimal Amount, string Status, string? CapturePurpose, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt);
