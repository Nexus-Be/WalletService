namespace Wallet.Application.Features.Ledger.Commands.RunReconciliation;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

using Wallet.Application.Features.Ledger.Queries.ListReconciliationRuns;

public sealed record RunReconciliationCommand(DateTimeOffset? FromDate = null, DateTimeOffset? ToDate = null, IReadOnlyList<string>? Invariants = null)
    : IRequest<ReconciliationRunResult>, IRequestValidation
{
    [JsonIgnore] public Guid AdminUserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (FromDate.HasValue && ToDate.HasValue && ToDate.Value < FromDate.Value)
            yield return new ValidationResult("To date must not be before from date.", new[] { nameof(ToDate) });
        var unknown = (Invariants ?? Array.Empty<string>()).Where(code => !ReconciliationEvaluator.AllInvariants.Contains(code?.Trim().ToUpperInvariant())).ToList();
        if (unknown.Count > 0)
            yield return new ValidationResult($"Unknown invariant: {string.Join(", ", unknown)}.", new[] { nameof(Invariants) });
    }
}
