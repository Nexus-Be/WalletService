namespace Wallet.Application.Features.Ledger.Queries.ListJournal;

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

internal sealed class ListJournalQueryHandler : IRequestHandler<ListJournalQuery, PagedData<JournalLineResult>>
{
    private readonly IWalletDbContext _context;

    public ListJournalQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<JournalLineResult>> Handle(ListJournalQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var entries = _context.JournalEntries.AsNoTracking().AsQueryable();
        if (request.TransactionId.HasValue)
            entries = entries.Where(entry => entry.WalletTransactionId == request.TransactionId.Value);
        if (request.WalletId.HasValue)
            entries = entries.Where(entry => entry.WalletId == request.WalletId.Value);

        var query = from entry in entries
                    join line in _context.JournalLines.AsNoTracking() on entry.Id equals line.EntryId
                    select new { entry, line };
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.entry.CreatedAt).ThenBy(item => item.entry.Id).ThenBy(item => item.line.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new JournalLineResult(item.line.Id, item.entry.Id, item.entry.WalletTransactionId, item.entry.WalletId, item.entry.Source,
                item.entry.Description, item.line.AccountCode, item.line.Debit, item.line.Credit, item.entry.CreatedAt))
            .ToListAsync(cancellationToken);
        return new PagedData<JournalLineResult>(rows, PaginationMetadata.Create(page, pageSize, total));
    }
}
