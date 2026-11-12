namespace Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record AdjustWalletBalanceCommand(
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    [MoneyPrecision]
    decimal Amount,
    [Required(ErrorMessage = "Adjustment type is required.")]
    [RegularExpression("^(?i:Credit|Debit)$", ErrorMessage = "Adjustment type must be either 'Credit' or 'Debit'.")]
    string AdjustmentType = "Credit",
    [Required(ErrorMessage = "Reason is required.")]
    string Reason = "") : IRequest<BalanceAdjustmentResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    [JsonIgnore] public string AdminUserId { get; init; } = string.Empty;

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
    }
}

public sealed record BalanceAdjustmentResponse(
    string AdjustmentId,
    string WalletId,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset CreatedAt);