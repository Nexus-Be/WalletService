namespace Wallet.Application.Features.Topups.Commands.TopupStripe;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record TopupStripeCommand(
    [Range(10000, 99999999, ErrorMessage = "Topup amount must be between 10,000 and 99,999,999 VND.")]
    decimal Amount) : IRequest<TopupStripeResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }
    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userError is not null)
            yield return userError;
        if (Amount != decimal.Truncate(Amount))
            yield return new ValidationResult("Topup amount must be a whole VND value.", [nameof(Amount)]);
        if (string.IsNullOrEmpty(IdempotencyKey) || IdempotencyKey.Length > 128 ||
            IdempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            yield return new ValidationResult("Idempotency key is invalid.", [nameof(IdempotencyKey)]);
    }
}

public sealed record TopupStripeResponse(
    Guid TransactionId, string PaymentUrl, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);
