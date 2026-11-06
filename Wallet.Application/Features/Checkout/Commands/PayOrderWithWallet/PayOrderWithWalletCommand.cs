namespace Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record PayOrderWithWalletCommand(
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    [MoneyPrecision]
    decimal Amount) : IRequest<WalletOrderTransactionResponse>, IRequestValidation
{
    [JsonIgnore] public Guid OrderId { get; init; }

    [JsonIgnore] public string UserId { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var orderIdError = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (orderIdError is not null)
        {
            yield return orderIdError;
        }

        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }
    }
}

public sealed record WalletOrderTransactionResponse(
    string Id,
    string WalletId,
    string? UserId,
    string Type,
    decimal Amount,
    string? Currency,
    decimal BalanceAfter,
    string Status,
    string? Note,
    DateTimeOffset CreatedAt);
