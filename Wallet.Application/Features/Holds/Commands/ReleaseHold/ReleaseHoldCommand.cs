namespace Wallet.Application.Features.Holds.Commands.ReleaseHold;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Holds.Commands.CreateHold;

public sealed record ReleaseHoldCommand(string Reason) : IRequest<ReleaseHoldResponse>, IRequestValidation
{
    [JsonIgnore] public Guid HoldId { get; init; }
    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (HoldId == Guid.Empty || !CreateHoldCommand.ValidKey(IdempotencyKey) ||
            Reason is not ("AUCTION_ENDED_NOT_WINNER" or "AUCTION_CANCELLED" or "AUCTION_FAILED" or "ADMIN_FORCE_CANCEL"))
            yield return new ValidationResult("The auction hold release request is invalid.");
    }
}

public sealed record ReleaseHoldResponse(Guid HoldId, string Status, DateTimeOffset ReleasedAt,
    decimal AvailableBalance);
