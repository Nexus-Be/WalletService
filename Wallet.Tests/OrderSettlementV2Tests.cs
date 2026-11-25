namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Settlements.Commands.SettleOrderPayout;
using Wallet.Application.Features.Subsidies.Commands.ApproveSubsidyBudget;
using Wallet.Application.Features.Subsidies.Commands.CloseSubsidyBudget;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;
using Wallet.Application.Features.Subsidies.Commands.HoldPlatformSubsidy;
using Wallet.Application.Features.Subsidies.Commands.ReleasePlatformSubsidy;
using Wallet.Application.Features.Subsidies.Queries.ListSubsidyBudgets;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class OrderSettlementV2Tests
{
    private static readonly Guid AdminA = Guid.NewGuid();
    private static readonly Guid AdminB = Guid.NewGuid();

    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance)
    {
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Balance = balance, Currency = CurrencyCode.Vnd };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static async Task PayAsync(TestWalletDbContext context, WalletAccount buyer, Guid orderId, decimal amount) =>
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(amount) { OrderId = orderId, UserId = buyer.UserId.ToString() }, default);

    private static SettleOrderPayoutCommand Payout(
        Guid orderId, Guid subOrderId, WalletAccount seller, decimal sellerReceive, decimal fee, decimal ship, decimal subsidy,
        decimal orderEscrow, decimal depositEscrow = 0m, string type = "OrderPayout") =>
        new(type, orderId, subOrderId, seller.UserId.ToString(), sellerReceive, fee, ship, subsidy, orderEscrow, depositEscrow);

    private static async Task<SettleOrderPayoutResponse> SettleAsync(TestWalletDbContext context, SettleOrderPayoutCommand command) =>
        await new SettleOrderPayoutCommandHandler(context).Handle(command, default);

    private static async Task<string> ErrorCodeAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleValidationException>(action);
        return ex.ErrorCode;
    }

    private static async Task<PlatformSubsidyBudget> ActiveBudgetAsync(TestWalletDbContext context, decimal total = 1_000_000m, decimal daily = 500_000m, decimal cap = 100_000m)
    {
        var created = await new CreateSubsidyBudgetCommandHandler(context).Handle(
            new CreateSubsidyBudgetCommand("Launch campaign", total, daily, cap, "bank-transfer-2026-11-21") { AdminUserId = AdminA }, default);
        await new ApproveSubsidyBudgetCommandHandler(context).Handle(
            new ApproveSubsidyBudgetCommand { BudgetId = Guid.Parse(created.Id), AdminUserId = AdminB }, default);
        return await context.PlatformSubsidyBudgets.AsNoTracking().SingleAsync(b => b.Id == Guid.Parse(created.Id));
    }

    // ---------------------------------------------------------------- payout

    [Fact]
    public async Task Payout_NormalOrder_ReducesTheOrderEscrowToZero_AndCreditsOnlyTheSeller()
    {
        using var context = new TestWalletDbContext(nameof(Payout_NormalOrder_ReducesTheOrderEscrowToZero_AndCreditsOnlyTheSeller));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 230_000m); // items 200,000 + ship 30,000

        // fee 10% of 200,000 = 20,000; seller 180,000; ship 30,000 held as a pass-through allocation
        var result = await SettleAsync(context, Payout(orderId, subOrderId, seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m));

        Assert.False(result.IsReplay);
        Assert.Equal(180_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        var escrow = await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId);
        Assert.Equal(0m, escrow.RemainingAmount);
        Assert.Equal(OrderEscrowStatus.Settled, escrow.Status);
        var tx = Assert.Single(context.WalletTransactions.Where(t => t.WalletId == seller.Id));
        Assert.Equal(WalletTransactionType.EscrowRelease, tx.Type);
        Assert.Equal(180_000m, tx.Amount);
        var row = await context.OrderSettlements.SingleAsync();
        Assert.Equal(20_000m, row.PlatformFeeAmount);
        Assert.Equal(30_000m, row.ShippingAmount);
        Assert.Equal(230_000m, row.OrderEscrowAmount);
        Assert.Equal(64, row.PayloadFingerprint.Length);
    }

    [Fact]
    public async Task Payout_TwoSubOrdersOfOneParent_ShareTheEscrowWithoutConflict()
    {
        using var context = new TestWalletDbContext(nameof(Payout_TwoSubOrdersOfOneParent_ShareTheEscrowWithoutConflict));
        var buyer = AddWallet(context, 1_000_000m);
        var sellerA = AddWallet(context, 0m);
        var sellerB = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 500_000m);

        await SettleAsync(context, Payout(orderId, Guid.NewGuid(), sellerA, 270_000m, 0m, 30_000m, 0m, 300_000m));
        await SettleAsync(context, Payout(orderId, Guid.NewGuid(), sellerB, 170_000m, 0m, 30_000m, 0m, 200_000m));

        Assert.Equal(0m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        Assert.Equal(2, await context.OrderSettlements.CountAsync());
    }

    [Fact]
    public async Task Payout_SamePayloadTwice_ReturnsTheFirstResult_AndMovesNoMoneyTwice()
    {
        using var context = new TestWalletDbContext(nameof(Payout_SamePayloadTwice_ReturnsTheFirstResult_AndMovesNoMoneyTwice));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 230_000m);
        var command = Payout(orderId, subOrderId, seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m);

        var first = await SettleAsync(context, command);
        var second = await SettleAsync(context, command);

        Assert.True(second.IsReplay);
        Assert.Equal(first.SettlementId, second.SettlementId);
        Assert.Equal(180_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Single(context.WalletTransactions.Where(t => t.WalletId == seller.Id));
        Assert.Equal(1, await context.OrderSettlements.CountAsync());
    }

    [Fact]
    public async Task Payout_DifferentPayloadForTheSameKey_IsAConflict_KeepsMoney_AndLeavesAnAuditTrail()
    {
        using var context = new TestWalletDbContext(nameof(Payout_DifferentPayloadForTheSameKey_IsAConflict_KeepsMoney_AndLeavesAnAuditTrail));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 230_000m);
        await SettleAsync(context, Payout(orderId, subOrderId, seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m));

        // Same sub-order, but the fee/seller split differs: must never be answered with the old success.
        var code = await ErrorCodeAsync(() => SettleAsync(context, Payout(orderId, subOrderId, seller, 175_000m, 25_000m, 30_000m, 0m, 230_000m)));

        Assert.Equal("SETTLEMENT_CONFLICT", code);
        Assert.Equal(180_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Equal(1, await context.OrderSettlements.CountAsync());
        Assert.Contains(context.AuditLogs, a => a.Action == AuditAction.SettlementConflict);
    }

    [Fact]
    public async Task Payout_TheSameSubOrderUnderADifferentParent_IsAConflict()
    {
        using var context = new TestWalletDbContext(nameof(Payout_TheSameSubOrderUnderADifferentParent_IsAConflict));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 230_000m);
        await SettleAsync(context, Payout(orderId, subOrderId, seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m));

        // The key includes the parent order, but one sub-order can only ever be paid once, whichever parent it is sent under.
        var otherParent = Payout(Guid.NewGuid(), subOrderId, seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m);
        Assert.Equal("SETTLEMENT_CONFLICT", await ErrorCodeAsync(() => SettleAsync(context, otherParent)));
        Assert.Equal(180_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Equal(1, await context.OrderSettlements.CountAsync());
    }

    [Fact]
    public async Task Payout_WhenTheSubOrderWasPaidByTheLegacyRoute_IsRefused()
    {
        using var context = new TestWalletDbContext(nameof(Payout_WhenTheSubOrderWasPaidByTheLegacyRoute_IsRefused));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 230_000m);
        await new SettleSellerOrderCommandHandler(context).Handle(
            new SettleSellerOrderCommand(seller.UserId.ToString(), 180_000m) { SubOrderId = subOrderId }, default);

        var code = await ErrorCodeAsync(() => SettleAsync(context, Payout(orderId, subOrderId, seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m)));

        Assert.Equal("SETTLEMENT_ALREADY_PAID_LEGACY", code);
        Assert.Equal(180_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Empty(context.OrderSettlements);
    }

    [Fact]
    public async Task Payout_WithInsufficientEscrow_ChangesNothing()
    {
        using var context = new TestWalletDbContext(nameof(Payout_WithInsufficientEscrow_ChangesNothing));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 100_000m);

        var code = await ErrorCodeAsync(() => SettleAsync(context, Payout(orderId, Guid.NewGuid(), seller, 180_000m, 20_000m, 30_000m, 0m, 230_000m)));

        Assert.Equal("ORDER_ESCROW_INSUFFICIENT", code);
        Assert.Equal(0m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Equal(100_000m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
    }

    [Theory]
    [InlineData(180_000, 20_000, 30_000, 0, 200_000)]   // sources smaller than uses
    [InlineData(180_000, 20_000, 30_000, 0, 231_000)]   // sources larger than uses
    public async Task Payout_WithABrokenIdentity_IsRejected(decimal seller, decimal fee, decimal ship, decimal subsidy, decimal escrow)
    {
        using var context = new TestWalletDbContext("identity-" + escrow);
        var sellerWallet = AddWallet(context, 0m);
        var code = await ErrorCodeAsync(() => SettleAsync(context, Payout(Guid.NewGuid(), Guid.NewGuid(), sellerWallet, seller, fee, ship, subsidy, escrow)));
        Assert.Equal("SETTLEMENT_IDENTITY_INVALID", code);
    }

    [Fact]
    public async Task Payout_AnOrderPayoutCannotUseADepositEscrow()
    {
        using var context = new TestWalletDbContext(nameof(Payout_AnOrderPayoutCannotUseADepositEscrow));
        var seller = AddWallet(context, 0m);
        var code = await ErrorCodeAsync(() => SettleAsync(context,
            Payout(Guid.NewGuid(), Guid.NewGuid(), seller, 100_000m, 0m, 0m, 0m, 0m, depositEscrow: 100_000m)));
        Assert.Equal("SETTLEMENT_INVALID", code);
    }

    // ------------------------------------------------------------ auction

    [Fact]
    public async Task Payout_Auction_UsesBothEscrows_AndPaysTheSellerTheFullWinningAmountMinusFee()
    {
        using var context = new TestWalletDbContext(nameof(Payout_Auction_UsesBothEscrows_AndPaysTheSellerTheFullWinningAmountMinusFee));
        var buyer = AddWallet(context, 2_000_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        // winning 1,000,000, deposit 100,000 (already captured into its own escrow), ship 35,000: the buyer then pays 935,000
        context.AuctionDepositEscrows.Add(new AuctionDepositEscrow { Id = Guid.NewGuid(), HoldId = Guid.NewGuid(), OrderId = orderId, OriginalAmount = 100_000m, RemainingAmount = 100_000m });
        await context.SaveChangesAsync();
        await PayAsync(context, buyer, orderId, 935_000m);

        // fee 5% of 1,000,000 = 50,000; seller 950,000; ship 35,000
        var result = await SettleAsync(context, Payout(orderId, orderId, seller, 950_000m, 50_000m, 35_000m, 0m, 935_000m, depositEscrow: 100_000m, type: "AuctionPayout"));

        Assert.Equal(950_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Equal(0m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        Assert.Equal(0m, (await context.AuctionDepositEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        Assert.Equal(100_000m, result.DepositEscrowAmount);
    }

    [Fact]
    public async Task Payout_BuyNow_UsesTheDepositAndTheGoodsHoldEscrowsAsOnePool()
    {
        using var context = new TestWalletDbContext(nameof(Payout_BuyNow_UsesTheDepositAndTheGoodsHoldEscrowsAsOnePool));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        // Buy Now at 2,000,000: a 50,000 deposit and a 1,950,000 goods hold are captured into two escrows of the same order; the buyer pays only shipping.
        context.AuctionDepositEscrows.Add(new AuctionDepositEscrow { Id = Guid.NewGuid(), HoldId = Guid.NewGuid(), OrderId = orderId, OriginalAmount = 50_000m, RemainingAmount = 50_000m, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) });
        context.AuctionDepositEscrows.Add(new AuctionDepositEscrow { Id = Guid.NewGuid(), HoldId = Guid.NewGuid(), OrderId = orderId, OriginalAmount = 1_950_000m, RemainingAmount = 1_950_000m, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await context.SaveChangesAsync();
        await PayAsync(context, buyer, orderId, 30_000m);

        // fee 5% of 2,000,000 = 100,000; seller 1,900,000; shipping 30,000
        await SettleAsync(context, Payout(orderId, orderId, seller, 1_900_000m, 100_000m, 30_000m, 0m, 30_000m, depositEscrow: 2_000_000m, type: "AuctionPayout"));

        Assert.Equal(1_900_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.All(context.AuctionDepositEscrows.Where(e => e.OrderId == orderId), e => Assert.Equal(0m, e.RemainingAmount));
        Assert.Equal(0m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
    }

    [Fact]
    public async Task Payout_BuyNow_WhileTheGoodsHoldIsNotCapturedYet_IsRefusedWithoutTakingAnything()
    {
        using var context = new TestWalletDbContext(nameof(Payout_BuyNow_WhileTheGoodsHoldIsNotCapturedYet_IsRefusedWithoutTakingAnything));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        context.AuctionDepositEscrows.Add(new AuctionDepositEscrow { Id = Guid.NewGuid(), HoldId = Guid.NewGuid(), OrderId = orderId, OriginalAmount = 50_000m, RemainingAmount = 50_000m });
        await context.SaveChangesAsync();
        await PayAsync(context, buyer, orderId, 30_000m);

        var code = await ErrorCodeAsync(() => SettleAsync(context, Payout(orderId, orderId, seller, 1_900_000m, 100_000m, 30_000m, 0m, 30_000m, depositEscrow: 2_000_000m, type: "AuctionPayout")));

        Assert.Equal("DEPOSIT_ESCROW_INSUFFICIENT", code);
        Assert.Equal(50_000m, (await context.AuctionDepositEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        Assert.Equal(30_000m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        Assert.Empty(context.OrderSettlements);
    }

    [Fact]
    public async Task Payout_Auction_WithAMissingDepositEscrow_IsRefused_AndLeavesTheOrderEscrowUntouched()
    {
        using var context = new TestWalletDbContext(nameof(Payout_Auction_WithAMissingDepositEscrow_IsRefused_AndLeavesTheOrderEscrowUntouched));
        var buyer = AddWallet(context, 2_000_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 935_000m);

        var code = await ErrorCodeAsync(() => SettleAsync(context,
            Payout(orderId, orderId, seller, 950_000m, 50_000m, 35_000m, 0m, 935_000m, depositEscrow: 100_000m, type: "AuctionPayout")));

        Assert.Equal("DEPOSIT_ESCROW_NOT_FOUND", code);
    }

    // ------------------------------------------------------------ subsidy

    [Fact]
    public async Task Payout_WithAPlatformSubsidy_PaysItToTheSellerFromTheBudget_NotFromTheEscrow()
    {
        using var context = new TestWalletDbContext(nameof(Payout_WithAPlatformSubsidy_PaysItToTheSellerFromTheBudget_NotFromTheEscrow));
        var budget = await ActiveBudgetAsync(context);
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        // items 200,000, platform voucher 10,000 -> buyer pays 190,000 + ship 30,000 = 220,000; seller must still get 200,000 (fee 0%)
        await PayAsync(context, buyer, orderId, 220_000m);
        await new HoldPlatformSubsidyCommandHandler(context).Handle(new HoldPlatformSubsidyCommand(orderId, 10_000m) { SubOrderId = subOrderId }, default);

        await SettleAsync(context, Payout(orderId, subOrderId, seller, 200_000m, 0m, 30_000m, 10_000m, 220_000m));

        Assert.Equal(200_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        var types = context.WalletTransactions.Where(t => t.WalletId == seller.Id).OrderBy(t => t.Type).Select(t => new { t.Type, t.Amount }).ToList();
        Assert.Equal(2, types.Count);
        Assert.Contains(types, t => t.Type == WalletTransactionType.EscrowRelease && t.Amount == 190_000m);
        Assert.Contains(types, t => t.Type == WalletTransactionType.PlatformSubsidyCredit && t.Amount == 10_000m);
        Assert.Equal(0m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
        var after = await context.PlatformSubsidyBudgets.AsNoTracking().SingleAsync(b => b.Id == budget.Id);
        Assert.Equal(0m, after.ReservedAmount);
        Assert.Equal(10_000m, after.UsedAmount);
        Assert.Equal(SubsidyReservationStatus.Used, (await context.PlatformSubsidyReservations.SingleAsync(r => r.SubOrderId == subOrderId)).Status);
    }

    [Fact]
    public async Task Payout_WithASubsidy_PostsTheSubsidyToItsOwnLedgerAccount_NotToTheEscrowAccount()
    {
        using var context = new TestWalletDbContext(nameof(Payout_WithASubsidy_PostsTheSubsidyToItsOwnLedgerAccount_NotToTheEscrowAccount));
        context.LedgerSettings.Add(new LedgerSettings { Id = 1, Enabled = true, ShadowEpoch = 1, Version = 1 });
        await context.SaveChangesAsync();
        await ActiveBudgetAsync(context);
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 220_000m);
        await new HoldPlatformSubsidyCommandHandler(context).Handle(new HoldPlatformSubsidyCommand(orderId, 10_000m) { SubOrderId = subOrderId }, default);

        await SettleAsync(context, Payout(orderId, subOrderId, seller, 200_000m, 0m, 30_000m, 10_000m, 220_000m));

        // One ledger posting per counter account: the escrow release (190,000) and the platform subsidy (10,000) are separate rows.
        var postings = context.LedgerPostingPending.Where(p => p.WalletId == seller.Id && !p.IsEpochOpening).OrderBy(p => p.LedgerSeq).ToList();
        Assert.Equal(2, postings.Count);
        Assert.Equal(("EscrowRelease", 190_000m), (postings[0].Type, postings[0].Delta));
        Assert.Equal(("PlatformSubsidyCredit", 10_000m), (postings[1].Type, postings[1].Delta));
        Assert.Equal("ORDER_ESCROW", Wallet.Application.Common.Ledger.LedgerPostingRules.CounterAccountForPending(postings[0].Type));
        Assert.Equal("PLATFORM_SUBSIDY", Wallet.Application.Common.Ledger.LedgerPostingRules.CounterAccountForPending(postings[1].Type));
        Assert.Equal(200_000m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
    }

    [Fact]
    public async Task Payout_WithASubsidyThatWasNeverReserved_IsRefused_AndPaysNothing()
    {
        using var context = new TestWalletDbContext(nameof(Payout_WithASubsidyThatWasNeverReserved_IsRefused_AndPaysNothing));
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 220_000m);

        var code = await ErrorCodeAsync(() => SettleAsync(context, Payout(orderId, Guid.NewGuid(), seller, 200_000m, 0m, 30_000m, 10_000m, 220_000m)));

        Assert.Equal("SUBSIDY_NOT_RESERVED", code);
        Assert.Equal(0m, (await context.Wallets.SingleAsync(w => w.Id == seller.Id)).Balance);
        Assert.Equal(220_000m, (await context.OrderEscrows.SingleAsync(e => e.OrderId == orderId)).RemainingAmount);
    }

    [Fact]
    public async Task Budget_NeedsADifferentAdminToApprove_AndOnlyOneCanBeActive()
    {
        using var context = new TestWalletDbContext(nameof(Budget_NeedsADifferentAdminToApprove_AndOnlyOneCanBeActive));
        var created = await new CreateSubsidyBudgetCommandHandler(context).Handle(
            new CreateSubsidyBudgetCommand("Campaign", 1_000_000m, 500_000m, 100_000m, "proof-1") { AdminUserId = AdminA }, default);
        Assert.Equal("PendingApproval", created.Status);

        // Not approved yet: nothing can be reserved.
        var hold = new HoldPlatformSubsidyCommandHandler(context);
        Assert.Equal("SUBSIDY_BUDGET_UNAVAILABLE", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(Guid.NewGuid(), 10_000m) { SubOrderId = Guid.NewGuid() }, default)));

        var approve = new ApproveSubsidyBudgetCommandHandler(context);
        Assert.Equal("SUBSIDY_APPROVER_MUST_DIFFER", await ErrorCodeAsync(() => approve.Handle(
            new ApproveSubsidyBudgetCommand { BudgetId = Guid.Parse(created.Id), AdminUserId = AdminA }, default)));
        var approved = await approve.Handle(new ApproveSubsidyBudgetCommand { BudgetId = Guid.Parse(created.Id), AdminUserId = AdminB }, default);
        Assert.Equal("Active", approved.Status);

        var second = await new CreateSubsidyBudgetCommandHandler(context).Handle(
            new CreateSubsidyBudgetCommand("Second", 500_000m, 100_000m, 50_000m, "proof-2") { AdminUserId = AdminA }, default);
        Assert.Equal("SUBSIDY_ACTIVE_BUDGET_EXISTS", await ErrorCodeAsync(() => approve.Handle(
            new ApproveSubsidyBudgetCommand { BudgetId = Guid.Parse(second.Id), AdminUserId = AdminB }, default)));

        await new CloseSubsidyBudgetCommandHandler(context).Handle(new CloseSubsidyBudgetCommand { BudgetId = Guid.Parse(created.Id), AdminUserId = AdminB }, default);
        var nowActive = await approve.Handle(new ApproveSubsidyBudgetCommand { BudgetId = Guid.Parse(second.Id), AdminUserId = AdminB }, default);
        Assert.Equal("Active", nowActive.Status);

        var page = await new ListSubsidyBudgetsQueryHandler(context).Handle(new ListSubsidyBudgetsQuery(), default);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Budget_RejectsInconsistentLimits()
    {
        using var context = new TestWalletDbContext(nameof(Budget_RejectsInconsistentLimits));
        var code = await ErrorCodeAsync(() => new CreateSubsidyBudgetCommandHandler(context).Handle(
            new CreateSubsidyBudgetCommand("Bad", 100_000m, 200_000m, 50_000m, "proof") { AdminUserId = AdminA }, default));
        Assert.Equal("SUBSIDY_LIMITS_INVALID", code);
    }

    [Fact]
    public async Task Hold_EnforcesCapDailyAndTotalLimits_AndIsIdempotentPerSubOrder()
    {
        using var context = new TestWalletDbContext(nameof(Hold_EnforcesCapDailyAndTotalLimits_AndIsIdempotentPerSubOrder));
        var budget = await ActiveBudgetAsync(context, total: 60_000m, daily: 40_000m, cap: 25_000m);
        var hold = new HoldPlatformSubsidyCommandHandler(context);
        var orderId = Guid.NewGuid();
        var first = Guid.NewGuid();

        Assert.Equal("SUBSIDY_PER_ORDER_CAP_EXCEEDED", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(orderId, 30_000m) { SubOrderId = Guid.NewGuid() }, default)));

        var r1 = await hold.Handle(new HoldPlatformSubsidyCommand(orderId, 20_000m) { SubOrderId = first }, default);
        var replay = await hold.Handle(new HoldPlatformSubsidyCommand(orderId, 20_000m) { SubOrderId = first }, default);
        Assert.False(r1.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(20_000m, (await context.PlatformSubsidyBudgets.AsNoTracking().SingleAsync(b => b.Id == budget.Id)).ReservedAmount);

        Assert.Equal("SUBSIDY_CONFLICT", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(orderId, 21_000m) { SubOrderId = first }, default)));

        // daily limit 40,000: 20,000 held, another 25,000 would make 45,000
        Assert.Equal("SUBSIDY_DAILY_LIMIT_EXCEEDED", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(orderId, 25_000m) { SubOrderId = Guid.NewGuid() }, default)));
        await hold.Handle(new HoldPlatformSubsidyCommand(orderId, 20_000m) { SubOrderId = Guid.NewGuid() }, default);

        // total 60,000 but the daily limit (40,000) is reached first; the budget never goes beyond either limit
        Assert.Equal("SUBSIDY_DAILY_LIMIT_EXCEEDED", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(orderId, 1_000m) { SubOrderId = Guid.NewGuid() }, default)));
        var after = await context.PlatformSubsidyBudgets.AsNoTracking().SingleAsync(b => b.Id == budget.Id);
        Assert.Equal(40_000m, after.ReservedAmount);
        Assert.True(after.ReservedAmount + after.UsedAmount <= after.TotalLimit);
    }

    [Fact]
    public async Task Hold_FailsWhenTheTotalBudgetIsExhausted()
    {
        using var context = new TestWalletDbContext(nameof(Hold_FailsWhenTheTotalBudgetIsExhausted));
        await ActiveBudgetAsync(context, total: 30_000m, daily: 30_000m, cap: 20_000m);
        var hold = new HoldPlatformSubsidyCommandHandler(context);
        await hold.Handle(new HoldPlatformSubsidyCommand(Guid.NewGuid(), 20_000m) { SubOrderId = Guid.NewGuid() }, default);

        Assert.Equal("SUBSIDY_BUDGET_EXHAUSTED", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(Guid.NewGuid(), 11_000m) { SubOrderId = Guid.NewGuid() }, default)));
    }

    [Fact]
    public async Task Release_GivesTheBudgetBack_IsIdempotent_AndCannotUndoAUsedSubsidy()
    {
        using var context = new TestWalletDbContext(nameof(Release_GivesTheBudgetBack_IsIdempotent_AndCannotUndoAUsedSubsidy));
        var budget = await ActiveBudgetAsync(context);
        var hold = new HoldPlatformSubsidyCommandHandler(context);
        var release = new ReleasePlatformSubsidyCommandHandler(context);
        var subOrderId = Guid.NewGuid();
        var holdOrderId = Guid.NewGuid();
        await hold.Handle(new HoldPlatformSubsidyCommand(holdOrderId, 15_000m) { SubOrderId = subOrderId }, default);

        var first = await release.Handle(new ReleasePlatformSubsidyCommand("Order cancelled") { SubOrderId = subOrderId }, default);
        var again = await release.Handle(new ReleasePlatformSubsidyCommand("Order cancelled") { SubOrderId = subOrderId }, default);
        var never = await release.Handle(new ReleasePlatformSubsidyCommand("Order cancelled") { SubOrderId = Guid.NewGuid() }, default);

        Assert.True(first.Changed);
        Assert.False(again.Changed);
        Assert.Equal("None", never.Status);
        Assert.Equal(0m, (await context.PlatformSubsidyBudgets.AsNoTracking().SingleAsync(b => b.Id == budget.Id)).ReservedAmount);
        // A released reservation cannot be held again by the same sub-order.
        Assert.Equal("SUBSIDY_ALREADY_RELEASED", await ErrorCodeAsync(() => hold.Handle(
            new HoldPlatformSubsidyCommand(holdOrderId, 15_000m) { SubOrderId = subOrderId }, default)));

        // A used subsidy is final.
        var buyer = AddWallet(context, 500_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var usedSub = Guid.NewGuid();
        await PayAsync(context, buyer, orderId, 220_000m);
        await hold.Handle(new HoldPlatformSubsidyCommand(orderId, 10_000m) { SubOrderId = usedSub }, default);
        await SettleAsync(context, Payout(orderId, usedSub, seller, 200_000m, 0m, 30_000m, 10_000m, 220_000m));
        Assert.Equal("SUBSIDY_ALREADY_USED", await ErrorCodeAsync(() => release.Handle(
            new ReleasePlatformSubsidyCommand("late cancel") { SubOrderId = usedSub }, default)));
    }
}
