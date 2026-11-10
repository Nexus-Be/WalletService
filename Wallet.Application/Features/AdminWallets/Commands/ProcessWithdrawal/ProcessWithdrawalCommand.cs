namespace Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record ProcessWithdrawalCommand(
    [Required(ErrorMessage = "Action is required.")]
    [RegularExpression("^(?i:Approve|Reject)$", ErrorMessage = "Action must be either 'Approve' or 'Reject'.")]
    string Action,
    string? AdminNote = null,
    string? ReferenceCode = null) : IRequest<ProcessWithdrawalResponse>, IRequestValidation
{
    [JsonIgnore] public Guid WithdrawalId { get; init; }

    [JsonIgnore] public string AdminUserId { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var withdrawalIdError =
            RequestValidation.ValidateGuid(WithdrawalId, nameof(WithdrawalId), "Withdrawal ID");
        if (withdrawalIdError is not null)
        {
            yield return withdrawalIdError;
        }

        var adminUserIdError = RequestValidation.ValidateGuid(AdminUserId, nameof(AdminUserId), "Admin user ID");
        if (adminUserIdError is not null)
        {
            yield return adminUserIdError;
        }

        if (ReferenceCode?.Length > 100)
        {
            yield return new ValidationResult(
                "Reference code must not exceed 100 characters.",
                new[] { nameof(ReferenceCode) });
        }

        if (string.Equals(Action, "Approve", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(ReferenceCode))
        {
            yield return new ValidationResult(
                "Reference code is required when approving a withdrawal.",
                new[] { nameof(ReferenceCode) });
        }
    }
}

public sealed record ProcessWithdrawalResponse(
    string WithdrawalId,
    string Status,
    DateTimeOffset ProcessedAt,
    string? TransactionId,
    string? ReferenceCode);