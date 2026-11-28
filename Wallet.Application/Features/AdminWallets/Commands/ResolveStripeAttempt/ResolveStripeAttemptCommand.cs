namespace Wallet.Application.Features.AdminWallets.Commands.ResolveStripeAttempt;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record ResolveStripeAttemptCommand(
    [Required(ErrorMessage = "Resolution is required.")]
    [RegularExpression("^(?i:Failed|Expired)$", ErrorMessage = "Resolution must be either 'Failed' or 'Expired'.")]
    string Resolution,
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be between 3 and 500 characters.")]
    string Reason) : IRequest<ResolveStripeAttemptResponse>, IRequestValidation
{
    [JsonIgnore] public Guid AttemptId { get; init; }

    [JsonIgnore] public string AdminUserId { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var attemptError = RequestValidation.ValidateGuid(AttemptId, nameof(AttemptId), "Attempt ID");
        if (attemptError is not null)
        {
            yield return attemptError;
        }

        var adminError = RequestValidation.ValidateGuid(AdminUserId, nameof(AdminUserId), "Admin user ID");
        if (adminError is not null)
        {
            yield return adminError;
        }
    }
}

public sealed record ResolveStripeAttemptResponse(
    Guid AttemptId,
    string Status,
    bool BlocksReplacement,
    string Reason,
    DateTimeOffset ResolvedAt);
