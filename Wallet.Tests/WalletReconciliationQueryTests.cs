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
}
