namespace Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Withdrawals;

public sealed record AdjustOrderPaymentCommand(
    [Required(ErrorMessage = "Buyer ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Buyer ID must be a valid UUID.")]
    string BuyerId,
    [MoneyPrecision]
    decimal DeltaAmount,
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be between 3 and 500 characters.")]
    string Reason) : IRequest<WalletOrderTransactionResponse>, IRequestValidation
{
    [JsonIgnore] public Guid OrderId { get; init; }

    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var orderError = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (orderError is not null)
        {
            yield return orderError;
        }

        if (DeltaAmount == 0m)
        {
            yield return new ValidationResult(
                "Delta amount must be non-zero.",
                new[] { nameof(DeltaAmount) });
        }

        if (!WithdrawalWorkflow.IsValidIdempotencyKey(IdempotencyKey))
        {
            yield return new ValidationResult(
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.",
                new[] { nameof(IdempotencyKey) });
        }
    }
}
