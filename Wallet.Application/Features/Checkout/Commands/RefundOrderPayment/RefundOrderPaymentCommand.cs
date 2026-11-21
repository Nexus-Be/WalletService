namespace Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Withdrawals;

public sealed record RefundOrderPaymentCommand(
    [Required(ErrorMessage = "User ID is required.")]
    [NonEmptyGuid(ErrorMessage = "User ID must be a valid UUID.")]
    string UserId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    [MoneyPrecision]
    decimal Amount,
    [MaxLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    string? Reason = null,
    Guid? ParentOrderId = null) : IRequest<WalletOrderTransactionResponse>, IRequestValidation
{
    // Order ID or sub-order ID, depending on the route that was called.
    [JsonIgnore] public Guid OrderId { get; init; }

    [JsonIgnore] public bool IsSubOrder { get; init; }

    [JsonIgnore] public string? IdempotencyKey { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (error is not null)
        {
            yield return error;
        }

        if (ParentOrderId.HasValue && ParentOrderId.Value == Guid.Empty)
        {
            yield return new ValidationResult("Parent order ID must be a valid UUID.", new[] { nameof(ParentOrderId) });
        }

        if (!string.IsNullOrWhiteSpace(IdempotencyKey) &&
            !WithdrawalWorkflow.IsValidIdempotencyKey(IdempotencyKey.Trim()))
        {
            yield return new ValidationResult(
                "Idempotency-Key header must be 1 to 100 ASCII letters, digits, hyphens, or underscores.",
                new[] { nameof(IdempotencyKey) });
        }
    }
}
