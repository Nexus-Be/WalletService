namespace Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Withdrawals;

public sealed record CancelOrderEscrowCommand(
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be between 3 and 500 characters.")]
    string Reason,
    string? ExpectedBuyerId = null,
    decimal? ExpectedAmount = null) : IRequest<CancelOrderEscrowResponse>, IRequestValidation
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

        if (!WithdrawalWorkflow.IsValidIdempotencyKey(IdempotencyKey))
        {
            yield return new ValidationResult(
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.",
                new[] { nameof(IdempotencyKey) });
        }

        if (ExpectedBuyerId is not null)
        {
            var buyerError = RequestValidation.ValidateGuid(
                ExpectedBuyerId,
                nameof(ExpectedBuyerId),
                "Expected buyer ID");
            if (buyerError is not null)
            {
                yield return buyerError;
            }
        }

        if (ExpectedAmount is decimal expectedAmount)
        {
            if (expectedAmount <= 0m)
            {
                yield return new ValidationResult(
                    "Expected amount must be greater than zero.",
                    new[] { nameof(ExpectedAmount) });
            }
            else if (expectedAmount > 9999999999999999.99m ||
                     expectedAmount != decimal.Round(expectedAmount, 2))
            {
                yield return new ValidationResult(
                    "Expected amount must fit the supported decimal(18,2) range.",
                    new[] { nameof(ExpectedAmount) });
            }
        }
    }
}

public sealed record CancelOrderEscrowResponse(
    string TransactionId,
    string OrderId,
    string WalletId,
    string UserId,
    string Type,
    decimal ReturnedAmount,
    string Currency,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string EscrowStatus,
    string Status,
    string Note,
    DateTimeOffset CancelledAt,
    bool IsReplay);
