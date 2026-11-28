namespace Wallet.Application.Features.Ledger.Queries.GetReconciliationFinding;

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

internal sealed class GetReconciliationFindingQueryHandler : IRequestHandler<GetReconciliationFindingQuery, ReconciliationFindingResult>
{
    private readonly IWalletDbContext _context;

    public GetReconciliationFindingQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<ReconciliationFindingResult> Handle(GetReconciliationFindingQuery request, CancellationToken cancellationToken)
    {
        var finding = await _context.ReconciliationFindings.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new DomainException("RECONCILIATION_FINDING_NOT_FOUND", "Reconciliation finding was not found.", 404);
        return ReconciliationMapping.Map(finding);
    }
}
