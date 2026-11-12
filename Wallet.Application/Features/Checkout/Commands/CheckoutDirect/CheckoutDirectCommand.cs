namespace Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record CheckoutDirectCommand(
    [Required(ErrorMessage = "Order ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Order ID must be a valid UUID.")]
    string OrderId,
    [Range(1, 99999999, ErrorMessage = "Amount must be between 1 and 99,999,999 VND.")]
    decimal Amount) : IRequest<DirectPaymentResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null)
            yield return error;
        if (Amount != decimal.Truncate(Amount))
            yield return new ValidationResult("Amount must be a whole VND value.", [nameof(Amount)]);
    }
}

public sealed record DirectPaymentResponse(
    string TransactionId, string OrderId, decimal Amount, string PaymentUrl, string Status, DateTimeOffset CreatedAt);
