namespace Wallet.Application.Features.Subsidies.Commands.CloseSubsidyBudget;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;

/// <summary>Stops a budget from taking new reservations. Reservations already held can still be used or released.</summary>
public sealed record CloseSubsidyBudgetCommand : IRequest<SubsidyBudgetResult>, IRequestValidation
{
    [JsonIgnore] public Guid BudgetId { get; init; }
    [JsonIgnore] public Guid AdminUserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(BudgetId, nameof(BudgetId), "Budget ID");
        if (error is not null) yield return error;
    }
}
