namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Wallet.Application.Features.RefundFund.Commands.DepositRefundFund;
using Wallet.Application.Features.RefundFund.Commands.PayRefundFundCase;
using Wallet.Application.Features.Settlements.Commands.SettleOrderPayout;
using Wallet.Application.Features.Subsidies.Commands.ApproveSubsidyBudget;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;
using Wallet.Application.Features.Subsidies.Commands.HoldPlatformSubsidy;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

/// <summary>
/// Real-database checks for the V2 payout: the order lock, the escrow row locks and the unique allocation key must keep money
/// conserved when the same payout or competing spenders arrive at the same time. Needs WALLET_STRIPE_TEST_DATABASE.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public sealed class OrderSettlementPostgresTests
{
    [DisposablePostgresFact]
    public async Task ConcurrentIdenticalPayouts_CreditTheSellerOnce()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 500_000m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await Pay(options, buyerId, orderId, 230_000m);
        var command = new SettleOrderPayoutCommand("OrderPayout", orderId, subOrderId, sellerId.ToString(), 180_000m, 20_000m, 30_000m, 0m, 230_000m, 0m);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Attempt(async () =>
        {
            await using var context = new WalletDbContext(options);
            return await new SettleOrderPayoutCommandHandler(context).Handle(command, default);
        })));

        Assert.All(results, result => Assert.Null(result.Error));
        Assert.Equal(1, results.Count(result => !result.Value!.IsReplay));
        await using var verify = new WalletDbContext(options);
        Assert.Equal(180_000m, (await verify.Wallets.SingleAsync(w => w.UserId == sellerId)).Balance);
        Assert.Equal(1, await verify.OrderSettlements.CountAsync(item => item.SubOrderId == subOrderId));
        Assert.Equal(0m, (await verify.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
    }

    [DisposablePostgresFact]
    public async Task ConcurrentPayoutsOfTwoSubOrders_NeverSpendMoreThanTheEscrow()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 500_000m);
        var orderId = Guid.NewGuid();
        await Pay(options, buyerId, orderId, 300_000m);
        // Two sub-orders each want 200,000 from a 300,000 escrow: exactly one can win.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Attempt(async () =>
        {
            await using var context = new WalletDbContext(options);
            return await new SettleOrderPayoutCommandHandler(context).Handle(
                new SettleOrderPayoutCommand("OrderPayout", orderId, Guid.NewGuid(), sellerId.ToString(), 170_000m, 0m, 30_000m, 0m, 200_000m, 0m), default);
        })));

        Assert.Equal(1, attempts.Count(a => a.Error is null));
        Assert.Equal("ORDER_ESCROW_INSUFFICIENT", Assert.IsType<BusinessRuleValidationException>(attempts.Single(a => a.Error is not null).Error).ErrorCode);
        await using var verify = new WalletDbContext(options);
        Assert.Equal(100_000m, (await verify.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        Assert.Equal(170_000m, (await verify.Wallets.SingleAsync(w => w.UserId == sellerId)).Balance);
    }

    [DisposablePostgresFact]
    public async Task AuctionPayoutRacingTheDepositRefund_SpendsTheDepositExactlyOnce()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 2_000_000m);
        var orderId = Guid.NewGuid();
        var holdId = await CaptureDeposit(options, buyerId, orderId, 100_000m);
        await Pay(options, buyerId, orderId, 935_000m);
        var payout = new SettleOrderPayoutCommand("AuctionPayout", orderId, orderId, sellerId.ToString(), 950_000m, 50_000m, 35_000m, 0m, 935_000m, 100_000m);

        var attempts = await Task.WhenAll(
            Attempt<object>(async () =>
            {
                await using var context = new WalletDbContext(options);
                return await new SettleOrderPayoutCommandHandler(context).Handle(payout, default);
            }),
            Attempt<object>(async () =>
            {
                await using var context = new WalletDbContext(options);
                return await new RefundCapturedHoldCommandHandler(context).Handle(
                    new RefundCapturedHoldCommand("Order cancelled", orderId) { HoldId = holdId, IdempotencyKey = $"refund:{holdId:N}" }, default);
            }));

        // Exactly one of them spends the 100,000 deposit; the other is refused, never both.
        Assert.Equal(1, attempts.Count(a => a.Error is null));
        await using var verify = new WalletDbContext(options);
        var deposit = await verify.AuctionDepositEscrows.SingleAsync(e => e.OrderId == orderId);
        Assert.Equal(0m, deposit.RemainingAmount);
        var sellerBalance = (await verify.Wallets.SingleAsync(w => w.UserId == sellerId)).Balance;
        var refunds = await verify.WalletTransactions.CountAsync(t => t.Type == WalletTransactionType.AuctionDepositRefund && t.ReferenceId == holdId);
        if (attempts[0].Error is null)
        {
            Assert.Equal(950_000m, sellerBalance);
            Assert.Equal(0, refunds);
        }
        else
        {
            Assert.Equal(0m, sellerBalance);
            Assert.Equal(1, refunds);
        }
    }

    /// <summary>Runs the payout against another spender of the same order escrow at the same time and checks that money is conserved.</summary>
    private static async Task PayoutRacing(string name, Func<DbContextOptions<WalletDbContext>, Guid, Guid, Task<object>> other)
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 500_000m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await Pay(options, buyerId, orderId, 230_000m);
        var payout = new SettleOrderPayoutCommand("OrderPayout", orderId, subOrderId, sellerId.ToString(), 180_000m, 20_000m, 30_000m, 0m, 230_000m, 0m);

        var attempts = await Task.WhenAll(
            Attempt<object>(async () =>
            {
                await using var context = new WalletDbContext(options);
                return await new SettleOrderPayoutCommandHandler(context).Handle(payout, default);
            }),
            Attempt<object>(() => other(options, orderId, buyerId)));

        // Both outcomes are allowed (the order decides who wins), but a refusal must be a business refusal, never a lock or constraint failure.
        foreach (var attempt in attempts.Where(a => a.Error is not null))
            Assert.IsType<BusinessRuleValidationException>(attempt.Error);
        Assert.True(attempts.Any(a => a.Error is null), $"{name}: at least one of the two must succeed");

        await using var verify = new WalletDbContext(options);
        var buyerBalance = (await verify.Wallets.SingleAsync(w => w.UserId == buyerId)).Balance;
        var sellerBalance = (await verify.Wallets.SingleAsync(w => w.UserId == sellerId)).Balance;
        var escrow = await verify.OrderEscrows.SingleAsync(e => e.OrderId == orderId);
        var allocated = await verify.OrderSettlements.Where(s => s.ParentOrderId == orderId).SumAsync(s => s.PlatformFeeAmount + s.ShippingAmount);
        Assert.True(escrow.RemainingAmount >= 0m);
        // 500,000 in the world: nothing created, nothing lost, whoever won.
        Assert.Equal(500_000m, buyerBalance + sellerBalance + escrow.RemainingAmount + allocated);
    }

    [DisposablePostgresFact]
    public Task PayoutRacingAnOrderRefund_ConservesMoney() =>
        PayoutRacing("refund", async (options, orderId, buyerId) =>
        {
            await using var context = new WalletDbContext(options);
            return await new RefundOrderPaymentCommandHandler(context).Handle(
                new RefundOrderPaymentCommand(buyerId.ToString(), 230_000m, "Order refunded") { OrderId = orderId }, default);
        });

    [DisposablePostgresFact]
    public Task PayoutRacingAnEscrowCancellation_ConservesMoney() =>
        PayoutRacing("cancel", async (options, orderId, buyerId) =>
        {
            await using var context = new WalletDbContext(options);
            return await new CancelOrderEscrowCommandHandler(context).Handle(
                new CancelOrderEscrowCommand("Order cancelled before payout", buyerId.ToString(), 230_000m) { OrderId = orderId, IdempotencyKey = $"cancel-{Guid.NewGuid():N}" }, default);
        });

    [DisposablePostgresFact]
    public Task PayoutRacingAPaymentAdjustment_ConservesMoney() =>
        PayoutRacing("adjust", async (options, orderId, buyerId) =>
        {
            await using var context = new WalletDbContext(options);
            return await new AdjustOrderPaymentCommandHandler(context).Handle(
                new AdjustOrderPaymentCommand(buyerId.ToString(), -50_000m, "Partial refund of the order") { OrderId = orderId, IdempotencyKey = $"adjust-{Guid.NewGuid():N}" }, default);
        });

    [DisposablePostgresFact]
    public async Task ConcurrentApprovalsOfTwoBudgets_LeaveExactlyOneActive()
    {
        var (options, _, _) = await Setup(buyerBalance: 1m);
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        var ids = new List<Guid>();
        await using (var context = new WalletDbContext(options))
        {
            foreach (var active in await context.PlatformSubsidyBudgets.Where(b => b.Status == SubsidyBudgetStatus.Active).ToListAsync())
            {
                active.Status = SubsidyBudgetStatus.Closed;
            }

            await context.SaveChangesAsync();
            for (var i = 0; i < 2; i++)
            {
                var created = await new CreateSubsidyBudgetCommandHandler(context).Handle(
                    new CreateSubsidyBudgetCommand("Approval race " + i, 50_000m, 20_000m, 10_000m, "proof") { AdminUserId = adminA }, default);
                ids.Add(Guid.Parse(created.Id));
            }
        }

        var attempts = await Task.WhenAll(ids.Select(id => Attempt(async () =>
        {
            await using var context = new WalletDbContext(options);
            return await new ApproveSubsidyBudgetCommandHandler(context).Handle(new ApproveSubsidyBudgetCommand { BudgetId = id, AdminUserId = adminB }, default);
        })));

        Assert.Equal(1, attempts.Count(a => a.Error is null));
        Assert.Equal("SUBSIDY_ACTIVE_BUDGET_EXISTS", Assert.IsType<BusinessRuleValidationException>(attempts.Single(a => a.Error is not null).Error).ErrorCode);
        await using var verify = new WalletDbContext(options);
        Assert.Equal(1, await verify.PlatformSubsidyBudgets.CountAsync(b => b.Status == SubsidyBudgetStatus.Active));
    }

    [DisposablePostgresFact]
    public async Task ConcurrentSubsidyHolds_NeverExceedTheBudget()
    {
        var (options, _, _) = await Setup(buyerBalance: 1m);
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        Guid budgetId;
        await using (var context = new WalletDbContext(options))
        {
            // Close any budget left active by a previous test in the same database.
            foreach (var active in await context.PlatformSubsidyBudgets.Where(b => b.Status == SubsidyBudgetStatus.Active).ToListAsync())
            {
                active.Status = SubsidyBudgetStatus.Closed;
            }

            await context.SaveChangesAsync();
            var created = await new CreateSubsidyBudgetCommandHandler(context).Handle(
                new CreateSubsidyBudgetCommand("Race", 50_000m, 50_000m, 20_000m, "proof") { AdminUserId = adminA }, default);
            budgetId = Guid.Parse(created.Id);
            await new ApproveSubsidyBudgetCommandHandler(context).Handle(
                new ApproveSubsidyBudgetCommand { BudgetId = budgetId, AdminUserId = adminB }, default);
        }

        // 6 sub-orders each hold 15,000 against a 50,000 budget: at most 3 fit.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Attempt(async () =>
        {
            await using var context = new WalletDbContext(options);
            return await new HoldPlatformSubsidyCommandHandler(context).Handle(
                new HoldPlatformSubsidyCommand(Guid.NewGuid(), 15_000m) { SubOrderId = Guid.NewGuid() }, default);
        })));

        Assert.Equal(3, attempts.Count(a => a.Error is null));
        Assert.All(attempts.Where(a => a.Error is not null), a =>
            Assert.Equal("SUBSIDY_BUDGET_EXHAUSTED", Assert.IsType<BusinessRuleValidationException>(a.Error).ErrorCode));
        await using var verify = new WalletDbContext(options);
        var budget = await verify.PlatformSubsidyBudgets.SingleAsync(b => b.Id == budgetId);
        Assert.Equal(45_000m, budget.ReservedAmount);
        Assert.True(budget.ReservedAmount + budget.UsedAmount <= budget.TotalLimit);
    }

    [DisposablePostgresFact]
    public async Task TheDatabaseRejectsASettlementWhoseIdentityDoesNotHold()
    {
        var (options, _, sellerId) = await Setup();
        await using var context = new WalletDbContext(options);
        context.OrderSettlements.Add(new OrderSettlement
        {
            Id = Guid.NewGuid(),
            BusinessType = "OrderPayout",
            ParentOrderId = Guid.NewGuid(),
            SubOrderId = Guid.NewGuid(),
            SellerId = sellerId,
            SellerWalletId = Guid.NewGuid(),
            SellerReceiveAmount = 100m,
            PlatformFeeAmount = 10m,
            ShippingAmount = 5m,
            SubsidyAmount = 0m,
            OrderEscrowAmount = 100m, // 100 != 100 + 10 + 5
            DepositEscrowAmount = 0m,
            PayloadFingerprint = new string('a', 64),
            PayloadVersion = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [DisposablePostgresFact]
    public async Task ConcurrentRefundFundCases_SameSubOrder_PaysOnceAndReplaysCleanly()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 200_000m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        await Pay(options, buyerId, orderId, 100_000m);

        decimal alreadyPaidToday;
        decimal fundBefore;
        await using (var context = new WalletDbContext(options))
        {
            await new SettleOrderPayoutCommandHandler(context).Handle(
                new SettleOrderPayoutCommand("OrderPayout", orderId, subOrderId, sellerId.ToString(), 95_000m, 5_000m, 0m, 0m, 100_000m, 0m),
                default);
            await new DepositRefundFundCommandHandler(context).Handle(
                new DepositRefundFundCommand(200_000m, "pg-fund-replay") { AdminUserId = adminA },
                default);

            var dayStart = RefundFundClock.StartOfDayUtc(DateTimeOffset.UtcNow);
            alreadyPaidToday = await context.RefundFundPayouts.AsNoTracking()
                .Where(item => item.CreatedAt >= dayStart)
                .SumAsync(item => (decimal?)item.Amount) ?? 0m;
            fundBefore = (await context.Wallets.AsNoTracking().SingleAsync(w => w.UserId == RefundFundAccount.UserId)).Balance;
        }

        var refundOptions = Microsoft.Extensions.Options.Options.Create(new RefundFundOptions
        {
            Enabled = true,
            PerCaseCap = 500_000m,
            DailyCap = alreadyPaidToday + 500_000m,
            SecondApprovalThreshold = 200_000m
        });
        var command = new PayRefundFundCaseCommand(orderId, buyerId.ToString(), 100_000m, adminA.ToString(), null)
        {
            SubOrderId = subOrderId
        };

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Attempt(async () =>
        {
            await using var context = new WalletDbContext(options);
            return await new PayRefundFundCaseCommandHandler(context, refundOptions).Handle(command, default);
        })));

        Assert.All(attempts, a => Assert.Null(a.Error));
        Assert.Equal(1, attempts.Count(a => a.Value!.Outcome == "Refunded" && !a.Value.Replayed));
        Assert.Equal(3, attempts.Count(a => a.Value!.Outcome == "Refunded" && a.Value.Replayed));

        await using var verify = new WalletDbContext(options);
        Assert.Equal(1, await verify.RefundFundPayouts.CountAsync(item => item.SubOrderId == subOrderId));
        Assert.Equal(200_000m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId)).Balance);
        Assert.Equal(0m, (await verify.Wallets.AsNoTracking().SingleAsync(w => w.UserId == sellerId)).Balance);
        Assert.Equal(fundBefore - 100_000m + 95_000m,
            (await verify.Wallets.AsNoTracking().SingleAsync(w => w.UserId == RefundFundAccount.UserId)).Balance);
    }

    [DisposablePostgresFact]
    public async Task ConcurrentRefundFundCases_DifferentSubOrders_NeverExceedDailyCap()
    {
        var (options, buyerId, sellerId) = await Setup(buyerBalance: 300_000m);
        var orderId = Guid.NewGuid();
        var sub1 = Guid.NewGuid();
        var sub2 = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        await Pay(options, buyerId, orderId, 200_000m);

        decimal alreadyPaidToday;
        var dayStart = RefundFundClock.StartOfDayUtc(DateTimeOffset.UtcNow);
        await using (var context = new WalletDbContext(options))
        {
            var settlement = new SettleOrderPayoutCommandHandler(context);
            await settlement.Handle(
                new SettleOrderPayoutCommand("OrderPayout", orderId, sub1, sellerId.ToString(), 95_000m, 5_000m, 0m, 0m, 100_000m, 0m),
                default);
            await settlement.Handle(
                new SettleOrderPayoutCommand("OrderPayout", orderId, sub2, sellerId.ToString(), 95_000m, 5_000m, 0m, 0m, 100_000m, 0m),
                default);
            await new DepositRefundFundCommandHandler(context).Handle(
                new DepositRefundFundCommand(250_000m, "pg-fund-cap") { AdminUserId = adminA },
                default);

            alreadyPaidToday = await context.RefundFundPayouts.AsNoTracking()
                .Where(item => item.CreatedAt >= dayStart)
                .SumAsync(item => (decimal?)item.Amount) ?? 0m;
        }

        var refundOptions = Microsoft.Extensions.Options.Options.Create(new RefundFundOptions
        {
            Enabled = true,
            PerCaseCap = 500_000m,
            DailyCap = alreadyPaidToday + 150_000m,
            SecondApprovalThreshold = 200_000m
        });

        var attempts = await Task.WhenAll(new[] { sub1, sub2 }.Select(subId => Attempt(async () =>
        {
            await using var context = new WalletDbContext(options);
            return await new PayRefundFundCaseCommandHandler(context, refundOptions).Handle(
                new PayRefundFundCaseCommand(orderId, buyerId.ToString(), 100_000m, adminA.ToString(), null) { SubOrderId = subId },
                default);
        })));

        Assert.All(attempts, a => Assert.Null(a.Error));
        Assert.Single(attempts, a => a.Value!.Outcome == "Refunded");
        var blocked = Assert.Single(attempts, a => a.Value!.Outcome == "NeedsFinance");
        Assert.Equal("DAILY_CAP_EXCEEDED", blocked.Value!.Reason);

        await using var verify = new WalletDbContext(options);
        var paidTodayAfter = await verify.RefundFundPayouts.AsNoTracking()
            .Where(item => item.CreatedAt >= dayStart)
            .SumAsync(item => (decimal?)item.Amount) ?? 0m;
        Assert.Equal(alreadyPaidToday + 100_000m, paidTodayAfter);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(DbContextOptions<WalletDbContext> Options, Guid BuyerId, Guid SellerId)> Setup(decimal buyerBalance = 1000m)
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        context.Wallets.AddRange(
            new WalletAccount { Id = Guid.NewGuid(), UserId = buyerId, Balance = buyerBalance, Currency = CurrencyCode.Vnd },
            new WalletAccount { Id = Guid.NewGuid(), UserId = sellerId, Currency = CurrencyCode.Vnd });
        await context.SaveChangesAsync();
        return (options, buyerId, sellerId);
    }

    private static async Task Pay(DbContextOptions<WalletDbContext> options, Guid buyerId, Guid orderId, decimal amount)
    {
        await using var context = new WalletDbContext(options);
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(amount) { UserId = buyerId.ToString(), OrderId = orderId }, default);
    }

    private static async Task<Guid> CaptureDeposit(DbContextOptions<WalletDbContext> options, Guid buyerId, Guid orderId, decimal amount)
    {
        await using var context = new WalletDbContext(options);
        var created = await new CreateHoldCommandHandler(context).Handle(
            new CreateHoldCommand(buyerId, "AUCTION_DEPOSIT", Guid.NewGuid().ToString(), amount, "VND", null)
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

    private static async Task<AttemptResult<T>> Attempt<T>(Func<Task<T>> operation)
    {
        try
        {
            return new AttemptResult<T>(await operation(), null);
        }
        catch (Exception ex)
        {
            return new AttemptResult<T>(default, ex);
        }
    }

    private sealed record AttemptResult<T>(T? Value, Exception? Error);
}
