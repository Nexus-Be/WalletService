namespace Wallet.Application.Features.Ledger.Queries.ListReconciliationFindings;

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

internal sealed class ListReconciliationFindingsQueryHandler : IRequestHandler<ListReconciliationFindingsQuery, PagedData<ReconciliationFindingResult>>
{
    private readonly IWalletDbContext _context;

    public ListReconciliationFindingsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<ReconciliationFindingResult>> Handle(ListReconciliationFindingsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = _context.ReconciliationFindings.AsNoTracking().AsQueryable();
        if (Enum.TryParse<ReconciliationFindingStatus>(request.Status, true, out var status))
            query = query.Where(finding => finding.Status == status);
        if (!string.IsNullOrWhiteSpace(request.InvariantCode))
        {
            var code = request.InvariantCode.Trim().ToUpperInvariant();
            query = query.Where(finding => finding.InvariantCode == code);
        }
        if (!string.IsNullOrWhiteSpace(request.Severity))
        {
            var severity = request.Severity.Trim();
            query = query.Where(finding => finding.Severity == severity);
        }
        if (request.FromDate.HasValue) query = query.Where(finding => finding.CreatedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue) query = query.Where(finding => finding.CreatedAt <= request.ToDate.Value);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(finding => finding.CreatedAt).ThenBy(finding => finding.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedData<ReconciliationFindingResult>(rows.Select(ReconciliationMapping.Map).ToList(), PaginationMetadata.Create(page, pageSize, total));
    }
}
