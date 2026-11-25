namespace Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

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

        if (request.UserId.HasValue)
        {
            var userIdStr = request.UserId.Value.ToString();
            query = query.Where(transaction => transaction.UserId == userIdStr);
        }

        if (request.ReferenceId.HasValue)
            query = query.Where(transaction => transaction.ReferenceId == request.ReferenceId.Value);

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!request.Type.TryToEnum<WalletTransactionType>(out var type))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_TYPE", "Transaction type is invalid.");
            }

            query = query.Where(transaction => transaction.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<TransactionStatus>(out var status))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_STATUS", "Transaction status is invalid.");
            }

            query = query.Where(transaction => transaction.Status == status);
        }

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
                transaction.Currency ?? "Vnd",
                transaction.BalanceBefore,
                transaction.BalanceAfter,
                transaction.Status.ToString(),
                transaction.ReferenceId,
                transaction.CreatedAt,
                transaction.UserId ?? string.Empty))
            .ToListAsync(cancellationToken);

        return new PagedData<AdminWalletLedgerEntry>(
            items,
            PaginationMetadata.Create(page, pageSize, totalCount));
    }
}
