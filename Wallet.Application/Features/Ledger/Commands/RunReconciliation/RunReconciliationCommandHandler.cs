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

using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Ledger.Queries.ListReconciliationRuns;

internal sealed class RunReconciliationCommandHandler : IRequestHandler<RunReconciliationCommand, ReconciliationRunResult>
{
    private readonly IWalletDbContext _context;
    private readonly IReconciliationSourceClient _sources;

    public RunReconciliationCommandHandler(IWalletDbContext context, IReconciliationSourceClient sources)
    {
        _context = context;
        _sources = sources;
    }

    public async Task<ReconciliationRunResult> Handle(RunReconciliationCommand request, CancellationToken cancellationToken)
    {
        var evaluator = new ReconciliationEvaluator(_context, _sources);
        var run = await evaluator.RunAsync(
            new ReconciliationRequest(request.FromDate, request.ToDate, request.Invariants), request.AdminUserId, cancellationToken);

        _context.AddAuditLog(
            AuditAction.ReconciliationRun, nameof(ReconciliationRun), run.Id.ToString(),
            newValues: new { run.Status, run.FindingCount, run.SkippedNote, run.UnavailableSources },
            userId: request.AdminUserId.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        return ReconciliationMapping.Map(run);
    }
}
