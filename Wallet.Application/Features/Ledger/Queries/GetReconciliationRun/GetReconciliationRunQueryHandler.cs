namespace Wallet.Application.Features.Ledger.Queries.GetReconciliationRun;

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

internal sealed class GetReconciliationRunQueryHandler : IRequestHandler<GetReconciliationRunQuery, ReconciliationRunResult>
{
    private readonly IWalletDbContext _context;

    public GetReconciliationRunQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<ReconciliationRunResult> Handle(GetReconciliationRunQuery request, CancellationToken cancellationToken)
    {
        var run = await _context.ReconciliationRuns.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
            ?? throw new DomainException("RECONCILIATION_RUN_NOT_FOUND", "Reconciliation run was not found.", 404);
        return ReconciliationMapping.Map(run);
    }
}
