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
        {
            query = query.Where(transaction => transaction.WalletId == request.WalletId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Type) &&
            Enum.TryParse<WalletTransactionType>(request.Type.Trim(), true, out var type))
        {
            query = query.Where(transaction => transaction.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TransactionStatus>(request.Status.Trim(), true, out var status))
        {
            query = query.Where(transaction => transaction.Status == status);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(transaction => transaction.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(transaction => transaction.CreatedAt <= request.ToDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var items = await query
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(transaction => new AdminWalletLedgerEntry(
                transaction.Id.ToString(),
                transaction.WalletId.ToString(),
                transaction.Type.ToString(),
                transaction.Amount,
                transaction.Currency ?? "VND",
                transaction.BalanceBefore,
                transaction.BalanceAfter,
                transaction.Status.ToString(),
                transaction.ReferenceId.HasValue ? transaction.ReferenceId.Value.ToString() : null,
                transaction.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedData<AdminWalletLedgerEntry>(
            items,
            PaginationMetadata.Create(page, pageSize, totalCount));
    }
}
