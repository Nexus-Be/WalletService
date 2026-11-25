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

internal sealed class ListReconciliationRunsQueryHandler : IRequestHandler<ListReconciliationRunsQuery, PagedData<ReconciliationRunResult>>
{
    private readonly IWalletDbContext _context;

    public ListReconciliationRunsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<ReconciliationRunResult>> Handle(ListReconciliationRunsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = _context.ReconciliationRuns.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(run => run.CreatedAt).ThenByDescending(run => run.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedData<ReconciliationRunResult>(rows.Select(ReconciliationMapping.Map).ToList(), PaginationMetadata.Create(page, pageSize, total));
    }
}
