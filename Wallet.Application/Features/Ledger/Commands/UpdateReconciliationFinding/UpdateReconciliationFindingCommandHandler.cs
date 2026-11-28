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

using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Ledger.Queries.ListReconciliationRuns;

internal sealed class UpdateReconciliationFindingCommandHandler : IRequestHandler<UpdateReconciliationFindingCommand, ReconciliationFindingResult>
{
    private readonly IWalletDbContext _context;

    public UpdateReconciliationFindingCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<ReconciliationFindingResult> Handle(UpdateReconciliationFindingCommand request, CancellationToken cancellationToken)
    {
        var finding = await _context.ReconciliationFindings.FirstOrDefaultAsync(item => item.Id == request.FindingId, cancellationToken)
            ?? throw new DomainException("RECONCILIATION_FINDING_NOT_FOUND", "Reconciliation finding was not found.", 404);

        if (!request.Status.TryToEnum<ReconciliationFindingStatus>(out var next))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_INPUT",
                "Status must be Open, Investigating, Resolved or Accepted.");
        }

        var before = finding.Status;
        finding.Status = next;
        finding.Note = request.Note.Trim();
        finding.UpdatedAt = DateTimeOffset.UtcNow;
        finding.UpdatedBy = request.AdminUserId;

        _context.AddAuditLog(
            AuditAction.ReconciliationFindingUpdate, nameof(ReconciliationFinding), finding.Id.ToString(),
            oldValues: new { Status = before }, newValues: new { Status = next, finding.Note },
            userId: request.AdminUserId.ToString());
        await _context.SaveChangesAsync(cancellationToken);
        return ReconciliationMapping.Map(finding);
    }
}
