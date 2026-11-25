namespace Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;

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
using Wallet.Domain.Exceptions;

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
        string? normalizedResult = null;
        if (!string.IsNullOrWhiteSpace(request.Result))
        {
            normalizedResult = request.Result.Trim().ToUpperInvariant() switch
            {
                "BALANCED" => "Balanced",
                "DISCREPANCY" => "Discrepancy",
                "INSUFFICIENTHISTORY" => "InsufficientHistory",
                _ => throw new BusinessRuleValidationException(
                    "VAL_INVALID_STATUS",
                    "Reconciliation result filter must be 'Balanced', 'Discrepancy', or 'InsufficientHistory'.")
            };
        }

        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var wallets = _context.Wallets.AsNoTracking();
        if (request.WalletId.HasValue)
            wallets = wallets.Where(wallet => wallet.Id == request.WalletId.Value);
        if (request.UserId.HasValue)
            wallets = wallets.Where(wallet => wallet.UserId == request.UserId.Value);

        var successfulTransactions = _context.WalletTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.Status == TransactionStatus.Success &&
                transaction.Type != WalletTransactionType.DirectGatewayPayment);

        var projected = wallets.Select(wallet => new
        {
            wallet.Id,
            wallet.UserId,
            wallet.Currency,
            wallet.Balance,
            wallet.LockedBalance,
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
                .Select(transaction => (DateTimeOffset?)transaction.CreatedAt)
                .FirstOrDefault(),
            SuccessfulTransactionCount = successfulTransactions
                .Count(transaction => transaction.WalletId == wallet.Id),
            ExpectedLockedBalance =
                (_context.AuctionHolds.AsNoTracking()
                    .Where(hold => hold.WalletId == wallet.Id && hold.Status == AuctionHoldStatus.Held)
                    .Select(hold => (decimal?)hold.Amount)
                    .Sum() ?? 0m)
                + (_context.WithdrawalRequests.AsNoTracking()
                    .Where(withdrawal => withdrawal.WalletId == wallet.Id && withdrawal.Status == WithdrawalStatus.Pending)
                    .Select(withdrawal => (decimal?)withdrawal.Amount)
                    .Sum() ?? 0m)
        });

        if (normalizedResult == "Balanced")
        {
            projected = projected.Where(item =>
                item.LockedBalance == item.ExpectedLockedBalance &&
                item.LatestSuccessfulBalance != null &&
                item.Balance == item.LatestSuccessfulBalance.Value);
        }
        else if (normalizedResult == "Discrepancy")
        {
            projected = projected.Where(item =>
                item.LockedBalance != item.ExpectedLockedBalance ||
                (item.LatestSuccessfulBalance != null && item.Balance != item.LatestSuccessfulBalance.Value) ||
                (item.LatestSuccessfulBalance == null && item.Balance != 0m));
        }
        else if (normalizedResult == "InsufficientHistory")
        {
            projected = projected.Where(item =>
                item.LockedBalance == item.ExpectedLockedBalance &&
                item.LatestSuccessfulBalance == null &&
                item.Balance == 0m);
        }

        var totalCount = await projected.CountAsync(cancellationToken);
        var snapshots = await projected
            .OrderBy(wallet => wallet.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = snapshots
            .Select(snapshot =>
            {
                var lockedDifference = snapshot.LockedBalance - snapshot.ExpectedLockedBalance;
                var result = lockedDifference != 0m
                    ? "Discrepancy"
                    : snapshot.LatestSuccessfulBalance.HasValue
                        ? snapshot.Balance == snapshot.LatestSuccessfulBalance.Value
                            ? "Balanced"
                            : "Discrepancy"
                        : snapshot.Balance == 0m
                            ? "InsufficientHistory"
                            : "Discrepancy";
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
                    result,
                    snapshot.UserId,
                    snapshot.LockedBalance,
                    snapshot.ExpectedLockedBalance,
                    lockedDifference);
            })
            .ToList();

        return new PagedData<WalletReconciliationResult>(
            items,
            PaginationMetadata.Create(page, pageSize, totalCount));
    }
}
