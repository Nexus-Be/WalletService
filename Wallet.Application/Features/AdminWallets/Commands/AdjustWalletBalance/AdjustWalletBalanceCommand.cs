namespace Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Withdrawals;

public sealed record AdjustWalletBalanceCommand(
    [Range(typeof(decimal), "0.01", "500000000", ErrorMessage = "Amount must be between 0.01 and 500,000,000.")]
    [MoneyPrecision]
    decimal Amount,
    [Required(ErrorMessage = "Adjustment type is required.")]
    [RegularExpression("^(?i:Credit|Debit)$", ErrorMessage = "Adjustment type must be either 'Credit' or 'Debit'.")]
    string AdjustmentType = "Credit",
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, ErrorMessage = "Reason must not exceed 500 characters.")]
    string Reason = "") : IRequest<BalanceAdjustmentResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    [JsonIgnore] public string AdminUserId { get; init; } = string.Empty;

    [JsonIgnore] public string? IdempotencyKey { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }

        var adminUserIdError = RequestValidation.ValidateGuid(AdminUserId, nameof(AdminUserId), "Admin user ID");
        if (adminUserIdError is not null)
        {
            yield return adminUserIdError;
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

public sealed record BalanceAdjustmentResponse(
    string AdjustmentId,
    string WalletId,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset CreatedAt,
    string AdjustmentType = "",
    string Currency = "",
    decimal AvailableBalance = 0m);