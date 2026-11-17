namespace Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Models;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;

internal sealed class GetWalletReconciliationQueryHandler
    : IRequestHandler<GetWalletReconciliationQuery, PagedData<WalletReconciliationResult>>
{
    private readonly IWalletDbContext _context;

    public GetWalletReconciliationQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<WalletReconciliationResult>> Handle(
        GetWalletReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var wallets = _context.Wallets.AsNoTracking();
        if (request.WalletId.HasValue)
            wallets = wallets.Where(wallet => wallet.Id == request.WalletId.Value);

        var totalCount = await wallets.CountAsync(cancellationToken);
        var successfulTransactions = _context.WalletTransactions
            .AsNoTracking()
            .Where(transaction => transaction.Status == TransactionStatus.Success);
        var snapshots = await wallets
            .OrderBy(wallet => wallet.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(wallet => new
            {
                wallet.Id,
                wallet.Currency,
                wallet.Balance,
                LatestSuccessfulBalance = successfulTransactions
                    .Where(transaction => transaction.WalletId == wallet.Id)
                    .OrderByDescending(transaction => transaction.CreatedAt)
                    .ThenByDescending(transaction => transaction.Id)
                    .Select(transaction => (decimal?)transaction.BalanceAfter)
                    .FirstOrDefault(),
                LatestTransactionAt = successfulTransactions
                    .Where(transaction => transaction.WalletId == wallet.Id)
                    .OrderByDescending(transaction => transaction.CreatedAt)
                    .ThenByDescending(transaction => transaction.Id)
                    .Select(transaction => (System.DateTimeOffset?)transaction.CreatedAt)
                    .FirstOrDefault(),
                SuccessfulTransactionCount = successfulTransactions
                    .Count(transaction => transaction.WalletId == wallet.Id)
            })
            .ToListAsync(cancellationToken);

        var items = snapshots
            .Select(snapshot =>
            {
                var result = snapshot.LatestSuccessfulBalance.HasValue
                    ? snapshot.Balance == snapshot.LatestSuccessfulBalance.Value
                        ? "Balanced"
                        : "Discrepancy"
                    : "InsufficientHistory";
                return new WalletReconciliationResult(
                    snapshot.Id,
                    snapshot.Currency.ToString(),
                    snapshot.Balance,
                    snapshot.LatestSuccessfulBalance,
                    snapshot.LatestSuccessfulBalance.HasValue
                        ? snapshot.Balance - snapshot.LatestSuccessfulBalance.Value
                        : null,
                    snapshot.SuccessfulTransactionCount,
                    snapshot.LatestTransactionAt,
                    result);
            })
            .ToList();

        return new PagedData<WalletReconciliationResult>(
            items,
            PaginationMetadata.Create(page, pageSize, totalCount));
    }
}
