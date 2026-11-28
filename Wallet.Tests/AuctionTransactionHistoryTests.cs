namespace Wallet.Tests;

using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

public sealed class AuctionTransactionHistoryTests
{
    [Fact]
    public async Task RefundFilter_IncludesAuctionDepositRefundForOwner()
    {
        using var context = new TestWalletDbContext(nameof(RefundFilter_IncludesAuctionDepositRefundForOwner));
        var owner = Guid.NewGuid();
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = Guid.NewGuid(), UserId = owner.ToString(),
            Type = WalletTransactionType.AuctionDepositRefund, Amount = 50, Status = TransactionStatus.Success
        });
        await context.SaveChangesAsync();
        var result = await new ListTransactionsQueryHandler(context).Handle(
            new ListTransactionsQuery(Type: "REFUND") { UserId = owner.ToString() }, default);
        Assert.Single(result.Items, item => item.Type == "AuctionDepositRefund");
    }
}
