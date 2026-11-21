namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Settlements.Commands.SettleOrderPayout;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Tests.Support;
using Xunit;

/// <summary>I3 and I4 after the V2 payout: new orders must reconcile cleanly, old (legacy) payouts must be classified, real drift must still be reported.</summary>
public sealed class ReconciliationV2Tests
{
    private sealed class Sources : IReconciliationSourceClient
    {
        public List<OrderRecon> Orders { get; } = new();

        public Task<IReadOnlyList<OrderRecon>> GetOrdersAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrderRecon>>(Orders);

        public Task<IReadOnlyList<DepositRecon>> GetDepositsAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DepositRecon>>(Array.Empty<DepositRecon>());
    }

    private static WalletAccount AddWallet(TestWalletDbContext db, decimal balance)
    {
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Balance = balance, Currency = CurrencyCode.Vnd };
        db.Wallets.Add(wallet);
        db.SaveChanges();
        return wallet;
    }

    private static async Task Pay(TestWalletDbContext db, WalletAccount buyer, Guid orderId, decimal amount) =>
        await new PayOrderWithWalletCommandHandler(db).Handle(new PayOrderWithWalletCommand(amount) { OrderId = orderId, UserId = buyer.UserId.ToString() }, default);

    private static OrderRecon CompletedOrder(Guid orderId, Guid subId, decimal fee = 20_000m, decimal seller = 180_000m) =>
        new(orderId, Guid.NewGuid(), "Completed", 230_000m, DateTimeOffset.UtcNow.AddMinutes(-10),
            new[] { new SubOrderRecon(subId, "Completed", "Wallet", 200_000m, 0m, 0m, 0m, 30_000m, 230_000m, seller, fee, true, DateTimeOffset.UtcNow.AddMinutes(-20)) },
            Array.Empty<RefundRecon>());

    private static async Task<List<ReconciliationFinding>> RunAsync(TestWalletDbContext db, Sources sources, params string[] invariants)
    {
        await new ReconciliationEvaluator(db, sources).RunAsync(new ReconciliationRequest(null, null, invariants), null, default);
        return await db.ReconciliationFindings.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task A_v2_order_payout_reconciles_with_no_findings()
    {
        using var db = new TestWalletDbContext(nameof(A_v2_order_payout_reconciles_with_no_findings));
        var buyer = AddWallet(db, 500_000m);
        var seller = AddWallet(db, 0m);
        var orderId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        await Pay(db, buyer, orderId, 230_000m);
        await new SettleOrderPayoutCommandHandler(db).Handle(
            new SettleOrderPayoutCommand("OrderPayout", orderId, subId, seller.UserId.ToString(), 180_000m, 20_000m, 30_000m, 0m, 230_000m, 0m), default);
        var sources = new Sources();
        sources.Orders.Add(CompletedOrder(orderId, subId));

        Assert.Empty(await RunAsync(db, sources, "I3", "I4"));
    }

    [Fact]
    public async Task A_v2_payout_with_a_platform_subsidy_reconciles_with_no_findings()
    {
        using var db = new TestWalletDbContext(nameof(A_v2_payout_with_a_platform_subsidy_reconciles_with_no_findings));
        var buyer = AddWallet(db, 500_000m);
        var seller = AddWallet(db, 0m);
        var orderId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        await Pay(db, buyer, orderId, 220_000m);
        db.PlatformSubsidyBudgets.Add(new PlatformSubsidyBudget
        {
            Id = Guid.NewGuid(), Name = "c", TotalLimit = 100_000m, DailyLimit = 50_000m, PerOrderCap = 20_000m, EvidenceRef = "p",
            Status = SubsidyBudgetStatus.Active, CreatedByAdminId = Guid.NewGuid(), ApprovedByAdminId = Guid.NewGuid(), ApprovedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var budget = await db.PlatformSubsidyBudgets.SingleAsync();
        db.PlatformSubsidyReservations.Add(new PlatformSubsidyReservation { Id = Guid.NewGuid(), BudgetId = budget.Id, SubOrderId = subId, OrderId = orderId, Amount = 10_000m });
        budget.ReservedAmount = 10_000m;
        await db.SaveChangesAsync();
        await new SettleOrderPayoutCommandHandler(db).Handle(
            new SettleOrderPayoutCommand("OrderPayout", orderId, subId, seller.UserId.ToString(), 200_000m, 0m, 30_000m, 10_000m, 220_000m, 0m), default);
        var sources = new Sources();
        sources.Orders.Add(new OrderRecon(orderId, Guid.NewGuid(), "Completed", 220_000m, DateTimeOffset.UtcNow.AddMinutes(-10),
            new[] { new SubOrderRecon(subId, "Completed", "Wallet", 200_000m, 0m, 0m, 10_000m, 30_000m, 220_000m, 200_000m, 0m, true, DateTimeOffset.UtcNow.AddMinutes(-20)) },
            Array.Empty<RefundRecon>()));

        Assert.Empty(await RunAsync(db, sources, "I3", "I4"));
    }

    [Fact]
    public async Task A_v2_allocation_that_disagrees_with_the_order_is_reported_by_i4()
    {
        using var db = new TestWalletDbContext(nameof(A_v2_allocation_that_disagrees_with_the_order_is_reported_by_i4));
        var buyer = AddWallet(db, 500_000m);
        var seller = AddWallet(db, 0m);
        var orderId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        await Pay(db, buyer, orderId, 230_000m);
        await new SettleOrderPayoutCommandHandler(db).Handle(
            new SettleOrderPayoutCommand("OrderPayout", orderId, subId, seller.UserId.ToString(), 180_000m, 20_000m, 30_000m, 0m, 230_000m, 0m), default);
        var sources = new Sources();
        // Commerce says the fee was 25,000 and the seller should have received 175,000.
        sources.Orders.Add(CompletedOrder(orderId, subId, fee: 25_000m, seller: 175_000m));

        var codes = (await RunAsync(db, sources, "I4")).Select(f => f.Code).ToList();

        Assert.Contains("I4_ALLOCATION_MISMATCH", codes);
        Assert.Contains("I4_AMOUNT_MISMATCH", codes);
    }

    [Fact]
    public async Task A_legacy_payout_that_never_reduced_the_escrow_is_classified_not_reported_as_a_mismatch()
    {
        using var db = new TestWalletDbContext(nameof(A_legacy_payout_that_never_reduced_the_escrow_is_classified_not_reported_as_a_mismatch));
        var buyer = AddWallet(db, 500_000m);
        var seller = AddWallet(db, 0m);
        var orderId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        await Pay(db, buyer, orderId, 230_000m);
        // The legacy route as Commerce called it: no parent order id, so the parent escrow stays whole.
        await new SettleSellerOrderCommandHandler(db).Handle(new SettleSellerOrderCommand(seller.UserId.ToString(), 180_000m) { SubOrderId = subId }, default);
        var sources = new Sources();
        sources.Orders.Add(CompletedOrder(orderId, subId));

        var findings = await RunAsync(db, sources, "I3");

        var finding = Assert.Single(findings);
        Assert.Equal("I3_LEGACY_ESCROW_NOT_REDUCED", finding.Code);
        Assert.Equal("Low", finding.Severity);
    }

    [Fact]
    public async Task A_real_escrow_drift_after_a_v2_payout_is_still_a_high_severity_mismatch()
    {
        using var db = new TestWalletDbContext(nameof(A_real_escrow_drift_after_a_v2_payout_is_still_a_high_severity_mismatch));
        var buyer = AddWallet(db, 500_000m);
        var seller = AddWallet(db, 0m);
        var orderId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        await Pay(db, buyer, orderId, 300_000m);
        await new SettleOrderPayoutCommandHandler(db).Handle(
            new SettleOrderPayoutCommand("OrderPayout", orderId, subId, seller.UserId.ToString(), 180_000m, 20_000m, 30_000m, 0m, 230_000m, 0m), default);
        var escrow = await db.OrderEscrows.SingleAsync();
        escrow.RemainingAmount = 10_000m; // should be 70,000
        await db.SaveChangesAsync();
        var sources = new Sources();
        sources.Orders.Add(CompletedOrder(orderId, subId));

        var finding = Assert.Single(await RunAsync(db, sources, "I3"));

        Assert.Equal("I3_REMAINING_MISMATCH", finding.Code);
        Assert.Equal("High", finding.Severity);
    }

    [Fact]
    public async Task An_auction_payout_is_checked_from_the_wallets_own_allocation_record()
    {
        using var db = new TestWalletDbContext(nameof(An_auction_payout_is_checked_from_the_wallets_own_allocation_record));
        var buyer = AddWallet(db, 2_000_000m);
        var seller = AddWallet(db, 0m);
        var orderId = Guid.NewGuid();
        db.AuctionDepositEscrows.Add(new AuctionDepositEscrow { Id = Guid.NewGuid(), HoldId = Guid.NewGuid(), OrderId = orderId, OriginalAmount = 100_000m, RemainingAmount = 100_000m });
        await db.SaveChangesAsync();
        await Pay(db, buyer, orderId, 935_000m);
        await new SettleOrderPayoutCommandHandler(db).Handle(
            new SettleOrderPayoutCommand("AuctionPayout", orderId, orderId, seller.UserId.ToString(), 950_000m, 50_000m, 35_000m, 0m, 935_000m, 100_000m), default);
        var settlement = await db.OrderSettlements.SingleAsync();
        settlement.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10); // old enough to be judged
        await db.SaveChangesAsync();

        Assert.Empty(await RunAsync(db, new Sources(), "I3", "I4"));

        var credit = await db.WalletTransactions.SingleAsync(tx => tx.Type == WalletTransactionType.EscrowRelease);
        credit.Amount = 900_000m;
        var escrow = await db.OrderEscrows.SingleAsync();
        escrow.RemainingAmount = 5_000m;
        escrow.Status = OrderEscrowStatus.Active;
        await db.SaveChangesAsync();

        var codes = (await RunAsync(db, new Sources(), "I3", "I4")).Select(f => f.Code).ToList();
        Assert.Contains("I4_AUCTION_AMOUNT_MISMATCH", codes);
        Assert.Contains("I3_REMAINING_MISMATCH", codes);
    }
}
