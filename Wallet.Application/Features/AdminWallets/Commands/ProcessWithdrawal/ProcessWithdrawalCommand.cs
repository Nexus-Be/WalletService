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
    string? AdminNote = null) : IRequest<ProcessWithdrawalResponse>, IRequestValidation
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
    }
}

public sealed record ProcessWithdrawalResponse(
    string WithdrawalId,
    string Status,
    DateTimeOffset ProcessedAt,
    string? TransactionId);