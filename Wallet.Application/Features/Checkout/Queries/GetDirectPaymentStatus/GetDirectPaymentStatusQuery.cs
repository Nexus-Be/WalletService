namespace Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record GetDirectPaymentStatusQuery(
    [Required(ErrorMessage = "Order ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Order ID must be a valid UUID.")]
    string OrderId) : IRequest<DirectPaymentStatusResponse>, IRequestValidation
{
    [JsonIgnore]
    public string UserId { get; init; } = string.Empty;

    /// <summary>True for admin/staff callers, who may read any user's payment.</summary>
    [JsonIgnore]
    public bool AllowAnyUser { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (AllowAnyUser)
        {
            yield break;
        }

        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }
    }
}

public sealed record DirectPaymentStatusResponse(
    string TransactionId,
    string OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? GatewayTransactionNo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
