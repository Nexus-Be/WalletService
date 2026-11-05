namespace Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record CheckoutDirectCommand(
    [Required(ErrorMessage = "Order ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Order ID must be a valid UUID.")]
    string OrderId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    [MoneyPrecision]
    decimal Amount,
    string? BankCode = null,
    [Required(ErrorMessage = "Return URL is required.")]
    string ReturnUrl = "") : IRequest<DirectPaymentResponse>, IRequestValidation
{
    [JsonIgnore] public string UserId { get; init; } = string.Empty;

    [JsonIgnore] public string ClientIp { get; init; } = "127.0.0.1";

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }

        if (Amount > 0 && Amount != decimal.Truncate(Amount))
        {
            yield return new ValidationResult(
                "Amount must be a positive whole-VND value.",
                new[] { nameof(Amount) });
        }

        if (!Uri.TryCreate(ReturnUrl, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme != Uri.UriSchemeHttps)
        {
            yield return new ValidationResult(
                "VNPay return URL must be an absolute HTTPS URL.",
                new[] { nameof(ReturnUrl) });
        }
    }
}

public sealed record DirectPaymentResponse(
    string TransactionId,
    string OrderId,
    decimal Amount,
    string PaymentUrl,
    string Status,
    DateTimeOffset CreatedAt);