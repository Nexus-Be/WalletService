namespace Wallet.Application.Features.Ledger.Commands.UpdateReconciliationFinding;

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

public sealed record UpdateReconciliationFindingCommand(
    [Required(ErrorMessage = "Status is required.")] string Status,
    [Required(ErrorMessage = "A note is required."), MaxLength(2000)] string Note) : IRequest<ReconciliationFindingResult>, IRequestValidation
{
    [JsonIgnore] public Guid FindingId { get; init; }
    [JsonIgnore] public Guid AdminUserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (!Enum.TryParse<ReconciliationFindingStatus>(Status, true, out _))
            yield return new ValidationResult("Status must be Open, Investigating, Resolved or Accepted.", new[] { nameof(Status) });
        if (string.IsNullOrWhiteSpace(Note))
            yield return new ValidationResult("A note is required.", new[] { nameof(Note) });
    }
}
