namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.SetDefaultBankAccount;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class P0PostgresRegressionTests
{
    [DisposablePostgresFact]
    public async Task BankAccounts_SwitchDefaultCreateAndDeletePromote_NeverViolateUniqueIndex()
    {
        var (options, ownerId, _) = await Setup();

        Guid lowerId;
        Guid higherId;
        await using (var context = new WalletDbContext(options))
        {
            var create = new CreateBankAccountCommandHandler(context);
            var a = await create.Handle(
                new CreateBankAccountCommand("VCB", "1111000011", "Seller One", IsDefault: true) { UserId = ownerId },
                default);
            var b = await create.Handle(
                new CreateBankAccountCommand("ACB", "2222000022", "Seller One", IsDefault: true) { UserId = ownerId },
                default);
            var c = await create.Handle(
                new CreateBankAccountCommand("TPB", "3333000033", "Seller One", IsDefault: false) { UserId = ownerId },
                default);

            var sorted = new[] { a.Id, b.Id, c.Id }.OrderBy(id => id).ToArray();
            lowerId = sorted[0];
            higherId = sorted[2];
        }

        // Flip default in both UUID directions (lower -> higher and higher -> lower) on real PostgreSQL.
        await using (var context = new WalletDbContext(options))
        {
            var setDefault = new SetDefaultBankAccountCommandHandler(context);
            await setDefault.Handle(new SetDefaultBankAccountCommand(higherId) { UserId = ownerId }, default);
            await setDefault.Handle(new SetDefaultBankAccountCommand(lowerId) { UserId = ownerId }, default);
            await setDefault.Handle(new SetDefaultBankAccountCommand(higherId) { UserId = ownerId }, default);
        }

        // Deleting the active default must promote a replacement without tripping IX_BankAccounts_UserId_Default.
        await using (var context = new WalletDbContext(options))
        {
            await new DeleteBankAccountCommandHandler(context)
                .Handle(new DeleteBankAccountCommand(higherId) { UserId = ownerId }, default);
        }

        await using var verify = new WalletDbContext(options);
        var active = await verify.BankAccounts.AsNoTracking()
            .Where(item => item.UserId == ownerId.ToString())
            .ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Single(active, item => item.IsDefault);
    }

    [DisposablePostgresFact]
    public async Task ListTransactions_WithNonUtcDateOffsets_SucceedsOnPostgres()
    {
        var (options, ownerId, _) = await Setup();

        await using (var context = new WalletDbContext(options))
        {
            var orderId = Guid.NewGuid();
            await new PayOrderWithWalletCommandHandler(context).Handle(
                new PayOrderWithWalletCommand(50_000m) { OrderId = orderId, UserId = ownerId.ToString() },
                default);
        }

        await using var queryContext = new WalletDbContext(options);
        var nowVn = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var result = await new ListTransactionsQueryHandler(queryContext).Handle(
            new ListTransactionsQuery(1, 10, FromDate: nowVn.AddHours(-1), ToDate: nowVn.AddHours(1))
            {
                UserId = ownerId.ToString()
            },
            default);

        Assert.Single(result.Items);
    }

    [DisposablePostgresFact]
    public async Task SubOrderSettlements_WithParentOrderId_EnforceParentEscrowOnPostgres()
    {
        var (options, buyerId, seller1Id) = await Setup(1000m);
        var seller2Id = Guid.NewGuid();
        var parentOrderId = Guid.NewGuid();
        var sub1 = Guid.NewGuid();
        var sub2 = Guid.NewGuid();

        await using (var context = new WalletDbContext(options))
        {
            await new PayOrderWithWalletCommandHandler(context).Handle(
                new PayOrderWithWalletCommand(100m) { OrderId = parentOrderId, UserId = buyerId.ToString() },
                default);
        }

        await using (var context = new WalletDbContext(options))
        {
            var handler = new SettleSellerOrderCommandHandler(context);
            await handler.Handle(new SettleSellerOrderCommand(seller1Id.ToString(), 60m, parentOrderId) { SubOrderId = sub1 }, default);
            var replay = await handler.Handle(new SettleSellerOrderCommand(seller1Id.ToString(), 60m, parentOrderId) { SubOrderId = sub1 }, default);
            Assert.Equal(60m, replay.Amount);

            var overdraw = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                handler.Handle(new SettleSellerOrderCommand(seller2Id.ToString(), 50m, parentOrderId) { SubOrderId = sub2 }, default));
            Assert.Equal("ORDER_ESCROW_INSUFFICIENT", overdraw.ErrorCode);
        }

        await using (var context = new WalletDbContext(options))
        {
            await new RefundOrderPaymentCommandHandler(context).Handle(
                new RefundOrderPaymentCommand(buyerId.ToString(), 40m, "Sub-order 2 refunded", parentOrderId)
                {
                    OrderId = sub2,
                    IsSubOrder = true
                },
                default);
        }

        await using var verify = new WalletDbContext(options);
        var escrow = await verify.OrderEscrows.SingleAsync(item => item.OrderId == parentOrderId);
        Assert.Equal(0m, escrow.RemainingAmount);
        Assert.Equal(OrderEscrowStatus.Refunded, escrow.Status);
    }

    private static async Task<(DbContextOptions<WalletDbContext> Options, Guid BuyerId, Guid SellerId)> Setup(
        decimal buyerBalance = 1000m)
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        context.Wallets.AddRange(
            new WalletAccount
            {
                Id = Guid.NewGuid(),
                UserId = buyerId,
                Balance = buyerBalance,
                Currency = CurrencyCode.Vnd
            },
            new WalletAccount
            {
                Id = Guid.NewGuid(),
                UserId = sellerId,
                Currency = CurrencyCode.Vnd
            });
        await context.SaveChangesAsync();
        return (options, buyerId, sellerId);
    }

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
            {
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
            }
        }
    }
}
