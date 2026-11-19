namespace Wallet.Application.Features.Checkout.Commands.ResumeDirectPayment;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record ResumeDirectPaymentCommand(
    [NonEmptyGuid] Guid OrderId) : IRequest<ResumeDirectPaymentResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var orderError = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (orderError is not null)
        {
            yield return orderError;
        }

        var userError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userError is not null)
        {
            yield return userError;
        }
    }
}

public sealed record ResumeDirectPaymentResponse(
    Guid OrderId,
    string PaymentUrl,
    DateTimeOffset SessionExpiresAt);
