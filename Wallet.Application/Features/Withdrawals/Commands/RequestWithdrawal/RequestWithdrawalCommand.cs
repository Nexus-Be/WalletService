namespace Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record RequestWithdrawalCommand(
    [Range(50000, 500000000, ErrorMessage = "Withdrawal amount must be between 50,000 and 500,000,000 VND.")]
    [MoneyPrecision]
    decimal Amount,
    [Required(ErrorMessage = "Currency is required.")]
    [RegularExpression("^(?i:VND)$", ErrorMessage = "Currency must be VND.")]
    string? Currency = "VND",
    [StringLength(150, ErrorMessage = "Bank code cannot exceed 150 characters.")]
    string? BankCode = null,
    [StringLength(100, ErrorMessage = "Account number cannot exceed 100 characters.")]
    string? AccountNumber = null,
    [StringLength(255, ErrorMessage = "Account holder name cannot exceed 255 characters.")]
    string? AccountHolderName = null,
    Guid? BankAccountId = null) : IRequest<WithdrawalResponse>, IRequestValidation
{
    [JsonIgnore] public string UserId { get; init; } = string.Empty;

    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }

        if (!WithdrawalWorkflow.IsValidIdempotencyKey(IdempotencyKey))
        {
            yield return new ValidationResult(
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.",
                new[] { nameof(IdempotencyKey) });
        }

        if (BankAccountId.HasValue && BankAccountId.Value == Guid.Empty)
        {
            yield return new ValidationResult(
                "Bank account ID must be a valid UUID.",
                new[] { nameof(BankAccountId) });
        }

        if (BankAccountId is null)
        {
            var hasAnyManual = !string.IsNullOrWhiteSpace(BankCode)
                || !string.IsNullOrWhiteSpace(AccountNumber)
                || !string.IsNullOrWhiteSpace(AccountHolderName);

            if (hasAnyManual)
            {
                if (string.IsNullOrWhiteSpace(BankCode))
                {
                    yield return new ValidationResult(
                        "Bank code is required.",
                        new[] { nameof(BankCode) });
                }

                if (string.IsNullOrWhiteSpace(AccountNumber))
                {
                    yield return new ValidationResult(
                        "Account number is required.",
                        new[] { nameof(AccountNumber) });
                }

                if (string.IsNullOrWhiteSpace(AccountHolderName))
                {
                    yield return new ValidationResult(
                        "Account holder name is required.",
                        new[] { nameof(AccountHolderName) });
                }
            }
        }
    }
}

public sealed record WithdrawalResponse(
    string WithdrawalId,
    decimal Amount,
    decimal Fee,
    string Status,
    DateTimeOffset RequestedAt)
{
    [JsonIgnore] public bool IsReplay { get; init; }
}
