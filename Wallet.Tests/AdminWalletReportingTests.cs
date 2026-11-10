namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;
using Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

public sealed class AdminWalletReportingTests
{
    [Fact]
    public async Task ListWalletLedger_FiltersAndReturnsStablePagesWithoutSensitiveFields()
    {
        using var context = new TestWalletDbContext(nameof(ListWalletLedger_FiltersAndReturnsStablePagesWithoutSensitiveFields));
        var wallet = AddWallet(context, 100m);
        var otherWallet = AddWallet(context, 50m);
        var date = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        AddTransaction(context, wallet, firstId, WalletTransactionType.TransferIn, TransactionStatus.Success, date);
        AddTransaction(context, wallet, secondId, WalletTransactionType.TransferIn, TransactionStatus.Success, date);
        AddTransaction(context, wallet, Guid.NewGuid(), WalletTransactionType.TransferOut, TransactionStatus.Pending, date.AddDays(-1));
        AddTransaction(context, otherWallet, Guid.NewGuid(), WalletTransactionType.TransferIn, TransactionStatus.Success, date);

        var result = await new ListWalletLedgerQueryHandler(context).Handle(
            new ListWalletLedgerQuery(
                Page: 1,
                PageSize: 1,
                WalletId: wallet.Id,
                Type: nameof(WalletTransactionType.TransferIn),
                Status: nameof(TransactionStatus.Success),
                FromDate: date,
                ToDate: date),
            default);

        var entry = Assert.Single(result.Items);
        Assert.Equal(secondId.ToString(), entry.TransactionId);
        Assert.Equal(2, result.Pagination.TotalCount);
        Assert.Equal(1, result.Pagination.PageIndex);
        Assert.Equal(1, result.Pagination.PageSize);
        Assert.Equal(2, result.Pagination.TotalPages);
        Assert.True(result.Pagination.HasNextPage);
        Assert.False(result.Pagination.HasPreviousPage);
        var nextPage = await new ListWalletLedgerQueryHandler(context).Handle(
            new ListWalletLedgerQuery(
                Page: 2,
                PageSize: 1,
                WalletId: wallet.Id,
                Type: nameof(WalletTransactionType.TransferIn),
                Status: nameof(TransactionStatus.Success),
                FromDate: date,
                ToDate: date),
            default);
        Assert.Equal(firstId.ToString(), Assert.Single(nextPage.Items).TransactionId);
        Assert.False(nextPage.Pagination.HasNextPage);
        Assert.True(nextPage.Pagination.HasPreviousPage);
        Assert.DoesNotContain(
            typeof(AdminWalletLedgerEntry).GetProperties(),
            property => property.Name.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Note", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 101)]
    public void ListWalletLedger_RejectsInvalidPagination(int page, int pageSize)
    {
        var query = new ListWalletLedgerQuery(Page: page, PageSize: pageSize);

        var errors = new System.Collections.Generic.List<System.ComponentModel.DataAnnotations.ValidationResult>();
        RequestValidation.TryValidate(query, errors);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ListWalletLedger_RejectsInvalidDateRange()
    {
        var query = new ListWalletLedgerQuery(
            FromDate: DateTimeOffset.UtcNow,
            ToDate: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.NotEmpty(query.ValidateRequest());
    }

    [Fact]
    public void ListWalletLedger_RejectsUndefinedTypeAndStatusValues()
    {
        var query = new ListWalletLedgerQuery(Type: "999", Status: "-1");

        Assert.Equal(2, query.ValidateRequest().Count());
    }

    [Fact]
    public async Task ListWalletLedger_ReturnsEmptyPageForNoMatches()
    {
        using var context = new TestWalletDbContext(nameof(ListWalletLedger_ReturnsEmptyPageForNoMatches));
        var result = await new ListWalletLedgerQueryHandler(context).Handle(new ListWalletLedgerQuery(), default);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Pagination.TotalCount);
        Assert.Equal(0, result.Pagination.TotalPages);
    }

    [Fact]
    public async Task ListWalletLedger_LargePageNumberDoesNotOverflowOffset()
    {
        using var context = new TestWalletDbContext(nameof(ListWalletLedger_LargePageNumberDoesNotOverflowOffset));
        var result = await new ListWalletLedgerQueryHandler(context).Handle(
            new ListWalletLedgerQuery(Page: int.MaxValue, PageSize: 100),
            default);

        Assert.Empty(result.Items);
        Assert.Equal(int.MaxValue, result.Pagination.PageIndex);
    }

    [Fact]
    public async Task GetWalletReconciliation_ReportsBalancedDiscrepantAndInsufficientHistory()
    {
        using var context = new TestWalletDbContext(nameof(GetWalletReconciliation_ReportsBalancedDiscrepantAndInsufficientHistory));
        var balanced = AddWallet(context, 100m);
        var discrepant = AddWallet(context, 75m);
        var withoutHistory = AddWallet(context, 0m);
        var usdWallet = AddWallet(context, 20m, CurrencyCode.Usd);
        var date = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        AddTransaction(context, balanced, Guid.NewGuid(), WalletTransactionType.TransferIn, TransactionStatus.Success, date, balanceAfter: 100m);
        AddTransaction(context, discrepant, Guid.NewGuid(), WalletTransactionType.TransferIn, TransactionStatus.Success, date, balanceAfter: 80m);
        AddTransaction(context, discrepant, Guid.NewGuid(), WalletTransactionType.TransferIn, TransactionStatus.Failed, date.AddDays(1), balanceAfter: 75m);
        AddTransaction(context, balanced, Guid.NewGuid(), WalletTransactionType.TransferIn, TransactionStatus.Success, date, balanceAfter: 100m);
        AddTransaction(context, usdWallet, Guid.NewGuid(), WalletTransactionType.TransferIn, TransactionStatus.Success, date, balanceAfter: 20m);
        context.SaveChanges();

        var originalTransactionCount = context.WalletTransactions.Count();
        var result = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(Page: 1, PageSize: 10),
            default);

        Assert.Equal(4, result.Pagination.TotalCount);
        Assert.Equal("Balanced", Assert.Single(result.Items, item => item.WalletId == balanced.Id.ToString()).Result);
        Assert.Equal("Discrepancy", Assert.Single(result.Items, item => item.WalletId == discrepant.Id.ToString()).Result);
        Assert.Equal("InsufficientHistory", Assert.Single(result.Items, item => item.WalletId == withoutHistory.Id.ToString()).Result);
        Assert.Equal("Usd", Assert.Single(result.Items, item => item.WalletId == usdWallet.Id.ToString()).Currency);
        Assert.Equal(originalTransactionCount, context.WalletTransactions.Count());
        Assert.DoesNotContain(
            typeof(WalletReconciliationResult).GetProperties(),
            property => property.Name.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetWalletReconciliation_UsesTransactionIdToBreakTimestampTies()
    {
        using var context = new TestWalletDbContext(nameof(GetWalletReconciliation_UsesTransactionIdToBreakTimestampTies));
        var wallet = AddWallet(context, 200m);
        var date = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        AddTransaction(
            context,
            wallet,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            WalletTransactionType.TransferIn,
            TransactionStatus.Success,
            date,
            balanceAfter: 100m);
        AddTransaction(
            context,
            wallet,
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            WalletTransactionType.TransferIn,
            TransactionStatus.Success,
            date,
            balanceAfter: 200m);

        var result = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(WalletId: wallet.Id),
            default);

        Assert.Equal("Balanced", Assert.Single(result.Items).Result);
    }

    [Fact]
    public async Task GetWalletReconciliation_LargePageNumberDoesNotOverflowOffset()
    {
        using var context = new TestWalletDbContext(nameof(GetWalletReconciliation_LargePageNumberDoesNotOverflowOffset));
        var result = await new GetWalletReconciliationQueryHandler(context).Handle(
            new GetWalletReconciliationQuery(Page: int.MaxValue, PageSize: 100),
            default);

        Assert.Empty(result.Items);
        Assert.Equal(int.MaxValue, result.Pagination.PageIndex);
    }

    private static WalletAccount AddWallet(
        TestWalletDbContext context,
        decimal balance,
        CurrencyCode currency = CurrencyCode.Vnd)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = balance,
            Currency = currency,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static void AddTransaction(
        TestWalletDbContext context,
        WalletAccount wallet,
        Guid id,
        WalletTransactionType type,
        TransactionStatus status,
        DateTimeOffset createdAt,
        decimal? balanceAfter = null)
    {
        var transaction = new WalletTransaction
        {
            Id = id,
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = type,
            Amount = 25m,
            BalanceBefore = balanceAfter ?? wallet.Balance,
            BalanceAfter = balanceAfter ?? wallet.Balance,
            Currency = wallet.Currency.ToString(),
            Status = status,
            ReferenceId = Guid.NewGuid(),
            Note = "Sensitive test note",
            CreatedAt = createdAt
        };
        context.WalletTransactions.Add(transaction);
        context.SaveChanges();
    }
}
