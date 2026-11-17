namespace Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Models;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;

internal sealed class ListWalletLedgerQueryHandler
    : IRequestHandler<ListWalletLedgerQuery, PagedData<AdminWalletLedgerEntry>>
{
    private readonly IWalletDbContext _context;

    public ListWalletLedgerQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<AdminWalletLedgerEntry>> Handle(
        ListWalletLedgerQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = _context.WalletTransactions.AsNoTracking();

        if (request.WalletId.HasValue)
            query = query.Where(transaction => transaction.WalletId == request.WalletId.Value);

        if (!string.IsNullOrWhiteSpace(request.Type) &&
            Enum.TryParse<WalletTransactionType>(request.Type.Trim(), true, out var type))
            query = query.Where(transaction => transaction.Type == type);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TransactionStatus>(request.Status.Trim(), true, out var status))
            query = query.Where(transaction => transaction.Status == status);

        if (request.FromDate.HasValue)
        {
            var fromDate = request.FromDate.Value.ToUniversalTime();
            query = query.Where(transaction => transaction.CreatedAt >= fromDate);
        }

        if (request.ToDate.HasValue)
        {
            var toDate = request.ToDate.Value.ToUniversalTime();
            query = query.Where(transaction => transaction.CreatedAt <= toDate);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(transaction => new AdminWalletLedgerEntry(
                transaction.Id,
                transaction.WalletId,
                transaction.Type.ToString(),
                transaction.Amount,
                transaction.Currency ?? "VND",
                transaction.BalanceBefore,
                transaction.BalanceAfter,
                transaction.Status.ToString(),
                transaction.ReferenceId,
                transaction.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedData<AdminWalletLedgerEntry>(
            items,
            PaginationMetadata.Create(page, pageSize, totalCount));
    }
}
