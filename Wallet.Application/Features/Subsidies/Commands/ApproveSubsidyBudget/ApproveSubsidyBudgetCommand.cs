namespace Wallet.Application.Features.Subsidies.Commands.ApproveSubsidyBudget;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;

/// <summary>Approves a pending budget. The approver must be a different admin than the creator, and only one budget can be active.</summary>
public sealed record ApproveSubsidyBudgetCommand : IRequest<SubsidyBudgetResult>, IRequestValidation
{
    [JsonIgnore] public Guid BudgetId { get; init; }
    [JsonIgnore] public Guid AdminUserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(BudgetId, nameof(BudgetId), "Budget ID");
        if (error is not null) yield return error;
    }
}
