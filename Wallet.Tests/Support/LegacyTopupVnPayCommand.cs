namespace Wallet.Application.Features.Topups.Commands.TopupVnPay;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record TopupVnPayCommand(
    [Range(10000, 100000000, ErrorMessage = "Deposit amount must be between 10,000 and 100,000,000 VND.")]
    [MoneyPrecision]
    decimal Amount,
    [Required(ErrorMessage = "Currency is required.")]
    [RegularExpression("^(?i:VND)$", ErrorMessage = "Currency must be VND.")]
    string? Currency = "VND",
    [Required(ErrorMessage = "Payment method is required.")]
    [RegularExpression("^(?i:VNPAY)$", ErrorMessage = "Payment method must be VNPay.")]
    string? PaymentMethod = "VNPAY",
    string? BankCode = null,
    string? ReturnUrl = null) : IRequest<TopupResponse>, IRequestValidation
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
    }
}

public sealed record TopupResponse(
    string TransactionId,
    string PaymentUrl,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);