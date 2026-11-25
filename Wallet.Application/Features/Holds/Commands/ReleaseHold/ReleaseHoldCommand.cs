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
        if (HoldId == Guid.Empty || !CreateHoldCommand.ValidKey(IdempotencyKey) || !IsValidReason(Reason))
            yield return new ValidationResult("The auction hold release request is invalid.");
    }

    internal static bool IsValidReason(string? reason) =>
        !string.IsNullOrEmpty(reason) &&
        reason.Length <= 128 &&
        char.IsAsciiLetterUpper(reason[0]) &&
        reason.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}

public sealed record ReleaseHoldResponse(Guid HoldId, string Status, DateTimeOffset ReleasedAt,
    decimal AvailableBalance);
