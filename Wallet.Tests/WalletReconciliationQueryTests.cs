namespace Wallet.Tests;

using System.ComponentModel.DataAnnotations;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

public sealed class WalletReconciliationQueryTests
{
    private static WalletAccount AddWallet(
        TestWalletDbContext context,
        Guid id,
        decimal balance,
        CurrencyCode currency = CurrencyCode.Vnd)
    {
        var wallet = new WalletAccount
        {
            Id = id,
            UserId = Guid.NewGuid(),
            Balance = balance,
            Currency = currency
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static WalletTransaction AddTransaction(
        TestWalletDbContext context,
        WalletAccount wallet,
        Guid id,
        decimal balanceAfter,
        DateTimeOffset createdAt,
        TransactionStatus status = TransactionStatus.Success)
    {
        var transaction = new WalletTransaction
        {
            Id = id,
            WalletId = wallet.Id,
            Type = WalletTransactionType.AdjustmentCredit,
            Amount = 10m,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceAfter - 10m,
            BalanceAfter = balanceAfter,
            Status = status,
            CreatedAt = createdAt
        };
        context.WalletTransactions.Add(transaction);
        context.SaveChanges();
        return transaction;
    }

    [Fact]
    public async Task Reconciliation_ReportsBalancedDiscrepancyAndInsufficientHistory()
    {
        using var context = new TestWalletDbContext(nameof(Reconciliation_ReportsBalancedDiscrepancyAndInsufficientHistory));
        var timestamp = new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero);
        var balanced = AddWallet(
            context,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            100m);
        var discrepancy = AddWallet(
            context,
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            125m,
            CurrencyCode.Usd);
        var noHistory = AddWallet(
            context,
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            0m);
        AddTransaction(
            context,
            balanced,
            Guid.Parse("00000000-0000-0000-0000-000000000010"),
            50m,
            timestamp);
        AddTransaction(
            context,
            balanced,
            Guid.Parse("00000000-0000-0000-0000-000000000011"),
            100m,
            timestamp);
        AddTransaction(
            context,
            discrepancy,
            Guid.Parse("00000000-0000-0000-0000-000000000020"),
            100m,
            timestamp);
        AddTransaction(
            context,
            discrepancy,
            Guid.Parse("00000000-0000-0000-0000-000000000021"),
            200m,
            timestamp.AddDays(1),
            TransactionStatus.Failed);

        var result = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(),
            default);
        var balancedItem = Assert.Single(result.Items, item => item.WalletId == balanced.Id);
        var discrepancyItem = Assert.Single(result.Items, item => item.WalletId == discrepancy.Id);
        var noHistoryItem = Assert.Single(result.Items, item => item.WalletId == noHistory.Id);

        Assert.Equal("Balanced", balancedItem.Result);
        Assert.Equal(100m, balancedItem.LatestSuccessfulBalance);
        Assert.Equal(0m, balancedItem.Difference);
        Assert.Equal(2, balancedItem.SuccessfulTransactionCount);
        Assert.Equal(timestamp, balancedItem.LatestTransactionAt);
        Assert.Equal("Discrepancy", discrepancyItem.Result);
        Assert.Equal(100m, discrepancyItem.LatestSuccessfulBalance);
        Assert.Equal(25m, discrepancyItem.Difference);
        Assert.Equal(1, discrepancyItem.SuccessfulTransactionCount);
        Assert.Equal("InsufficientHistory", noHistoryItem.Result);
        Assert.Null(noHistoryItem.LatestSuccessfulBalance);
        Assert.Null(noHistoryItem.Difference);
        Assert.Equal(0, noHistoryItem.SuccessfulTransactionCount);
    }

