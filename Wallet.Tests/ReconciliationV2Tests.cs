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

        public List<DepositRecon> Deposits { get; } = new();

        public Task<IReadOnlyList<DepositRecon>> GetDepositsAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DepositRecon>>(Deposits);
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

    [Theory]
    [InlineData(AuctionHoldStatus.Released, 0)]   // released before capture
    [InlineData(AuctionHoldStatus.Refunded, 0)]   // captured, then refunded (seller rejected, order cancelled): still a given-back deposit
    [InlineData(AuctionHoldStatus.Held, 1)]       // the hold is still held although the deposit says it was given back: real mismatch
    [InlineData(AuctionHoldStatus.Captured, 1)]   // money was taken although the deposit says it was given back: real mismatch
    public async Task I6_treats_a_released_deposit_as_matching_a_released_or_refunded_hold(AuctionHoldStatus holdStatus, int expectedFindings)
    {
        using var db = new TestWalletDbContext("i6-" + holdStatus);
        var wallet = AddWallet(db, 0m);
        var hold = new AuctionHold { Id = Guid.NewGuid(), WalletId = wallet.Id, OwnerUserId = wallet.UserId, Amount = 200_000m, Status = holdStatus, ReferenceType = "AUCTION_DEPOSIT", ReferenceId = Guid.NewGuid().ToString(), MetadataJson = "{}" };
        db.AuctionHolds.Add(hold);
        await db.SaveChangesAsync();
        var sources = new Sources();
        sources.Deposits.Add(new DepositRecon(Guid.NewGuid(), Guid.NewGuid(), wallet.UserId, 200_000m, "Released", hold.Id.ToString(), DateTimeOffset.UtcNow.AddMinutes(-10)));

        var findings = await RunAsync(db, sources, "I6");

        Assert.Equal(expectedFindings, findings.Count);
        if (expectedFindings > 0) Assert.Equal("I6_STATUS_MISMATCH", findings[0].Code);
    }

    private static async Task<(TestWalletDbContext Db, WalletAccount Wallet)> LedgerOnFor(string name)
    {
        var db = new TestWalletDbContext(name);
        db.LedgerSettings.Add(new LedgerSettings { Id = 1, Enabled = true, ShadowEpoch = 1, Version = 1 });
        // The ledger was switched on an hour ago and nothing has switched it since.
        db.AuditLogs.Add(new AuditLog { ServiceName = "wallet-service", Action = AuditAction.LedgerShadowSwitch, EntityName = "LedgerSettings", EntityId = "1", NewValues = "{\"Enabled\":true}", CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Balance = 0m, Currency = CurrencyCode.Vnd };
        db.Wallets.Add(wallet);
        db.LedgerPostingPending.Add(new LedgerPostingPending { WalletId = wallet.Id, LedgerEpoch = 1, LedgerSeq = 1, IsEpochOpening = true, Type = "EpochOpening", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-40) });
        await db.SaveChangesAsync();
        return (db, wallet);
    }

    private static WalletTransaction OldCredit(WalletAccount wallet, int minutesAgo, decimal amount = 100m) => new()
    {
        WalletId = wallet.Id, UserId = wallet.UserId.ToString(), Type = WalletTransactionType.AdjustmentCredit, Amount = amount, Currency = "Vnd",
        BalanceBefore = 0m, BalanceAfter = amount, Status = TransactionStatus.Success, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)
    };

    [Fact]
    public async Task I12_reports_a_successful_balance_change_that_has_no_queue_row_and_accepts_one_that_has()
    {
        var (db, wallet) = await LedgerOnFor(nameof(I12_reports_a_successful_balance_change_that_has_no_queue_row_and_accepts_one_that_has));
        using var _ = db;
        var covered = OldCredit(wallet, 20);
        var lost = OldCredit(wallet, 10);
        db.WalletTransactions.AddRange(covered, lost);
        db.LedgerPostingPending.Add(new LedgerPostingPending { WalletId = wallet.Id, LedgerEpoch = 1, LedgerSeq = 2, WalletTransactionId = covered.Id, RelatedTransactionIds = covered.Id.ToString(), Type = "AdjustmentCredit", Delta = 100m, CreatedAt = covered.CreatedAt });
        await db.SaveChangesAsync();

        var findings = await RunAsync(db, new Sources(), "I12");

        var finding = Assert.Single(findings);
        Assert.Equal("I12_MISSING_POSTING_ROW", finding.Code);
        Assert.Equal(lost.Id.ToString(), finding.ReferenceId);
    }

    [Fact]
    public async Task I12_still_sees_a_recent_transaction_when_more_than_two_thousand_older_ones_are_covered()
    {
        var (db, wallet) = await LedgerOnFor(nameof(I12_still_sees_a_recent_transaction_when_more_than_two_thousand_older_ones_are_covered));
        using var _ = db;
        // 2,050 older transactions, all with a queue row, then one recent one that lost its row. A scan that stops after the first
        // 2,000 would never reach it.
        for (var i = 0; i < 2050; i++)
        {
            var tx = OldCredit(wallet, 30);
            tx.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30).AddMilliseconds(i);
            db.WalletTransactions.Add(tx);
            db.LedgerPostingPending.Add(new LedgerPostingPending { WalletId = wallet.Id, LedgerEpoch = 1, LedgerSeq = i + 2, WalletTransactionId = tx.Id, RelatedTransactionIds = tx.Id.ToString(), Type = "AdjustmentCredit", Delta = 100m, CreatedAt = tx.CreatedAt });
        }

        var lost = OldCredit(wallet, 5);
        db.WalletTransactions.Add(lost);
        await db.SaveChangesAsync();

        var findings = await RunAsync(db, new Sources(), "I12");

        var finding = Assert.Single(findings);
        Assert.Equal(lost.Id.ToString(), finding.ReferenceId);
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
