namespace Wallet.Tests;

using System.ComponentModel.DataAnnotations;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

public sealed class WalletLedgerQueryTests
{
    private static WalletAccount AddWallet(TestWalletDbContext context)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = 100m,
            Currency = CurrencyCode.Vnd
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static WalletTransaction AddTransaction(
        TestWalletDbContext context,
        WalletAccount wallet,
        Guid id,
        DateTimeOffset createdAt,
        WalletTransactionType type = WalletTransactionType.TransferIn,
        TransactionStatus status = TransactionStatus.Success)
    {
        var transaction = new WalletTransaction
        {
            Id = id,
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = type,
            Amount = 10m,
            Currency = "VND",
            BalanceBefore = 90m,
            BalanceAfter = 100m,
            ReferenceId = Guid.NewGuid(),
            Status = status,
            Note = "sensitive note",
            CreatedAt = createdAt
        };
        context.WalletTransactions.Add(transaction);
        context.SaveChanges();
        return transaction;
    }

    [Fact]
    public async Task Ledger_AppliesInclusiveUtcDateAndOtherFilters()
    {
        using var context = new TestWalletDbContext(nameof(Ledger_AppliesInclusiveUtcDateAndOtherFilters));
        var wallet = AddWallet(context);
        var otherWallet = AddWallet(context);
        var from = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var lower = AddTransaction(context, wallet, Guid.NewGuid(), from);
        var upper = AddTransaction(context, wallet, Guid.NewGuid(), to);
        AddTransaction(context, wallet, Guid.NewGuid(), from.AddTicks(-1));
        AddTransaction(context, wallet, Guid.NewGuid(), from.AddTicks(1), WalletTransactionType.TransferOut);
        AddTransaction(context, wallet, Guid.NewGuid(), from.AddTicks(2), status: TransactionStatus.Failed);
        AddTransaction(context, otherWallet, Guid.NewGuid(), from);

        var result = await new ListWalletLedgerQueryHandler(context).Handle(
            new ListWalletLedgerQuery(
                Page: 1,
                PageSize: 10,
                WalletId: wallet.Id,
                Type: "transferin",
                Status: "SUCCESS",
                FromDate: from.ToOffset(TimeSpan.FromHours(5)),
                ToDate: to.ToOffset(TimeSpan.FromHours(-4))),
            default);

        Assert.Equal(2, result.Pagination.TotalCount);
        Assert.Equal(new[] { upper.Id, lower.Id }, result.Items.Select(item => item.TransactionId));
        Assert.All(result.Items, item => Assert.Equal("TransferIn", item.Type));
        Assert.All(result.Items, item => Assert.Equal("Success", item.Status));
    }

    [Fact]
    public async Task Ledger_UsesStableOrderingAndDatabasePagination()
    {
        using var context = new TestWalletDbContext(nameof(Ledger_UsesStableOrderingAndDatabasePagination));
        var wallet = AddWallet(context);
        var timestamp = new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        AddTransaction(context, wallet, firstId, timestamp);
        AddTransaction(context, wallet, secondId, timestamp);
        AddTransaction(context, wallet, thirdId, timestamp);

        var page = await new ListWalletLedgerQueryHandler(context).Handle(
            new ListWalletLedgerQuery(Page: 2, PageSize: 1),
            default);
        var emptyPage = await new ListWalletLedgerQueryHandler(context).Handle(
            new ListWalletLedgerQuery(Page: 4, PageSize: 1),
            default);

        Assert.Equal(3, page.Pagination.TotalCount);
        Assert.Equal(secondId, Assert.Single(page.Items).TransactionId);
        Assert.Empty(emptyPage.Items);
        Assert.Equal(3, emptyPage.Pagination.TotalCount);
        Assert.Equal(4, emptyPage.Pagination.PageIndex);
    }

    [Fact]
    public void LedgerResponse_ExcludesNotesAndBankDetails()
    {
        var propertyNames = typeof(AdminWalletLedgerEntry)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Note", propertyNames);
        Assert.DoesNotContain("BankAccountNumber", propertyNames);
        Assert.DoesNotContain("BankAccountHolder", propertyNames);
    }

    [Fact]
    public void LedgerQuery_RejectsInvalidFiltersAndReversedDates()
    {
        var query = new ListWalletLedgerQuery(
            WalletId: Guid.Empty,
            Type: "Unknown",
            Status: "Unknown",
            FromDate: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            ToDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var results = new List<ValidationResult>();

        Assert.False(RequestValidation.TryValidate(query, results));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(query.WalletId)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(query.Type)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(query.Status)));
        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(query.FromDate)) &&
            result.MemberNames.Contains(nameof(query.ToDate)));
    }
}
