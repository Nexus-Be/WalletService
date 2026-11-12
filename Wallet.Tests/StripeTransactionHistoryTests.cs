namespace Wallet.Tests;

using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeTransactionHistoryTests
{
    [Fact]
    public async Task DepositFilter_IncludesStripeAndHistoricalVnPayButOnlyForOwner()
    {
        using var context = new TestWalletDbContext(nameof(DepositFilter_IncludesStripeAndHistoricalVnPayButOnlyForOwner));
        var owner = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        context.WalletTransactions.AddRange(
            Transaction(owner, walletId, WalletTransactionType.TopupVnpay),
            Transaction(owner, walletId, WalletTransactionType.TopupStripe),
            Transaction(owner, walletId, WalletTransactionType.PurchaseDebit),
            Transaction(Guid.NewGuid(), walletId, WalletTransactionType.TopupStripe));
        await context.SaveChangesAsync();

        var result = await new ListTransactionsQueryHandler(context).Handle(
            new ListTransactionsQuery(Type: "DEPOSIT") { UserId = owner.ToString() }, default);

        Assert.Equal(2, result.Pagination.TotalCount);
        Assert.Contains(result.Items, item => item.Type == "TopupVnpay");
        Assert.Contains(result.Items, item => item.Type == "TopupStripe");
        Assert.Equal(0, (int)WalletTransactionType.TopupVnpay);
        Assert.Equal(13, (int)WalletTransactionType.TopupStripe);
    }

    private static WalletTransaction Transaction(Guid owner, Guid walletId, WalletTransactionType type) => new()
    {
        Id = Guid.NewGuid(),
        WalletId = walletId,
        UserId = owner.ToString(),
        Type = type,
        Amount = 100000,
        Status = TransactionStatus.Success
    };
}
