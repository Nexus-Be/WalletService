namespace Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;

public sealed record RefundOrderPaymentCommand(
    [Required(ErrorMessage = "User ID is required.")]
    [NonEmptyGuid(ErrorMessage = "User ID must be a valid UUID.")]
    string UserId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    [MoneyPrecision]
    decimal Amount,
    [MaxLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    string? Reason = null) : IRequest<WalletOrderTransactionResponse>, IRequestValidation
{
    // Order ID or sub-order ID, depending on the route that was called.
    [JsonIgnore] public Guid OrderId { get; init; }

    [JsonIgnore] public bool IsSubOrder { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (error is not null)
        {
            yield return error;
        }
    }
}
