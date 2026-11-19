namespace Wallet.Tests;

using System;
using System.Threading.Tasks;
using Wallet.Application.Features.Wallets.Queries.GetTransactionById;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class GetTransactionByIdTests
{
    [Fact]
    public async Task GetTransactionById_ReturnsDetailForOwner_And404ForOthers()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Balance = 350_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 150_000m,
            VnpTransactionNo = "pi_test_123",
            Status = TopupStatus.Success
        };
        var tx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = ownerId.ToString(),
            Type = WalletTransactionType.TopupStripe,
            Amount = 150_000m,
            BalanceBefore = 200_000m,
            BalanceAfter = 350_000m,
            Currency = "Vnd",
            Status = TransactionStatus.Success,
            ReferenceId = topup.Id,
            Note = "Stripe topup"
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.WalletTransactions.Add(tx);
        await context.SaveChangesAsync();

        var handler = new GetTransactionByIdQueryHandler(context);
        var detail = await handler.Handle(new GetTransactionByIdQuery(tx.Id) { UserId = ownerId }, default);

        Assert.Equal(tx.Id.ToString(), detail.TransactionId);
        Assert.Equal(200_000m, detail.BalanceBefore);
        Assert.Equal(350_000m, detail.BalanceAfter);
        Assert.Equal("Success", detail.GatewayStatus);
        Assert.Equal("pi_test_123", detail.GatewayReference);

        var notFoundOther = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetTransactionByIdQuery(tx.Id) { UserId = otherUserId }, default));
        Assert.Equal("TRANSACTION_NOT_FOUND", notFoundOther.ErrorCode);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetTransactionByIdQuery(Guid.NewGuid()) { UserId = ownerId }, default));
    }
}
