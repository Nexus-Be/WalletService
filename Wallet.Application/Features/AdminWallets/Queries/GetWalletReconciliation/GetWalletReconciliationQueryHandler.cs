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
        {
            wallets = wallets.Where(wallet => wallet.Id == request.WalletId.Value);
        }

        var totalCount = await wallets.CountAsync(cancellationToken);
        var offset = (int)System.Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var projections = await wallets
            .OrderBy(wallet => wallet.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(wallet => new ReconciliationProjection(
                wallet.Id,
                wallet.Currency.ToString(),
                wallet.Balance,
                _context.WalletTransactions.AsNoTracking().Count(transaction =>
                    transaction.WalletId == wallet.Id && transaction.Status == TransactionStatus.Success),
                _context.WalletTransactions
                    .AsNoTracking()
                    .Where(transaction =>
                        transaction.WalletId == wallet.Id &&
                        transaction.Status == TransactionStatus.Success)
                    .OrderByDescending(transaction => transaction.CreatedAt)
                    .ThenByDescending(transaction => transaction.Id)
                    .Select(transaction => (decimal?)transaction.BalanceAfter)
                    .FirstOrDefault(),
                _context.WalletTransactions
                    .AsNoTracking()
                    .Where(transaction =>
                        transaction.WalletId == wallet.Id &&
                        transaction.Status == TransactionStatus.Success)
                    .OrderByDescending(transaction => transaction.CreatedAt)
                    .ThenByDescending(transaction => transaction.Id)
                    .Select(transaction => (DateTimeOffset?)transaction.CreatedAt)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var items = projections
            .Select(projection =>
            {
                var hasHistory = projection.LatestSuccessfulBalance.HasValue;
                decimal? difference = hasHistory
                    ? projection.CurrentBalance - projection.LatestSuccessfulBalance!.Value
                    : null;
                var result = !hasHistory
                    ? "InsufficientHistory"
                    : difference == 0m
                        ? "Balanced"
                        : "Discrepancy";

                return new WalletReconciliationResult(
                    projection.WalletId.ToString(),
                    projection.Currency,
                    projection.CurrentBalance,
                    projection.LatestSuccessfulBalance,
                    difference,
                    projection.SuccessfulTransactionCount,
                    projection.LatestTransactionAt,
                    result);
            })
            .ToList();

        return new PagedData<WalletReconciliationResult>(
            items,
            PaginationMetadata.Create(page, pageSize, totalCount));
    }

    private sealed record ReconciliationProjection(
        System.Guid WalletId,
        string Currency,
        decimal CurrentBalance,
        int SuccessfulTransactionCount,
        decimal? LatestSuccessfulBalance,
        System.DateTimeOffset? LatestTransactionAt);
}
