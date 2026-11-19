namespace Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Withdrawals;

public sealed record ProcessWithdrawalCommand(
    [Required(ErrorMessage = "Action is required.")]
    [RegularExpression("^(?i:Approve|Reject|StartProcessing|Complete|Fail|ResolveFailed)$",
        ErrorMessage = "Action must be one of: Approve, Reject, StartProcessing, Complete, Fail, ResolveFailed.")]
    string Action,
    [StringLength(WithdrawalWorkflow.MaxReasonLength, ErrorMessage = "Admin note must be 500 characters or fewer.")]
    string? AdminNote = null,
    bool TransferConfirmed = false,
    [StringLength(WithdrawalWorkflow.MaxReferenceLength, ErrorMessage = "Transfer reference must be 100 characters or fewer.")]
    string? TransferReference = null,
    bool BankVerifiedNotTransferred = false,
    [StringLength(WithdrawalWorkflow.MaxReasonLength, ErrorMessage = "Verification evidence must be 500 characters or fewer.")]
    string? VerificationEvidence = null) : IRequest<ProcessWithdrawalResponse>, IRequestValidation
{
    [JsonIgnore] public Guid WithdrawalId { get; init; }

    [JsonIgnore] public string AdminUserId { get; init; } = string.Empty;

    [JsonIgnore] public bool ActorIsAdmin { get; init; }

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

        if (!WithdrawalWorkflow.TryParse(Action, out var action))
        {
            yield break;
        }

        if (action == WithdrawalAction.Complete)
        {
            if (!TransferConfirmed)
            {
                yield return new ValidationResult(
                    "Transfer confirmation is required when completing a withdrawal.",
                    new[] { nameof(TransferConfirmed) });
            }

            if (string.IsNullOrWhiteSpace(TransferReference))
            {
                yield return new ValidationResult(
                    "Transfer reference is required when completing a withdrawal.",
                    new[] { nameof(TransferReference) });
            }
        }
        else if (action == WithdrawalAction.Fail)
        {
            var reason = AdminNote?.Trim() ?? string.Empty;
            if (reason.Length < WithdrawalWorkflow.MinReasonLength || reason.Length > WithdrawalWorkflow.MaxReasonLength)
            {
                yield return new ValidationResult(
                    "Admin note between 3 and 500 characters is required when failing a withdrawal.",
                    new[] { nameof(AdminNote) });
            }
        }
        else if (action == WithdrawalAction.ResolveFailed)
        {
            if (!BankVerifiedNotTransferred)
            {
                yield return new ValidationResult(
                    "Bank verification confirmation is required to resolve a processing withdrawal as failed.",
                    new[] { nameof(BankVerifiedNotTransferred) });
            }

            var evidence = VerificationEvidence?.Trim() ?? string.Empty;
            if (evidence.Length < WithdrawalWorkflow.MinReasonLength || evidence.Length > WithdrawalWorkflow.MaxReasonLength)
            {
                yield return new ValidationResult(
                    "Verification evidence between 3 and 500 characters is required.",
                    new[] { nameof(VerificationEvidence) });
            }

            var reason = AdminNote?.Trim() ?? string.Empty;
            if (reason.Length < WithdrawalWorkflow.MinReasonLength || reason.Length > WithdrawalWorkflow.MaxReasonLength)
            {
                yield return new ValidationResult(
                    "Admin note between 3 and 500 characters is required when resolving a failed withdrawal.",
                    new[] { nameof(AdminNote) });
            }
        }
    }
}

public sealed record ProcessWithdrawalResponse(
    string WithdrawalId,
    string Status,
    DateTimeOffset ProcessedAt,
    string? TransactionId,
    string? TransferReference,
    string? StatusReason);
