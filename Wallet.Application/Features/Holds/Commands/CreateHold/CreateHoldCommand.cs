namespace Wallet.Application.Features.Holds.Commands.CreateHold;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record CreateHoldCommand(Guid OwnerUserId, string ReferenceType, string ReferenceId,
    decimal Amount, string Currency, JsonElement? Metadata) : IRequest<CreateHoldResult>, IRequestValidation
{
    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (OwnerUserId == Guid.Empty || ReferenceType != "AUCTION_DEPOSIT" ||
            string.IsNullOrWhiteSpace(ReferenceId) || ReferenceId.Length > 128 ||
            Amount <= 0 || Amount != decimal.Truncate(Amount) || Amount > 9999999999999999m ||
            !string.Equals(Currency, "VND", StringComparison.OrdinalIgnoreCase) ||
            !ValidKey(IdempotencyKey) ||
            (Metadata is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) }))
            yield return new ValidationResult("The auction hold request is invalid.");
    }

    internal static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 128 &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.');
}

public sealed record CreateHoldResponse(Guid HoldId, string Status, decimal Amount,
    decimal Balance, decimal HeldBalance, decimal AvailableBalance, DateTimeOffset CreatedAt);
public sealed record CreateHoldResult(CreateHoldResponse Hold, bool Created);
