namespace Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;

public sealed record SettleSellerOrderCommand(
    [Required(ErrorMessage = "Seller ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Seller ID must be a valid UUID.")]
    string SellerId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    [MoneyPrecision]
    decimal Amount,
    Guid? ParentOrderId = null) : IRequest<WalletOrderTransactionResponse>, IRequestValidation
{
    [JsonIgnore] public Guid SubOrderId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(SubOrderId, nameof(SubOrderId), "Sub-order ID");
        if (error is not null)
        {
            yield return error;
        }

        if (ParentOrderId.HasValue && ParentOrderId.Value == Guid.Empty)
        {
            yield return new ValidationResult("Parent order ID must be a valid UUID.", new[] { nameof(ParentOrderId) });
        }
    }
}
