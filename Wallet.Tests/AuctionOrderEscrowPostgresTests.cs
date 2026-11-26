namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class AuctionOrderEscrowPostgresTests
{
    [DisposablePostgresFact]
    public async Task PartialPayoutAndRefund_ConsumeWalletPaymentOnly_AndKeepDepositFullyRefundable()
    {
        var (options, buyerId, sellerId) = await Setup();
        var orderId = Guid.NewGuid();
        WalletOrderTransactionResponse payment;
        await using (var context = new WalletDbContext(options))
        {
            var handler = new PayOrderWithWalletCommandHandler(context);
            payment = await handler.Handle(Payment(buyerId, orderId, 100m), default);
            var replay = await handler.Handle(Payment(buyerId, orderId, 100m), default);
            Assert.Equal(payment.Id, replay.Id);
        }

        var holdId = await CaptureDeposit(options, buyerId, orderId, 50m);
        WalletOrderTransactionResponse payout;
        await using (var context = new WalletDbContext(options))
        {
            var handler = new SettleSellerOrderCommandHandler(context);
            payout = await handler.Handle(Settlement(sellerId, orderId, 60m), default);
            var replay = await handler.Handle(Settlement(sellerId, orderId, 60m), default);
            Assert.Equal(payout.Id, replay.Id);

            var changed = await Assert.ThrowsAsync<BusinessRuleValidationException>(
                () => handler.Handle(Settlement(sellerId, orderId, 50m), default));
            Assert.Equal("SETTLEMENT_CONFLICT", changed.ErrorCode);
        }

        await using (var context = new WalletDbContext(options))
        {
            var deposit = await context.AuctionDepositEscrows.SingleAsync(item => item.HoldId == holdId);
            Assert.Equal(50m, deposit.RemainingAmount);
        }

        WalletOrderTransactionResponse refund;
        await using (var context = new WalletDbContext(options))
        {
            var handler = new RefundOrderPaymentCommandHandler(context);
            refund = await handler.Handle(Refund(buyerId, orderId, 40m), default);
            var replay = await handler.Handle(Refund(buyerId, orderId, 40m), default);
            Assert.Equal(refund.Id, replay.Id);
        }

        await using (var context = new WalletDbContext(options))
        {
            var depositRefund = new RefundCapturedHoldCommand("SELLER_REJECTED", orderId)
            {
                HoldId = holdId,
                IdempotencyKey = $"deposit-refund:{holdId}"
            };
            await new RefundCapturedHoldCommandHandler(context).Handle(depositRefund, default);
        }

        await using var verify = new WalletDbContext(options);
        var buyer = await verify.Wallets.SingleAsync(wallet => wallet.UserId == buyerId);
        var seller = await verify.Wallets.SingleAsync(wallet => wallet.UserId == sellerId);
        var orderEscrow = await verify.OrderEscrows.SingleAsync(item => item.OrderId == orderId);
        var depositEscrow = await verify.AuctionDepositEscrows.SingleAsync(item => item.HoldId == holdId);

        Assert.Equal(940m, buyer.Balance);
        Assert.Equal(60m, seller.Balance);
        Assert.Equal(0m, orderEscrow.RemainingAmount);
        Assert.Equal(100m, orderEscrow.OriginalAmount);
        Assert.Equal(0m, depositEscrow.RemainingAmount);
        Assert.Equal(1, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.PurchaseDebit));
        Assert.Equal(1, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowRelease));
        Assert.Equal(1, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.RefundCredit));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderPayment && item.EntityId == orderId.ToString()));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderSettlement && item.EntityId == orderId.ToString()));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderRefund && item.EntityId == orderId.ToString()));
        Assert.NotEqual(payment.Id, payout.Id);
        Assert.NotEqual(payout.Id, refund.Id);
    }

    [DisposablePostgresFact]
    public async Task Payout_RejectsAmountAboveWalletPaymentEscrowEvenWhenDepositExists()
    {
        var (options, buyerId, sellerId) = await Setup();
        var orderId = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
        {
            await new PayOrderWithWalletCommandHandler(context)
                .Handle(Payment(buyerId, orderId, 100m), default);
        }

        var holdId = await CaptureDeposit(options, buyerId, orderId, 50m);
        await using (var context = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(
                () => new SettleSellerOrderCommandHandler(context)
                    .Handle(Settlement(sellerId, orderId, 101m), default));
            Assert.Equal("ORDER_ESCROW_INSUFFICIENT", error.ErrorCode);
            Assert.Equal(409, error.StatusCode);
        }

        await using var verify = new WalletDbContext(options);
        Assert.Equal(0m, (await verify.Wallets.SingleAsync(wallet => wallet.UserId == sellerId)).Balance);
        Assert.Equal(100m, (await verify.OrderEscrows.SingleAsync(item => item.OrderId == orderId)).RemainingAmount);
        Assert.Equal(50m, (await verify.AuctionDepositEscrows.SingleAsync(item => item.HoldId == holdId)).RemainingAmount);
        Assert.Equal(0, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowRelease));
        Assert.Equal(0, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderSettlement && item.EntityId == orderId.ToString()));
    }

    [DisposablePostgresFact]
    public async Task HistoricalPaymentWithoutResolution_ReconcilesBeforePayout()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 900m);
        var orderId = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
        {
            var wallet = await context.Wallets.SingleAsync(item => item.UserId == buyerId);
            context.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                UserId = buyerId.ToString(),
                Type = WalletTransactionType.PurchaseDebit,
                Amount = 100m,
                Currency = wallet.Currency.ToString(),
                BalanceBefore = 1000m,
                BalanceAfter = 900m,
                Status = TransactionStatus.Success,
                ReferenceId = orderId
            });
            await context.SaveChangesAsync();
        }

        await using (var context = new WalletDbContext(options))
        {
            await new SettleSellerOrderCommandHandler(context)
                .Handle(Settlement(sellerId, orderId, 40m), default);
        }

        await using var verify = new WalletDbContext(options);
        var escrow = await verify.OrderEscrows.SingleAsync(item => item.OrderId == orderId);
        Assert.Equal(60m, escrow.RemainingAmount);
        Assert.Equal(40m, (await verify.Wallets.SingleAsync(item => item.UserId == sellerId)).Balance);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderEscrowReconciliation && item.EntityId == escrow.Id.ToString()));
    }

    [DisposablePostgresFact]
    public async Task HistoricalPaymentAlreadyRefunded_DoesNotReconstructOrPayEscrow()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 1000m);
        var orderId = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
        {
            var wallet = await context.Wallets.SingleAsync(item => item.UserId == buyerId);
            context.WalletTransactions.AddRange(
                new WalletTransaction
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    UserId = buyerId.ToString(),
                    Type = WalletTransactionType.PurchaseDebit,
                    Amount = 100m,
                    Currency = wallet.Currency.ToString(),
                    BalanceBefore = 1100m,
                    BalanceAfter = 1000m,
                    Status = TransactionStatus.Success,
                    ReferenceId = orderId
                },
                new WalletTransaction
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    UserId = buyerId.ToString(),
                    Type = WalletTransactionType.RefundCredit,
                    Amount = 100m,
                    Currency = wallet.Currency.ToString(),
                    BalanceBefore = 1000m,
                    BalanceAfter = 1100m,
                    Status = TransactionStatus.Success,
                    ReferenceId = orderId
                });
            await context.SaveChangesAsync();
        }

        await using (var context = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(
                () => new SettleSellerOrderCommandHandler(context)
                    .Handle(Settlement(sellerId, orderId, 100m), default));
            Assert.Equal("SETTLEMENT_CONFLICT", error.ErrorCode);
        }

        await using var verify = new WalletDbContext(options);
        Assert.Equal(0m, (await verify.Wallets.SingleAsync(item => item.UserId == sellerId)).Balance);
        Assert.Equal(0, await verify.OrderEscrows.CountAsync(item => item.OrderId == orderId));
        Assert.Equal(0, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowRelease));
    }

    [DisposablePostgresFact]
    public async Task ConcurrentPayoutAndRefund_CannotSpendTheSameEscrowTwice()
    {
        var (options, buyerId, sellerId) = await Setup();
        var orderId = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
        {
            await new PayOrderWithWalletCommandHandler(context)
                .Handle(Payment(buyerId, orderId, 100m), default);
        }

        await using var payoutContext = new WalletDbContext(options);
        await using var refundContext = new WalletDbContext(options);
        var outcomes = await Task.WhenAll(
            Attempt(() => new SettleSellerOrderCommandHandler(payoutContext)
                .Handle(Settlement(sellerId, orderId, 60m), default)),
            Attempt(() => new RefundOrderPaymentCommandHandler(refundContext)
                .Handle(Refund(buyerId, orderId, 60m), default)));

        Assert.Single(outcomes, outcome => outcome.Succeeded);
        Assert.Equal("ORDER_ESCROW_INSUFFICIENT", Assert.Single(outcomes, outcome => !outcome.Succeeded).ErrorCode);

        await using var verify = new WalletDbContext(options);
        var escrow = await verify.OrderEscrows.SingleAsync(item => item.OrderId == orderId);
        Assert.Equal(40m, escrow.RemainingAmount);
        Assert.Equal(60m, await verify.WalletTransactions
            .Where(item => item.ReferenceId == orderId &&
                           (item.Type == WalletTransactionType.EscrowRelease ||
                            item.Type == WalletTransactionType.RefundCredit))
            .SumAsync(item => item.Amount));
    }

    [DisposablePostgresFact]
    public async Task FailedPaymentInsert_RollsBackBalanceTransactionAndAudit()
    {
        var (options, buyerId, _) = await Setup();
        var orderId = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            var wallet = await setup.Wallets.SingleAsync(item => item.UserId == buyerId);
            setup.OrderEscrows.Add(new OrderEscrow
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                WalletId = wallet.Id,
                OriginalAmount = 100m,
                RemainingAmount = 100m,
                IsDeleted = true
            });
            await setup.SaveChangesAsync();
        }

        await using (var context = new WalletDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => new PayOrderWithWalletCommandHandler(context)
                    .Handle(Payment(buyerId, orderId, 100m), default));
        }

        await using var verify = new WalletDbContext(options);
        Assert.Equal(1000m, (await verify.Wallets.SingleAsync(item => item.UserId == buyerId)).Balance);
        Assert.Equal(0, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.PurchaseDebit));
        Assert.Equal(0, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderPayment && item.EntityId == orderId.ToString()));
        Assert.Equal(1, await verify.OrderEscrows.IgnoreQueryFilters()
            .CountAsync(item => item.OrderId == orderId));
    }

    [DisposablePostgresFact]
    public async Task FailedRefundInsert_RollsBackBalanceEscrowTransactionAndAudit()
    {
        var (options, buyerId, _) = await Setup(buyerBalance: 900m);
        var orderId = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            var wallet = await setup.Wallets.SingleAsync(item => item.UserId == buyerId);
            setup.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                UserId = buyerId.ToString(),
                Type = WalletTransactionType.PurchaseDebit,
                Amount = 100m,
                Currency = wallet.Currency.ToString(),
                BalanceBefore = 1000m,
                BalanceAfter = 900m,
                Status = TransactionStatus.Success,
                ReferenceId = orderId
            });
            setup.OrderEscrows.Add(new OrderEscrow
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                WalletId = wallet.Id,
                OriginalAmount = 100m,
                RemainingAmount = 100m,
                IsDeleted = true
            });
            await setup.SaveChangesAsync();
        }

        await using (var context = new WalletDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => new RefundOrderPaymentCommandHandler(context)
                    .Handle(Refund(buyerId, orderId, 100m), default));
        }

        await using var verify = new WalletDbContext(options);
        Assert.Equal(900m, (await verify.Wallets.SingleAsync(item => item.UserId == buyerId)).Balance);
        Assert.Equal(0, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.RefundCredit));
        Assert.Equal(0, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderRefund && item.EntityId == orderId.ToString()));
        Assert.Equal(0, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderEscrowReconciliation && item.UserId == buyerId.ToString()));
        Assert.Equal(1, await verify.OrderEscrows.IgnoreQueryFilters()
            .CountAsync(item => item.OrderId == orderId));
    }

    [DisposablePostgresFact]
    public async Task ConcurrentCancelAndSettlement_OnlyOneSucceeds_AndConservesEscrow()
    {
        var (options, buyerId, sellerId) = await Setup();
        var orderId = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
        {
            await new PayOrderWithWalletCommandHandler(context)
                .Handle(Payment(buyerId, orderId, 100m), default);
        }

        await using var cancelContext = new WalletDbContext(options);
        await using var settleContext = new WalletDbContext(options);
        var cancelTask = Attempt(() => new CancelOrderEscrowCommandHandler(cancelContext)
            .Handle(new CancelOrderEscrowCommand("Concurrent cancel")
            {
                OrderId = orderId,
                IdempotencyKey = $"pg-cancel-{orderId:N}"
            }, default));
        var settleTask = Attempt(() => new SettleSellerOrderCommandHandler(settleContext)
            .Handle(Settlement(sellerId, orderId, 100m), default));

        var results = await Task.WhenAll(cancelTask, settleTask);
        var cancelOutcome = results[0];
        var settleOutcome = results[1];

        Assert.NotEqual(cancelOutcome.Succeeded, settleOutcome.Succeeded);

        await using var verify = new WalletDbContext(options);
        var buyer = await verify.Wallets.SingleAsync(wallet => wallet.UserId == buyerId);
        var seller = await verify.Wallets.SingleAsync(wallet => wallet.UserId == sellerId);
        var escrow = await verify.OrderEscrows.SingleAsync(item => item.OrderId == orderId);

        Assert.Equal(0m, escrow.RemainingAmount);
        if (cancelOutcome.Succeeded)
        {
            Assert.Contains(settleOutcome.ErrorCode, new[] { "ORDER_ESCROW_CANCELLED", "ORDER_ESCROW_INSUFFICIENT" });
            Assert.Equal(OrderEscrowStatus.Cancelled, escrow.Status);
            Assert.Equal(1000m, buyer.Balance);
            Assert.Equal(0m, seller.Balance);
        }
        else
        {
            Assert.Equal("ORDER_ESCROW_ALREADY_RESOLVED", cancelOutcome.ErrorCode);
            Assert.Equal(OrderEscrowStatus.Settled, escrow.Status);
            Assert.Equal(900m, buyer.Balance);
            Assert.Equal(100m, seller.Balance);
        }
    }

    [DisposablePostgresFact]
    public async Task CancelOrderEscrow_LeavesAuctionDepositEscrowUntouched()
    {
        var (options, buyerId, _) = await Setup();
        var orderId = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
        {
            await new PayOrderWithWalletCommandHandler(context)
                .Handle(Payment(buyerId, orderId, 100m), default);
        }

        var holdId = await CaptureDeposit(options, buyerId, orderId, 50m);
        await using (var context = new WalletDbContext(options))
        {
            var cancelled = await new CancelOrderEscrowCommandHandler(context).Handle(
                new CancelOrderEscrowCommand("Order cancelled after deposit capture")
                {
                    OrderId = orderId,
                    IdempotencyKey = $"cancel-with-deposit-{orderId:N}"
                },
                default);
            Assert.Equal(100m, cancelled.ReturnedAmount);
            Assert.Equal(950m, cancelled.BalanceAfter);
        }

        await using var verify = new WalletDbContext(options);
        Assert.Equal(950m, (await verify.Wallets.SingleAsync(wallet => wallet.UserId == buyerId)).Balance);
        Assert.Equal(OrderEscrowStatus.Cancelled, (await verify.OrderEscrows.SingleAsync(item => item.OrderId == orderId)).Status);
        Assert.Equal(50m, (await verify.AuctionDepositEscrows.SingleAsync(item => item.HoldId == holdId)).RemainingAmount);
    }

    [DisposablePostgresFact]
    public async Task FailedCancelInsert_RollsBackBalanceEscrowTransactionAndAudit()
    {
        var (options, buyerId, _) = await Setup(buyerBalance: 900m);
        var orderId = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            var wallet = await setup.Wallets.SingleAsync(item => item.UserId == buyerId);
            setup.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                UserId = buyerId.ToString(),
                Type = WalletTransactionType.PurchaseDebit,
                Amount = 100m,
                Currency = wallet.Currency.ToString(),
                BalanceBefore = 1000m,
                BalanceAfter = 900m,
                Status = TransactionStatus.Success,
                ReferenceId = orderId
            });
            setup.OrderEscrows.Add(new OrderEscrow
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                WalletId = wallet.Id,
                OriginalAmount = 100m,
                RemainingAmount = 100m,
                IsDeleted = true
            });
            await setup.SaveChangesAsync();
        }

        await using (var context = new WalletDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => new CancelOrderEscrowCommandHandler(context).Handle(
                    new CancelOrderEscrowCommand("Rollback test")
                    {
                        OrderId = orderId,
                        IdempotencyKey = $"rollback-cancel-{orderId:N}"
                    },
                    default));
        }

        await using var verify = new WalletDbContext(options);
        Assert.Equal(900m, (await verify.Wallets.SingleAsync(item => item.UserId == buyerId)).Balance);
        Assert.Equal(0, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowCancellationRefund));
        Assert.Equal(0, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderEscrowCancel && item.EntityId == orderId.ToString()));
        Assert.Equal(0, await verify.AuditLogs.CountAsync(item =>
            item.Action == AuditAction.OrderEscrowReconciliation && item.UserId == buyerId.ToString()));
        Assert.Equal(1, await verify.OrderEscrows.IgnoreQueryFilters()
            .CountAsync(item => item.OrderId == orderId));
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
                Payment(buyerId, parentOrderId, 100m),
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

    private static async Task<Guid> CaptureDeposit(
        DbContextOptions<WalletDbContext> options,
        Guid buyerId,
        Guid orderId,
        decimal amount)
    {
        await using var context = new WalletDbContext(options);
        var created = await new CreateHoldCommandHandler(context).Handle(
            new CreateHoldCommand(
                buyerId,
                "AUCTION_DEPOSIT",
                Guid.NewGuid().ToString(),
                amount,
                "VND",
                null)
            {
                IdempotencyKey = $"deposit:{Guid.NewGuid():N}"
            },
            default);
        await new CaptureHoldCommandHandler(context).Handle(
            new CaptureHoldCommand("ORDER_PAYMENT", orderId, [new("SYSTEM_ESCROW", null, amount)])
            {
                HoldId = created.Hold.HoldId,
                IdempotencyKey = $"capture:{created.Hold.HoldId}"
            },
            default);
        return created.Hold.HoldId;
    }

    private static PayOrderWithWalletCommand Payment(Guid buyerId, Guid orderId, decimal amount) =>
        new(amount) { UserId = buyerId.ToString(), OrderId = orderId };

    private static SettleSellerOrderCommand Settlement(Guid sellerId, Guid orderId, decimal amount) =>
        new(sellerId.ToString(), amount) { SubOrderId = orderId };

    private static RefundOrderPaymentCommand Refund(Guid buyerId, Guid orderId, decimal amount) =>
        new(buyerId.ToString(), amount) { OrderId = orderId };

    private static async Task<AttemptResult> Attempt<T>(Func<Task<T>> operation)
    {
        try
        {
            await operation();
            return new AttemptResult(true, null);
        }
        catch (BusinessRuleValidationException exception)
        {
            return new AttemptResult(false, exception.ErrorCode);
        }
    }

    private sealed record AttemptResult(bool Succeeded, string? ErrorCode);
}