    [Fact]
    public async Task Reconciliation_FiltersAndPaginatesWalletsWithoutChangingSourceData()
    {
        using var context = new TestWalletDbContext(nameof(Reconciliation_FiltersAndPaginatesWalletsWithoutChangingSourceData));
        var first = AddWallet(context, Guid.Parse("00000000-0000-0000-0000-000000000001"), 10m);
        var second = AddWallet(context, Guid.Parse("00000000-0000-0000-0000-000000000002"), 20m);
        var third = AddWallet(context, Guid.Parse("00000000-0000-0000-0000-000000000003"), 30m);
        var timestamp = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        AddTransaction(context, first, Guid.NewGuid(), 10m, timestamp);
        AddTransaction(context, second, Guid.NewGuid(), 20m, timestamp);
        AddTransaction(context, third, Guid.NewGuid(), 30m, timestamp);
        var transactionCount = context.WalletTransactions.Count();
        var auditCount = context.AuditLogs.Count();

        var page = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(Page: 2, PageSize: 1),
            default);
        var filtered = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(WalletId: third.Id),
            default);

        Assert.Equal(3, page.Pagination.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).WalletId);
        Assert.Equal(third.Id, Assert.Single(filtered.Items).WalletId);
        Assert.Equal(transactionCount, context.WalletTransactions.Count());
        Assert.Equal(auditCount, context.AuditLogs.Count());
        Assert.Equal(10m, first.Balance);
        Assert.Equal(20m, second.Balance);
        Assert.Equal(30m, third.Balance);
    }

    [Fact]
    public void ReconciliationQuery_RejectsEmptyWalletIdAndInvalidPagination()
    {
        var query = new GetWalletReconciliationQuery(Page: 0, PageSize: 101, WalletId: Guid.Empty);
        var results = new List<ValidationResult>();

        Assert.False(RequestValidation.TryValidate(query, results));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(query.Page)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(query.PageSize)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(query.WalletId)));
    }

    [Fact]
    public async Task Reconciliation_FlagsLockedBalanceMismatch_ExcludesDirectGatewayAndFlagsNonZeroZeroHistory()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var timestamp = new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);

        var lockedMismatch = AddWallet(context, Guid.NewGuid(), 100_000m);
        lockedMismatch.LockedBalance = 50_000m;
        context.AuctionHolds.Add(new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = lockedMismatch.Id,
            OwnerUserId = lockedMismatch.UserId,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "recon-hold-1",
            Amount = 30_000m,
            MetadataJson = "{}",
            Status = AuctionHoldStatus.Held
        });
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = lockedMismatch.Id,
            Type = WalletTransactionType.TopupStripe,
            Amount = 100_000m,
            Currency = "Vnd",
            BalanceBefore = 0m,
            BalanceAfter = 100_000m,
            Status = TransactionStatus.Success,
            CreatedAt = timestamp
        });

        var directExcluded = AddWallet(context, Guid.NewGuid(), 100_000m);
        context.WalletTransactions.AddRange(
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = directExcluded.Id,
                Type = WalletTransactionType.TopupStripe,
                Amount = 100_000m,
                Currency = "Vnd",
                BalanceBefore = 0m,
                BalanceAfter = 100_000m,
                Status = TransactionStatus.Success,
                CreatedAt = timestamp
            },
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = directExcluded.Id,
                Type = WalletTransactionType.DirectGatewayPayment,
                Amount = 50_000m,
                Currency = "Vnd",
                BalanceBefore = 0m,
                BalanceAfter = 0m,
                Status = TransactionStatus.Success,
                CreatedAt = timestamp.AddMinutes(5)
            });

        var nonZeroNoTx = AddWallet(context, Guid.NewGuid(), 25_000m);
        var zeroNoTx = AddWallet(context, Guid.NewGuid(), 0m);
        await context.SaveChangesAsync();

        var handler = new GetWalletReconciliationQueryHandler(context);
        var result = await handler.Handle(new GetWalletReconciliationQuery(Page: 1, PageSize: 20), default);

        var lockedRow = Assert.Single(result.Items, item => item.WalletId == lockedMismatch.Id);
        Assert.Equal("Discrepancy", lockedRow.Result);
        Assert.Equal(50_000m, lockedRow.LockedBalance);
        Assert.Equal(30_000m, lockedRow.ExpectedLockedBalance);
        Assert.Equal(20_000m, lockedRow.LockedDifference);

        var directRow = Assert.Single(result.Items, item => item.WalletId == directExcluded.Id);
        Assert.Equal("Balanced", directRow.Result);
        Assert.Equal(100_000m, directRow.LatestSuccessfulBalance);
        Assert.Equal(1, directRow.SuccessfulTransactionCount);

        var nonZeroRow = Assert.Single(result.Items, item => item.WalletId == nonZeroNoTx.Id);
        Assert.Equal("Discrepancy", nonZeroRow.Result);
        Assert.Equal(25_000m, nonZeroRow.CurrentBalance);
        Assert.Null(nonZeroRow.LatestSuccessfulBalance);
        Assert.Null(nonZeroRow.Difference);

        var zeroRow = Assert.Single(result.Items, item => item.WalletId == zeroNoTx.Id);
        Assert.Equal("InsufficientHistory", zeroRow.Result);
    }
}
