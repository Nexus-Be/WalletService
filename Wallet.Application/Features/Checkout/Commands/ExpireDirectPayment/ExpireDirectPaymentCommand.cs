namespace Wallet.Application.Features.Checkout.Commands.ExpireDirectPayment;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;

public sealed record ExpireDirectPaymentCommand(
    [NonEmptyGuid] Guid OrderId) : IRequest<DirectPaymentLifecycleResponse>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var orderError = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (orderError is not null)
        {
            yield return orderError;
        }
    }
}
