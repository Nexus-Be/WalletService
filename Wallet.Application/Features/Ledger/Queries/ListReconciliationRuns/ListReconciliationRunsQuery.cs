namespace Wallet.Application.Features.Ledger.Queries.ListReconciliationRuns;

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

public sealed record ListReconciliationRunsQuery(
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")] int Page = 1,
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")] int PageSize = 20) : IRequest<PagedData<ReconciliationRunResult>>;

public sealed record ReconciliationRunResult(
    Guid Id, DateTimeOffset CreatedAt, string Status, int FindingCount, DateTimeOffset? FromDate, DateTimeOffset? ToDate,
    IReadOnlyList<string> Invariants, IReadOnlyList<string> UnavailableSources, string? SkippedNote);

public sealed record ReconciliationFindingResult(
    Guid Id, Guid RunId, string InvariantCode, string Code, string Severity, string Status, string? ReferenceId, string? Source,
    string? ExpectedValue, string? ActualValue, string? Description, string? Note, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public static class ReconciliationMapping
{
    public static ReconciliationRunResult Map(ReconciliationRun run) => new(
        run.Id, run.CreatedAt, run.Status.ToString(), run.FindingCount, run.FromDate, run.ToDate,
        (run.Invariants ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
        (run.UnavailableSources ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries), run.SkippedNote);

    public static ReconciliationFindingResult Map(ReconciliationFinding finding) => new(
        finding.Id, finding.RunId, finding.InvariantCode, finding.Code, finding.Severity, finding.Status.ToString(), finding.ReferenceId, finding.Source,
        finding.ExpectedValue, finding.ActualValue, finding.Description, finding.Note, finding.CreatedAt, finding.UpdatedAt);
}
