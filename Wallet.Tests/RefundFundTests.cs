namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.Application.Common;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.RefundFund.Commands.DepositRefundFund;
using Wallet.Application.Features.RefundFund.Commands.PayRefundFundCase;
using Wallet.Application.Features.RefundFund.Queries.GetRefundFundStatus;
using Wallet.Application.Features.Settlements.Commands.SettleOrderPayout;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

/// <summary>
/// T30 phase 1: a buyer is refunded from the refund fund after the order was paid out to the seller. The fund only pays what was paid out
/// by wallet, once per sub-order, within its caps, with a second admin above the threshold; it takes back from the seller only what is
/// available; whatever it cannot pay or recover is answered, never forced.
/// </summary>
public sealed class RefundFundTests
{
    private static readonly Guid AdminA = Guid.NewGuid();
    private static readonly Guid AdminB = Guid.NewGuid();

    private static IOptions<RefundFundOptions> Options(bool enabled = true, decimal perCase = 500_000m, decimal daily = 5_000_000m, decimal threshold = 200_000m) =>
        Microsoft.Extensions.Options.Options.Create(new RefundFundOptions { Enabled = enabled, PerCaseCap = perCase, DailyCap = daily, SecondApprovalThreshold = threshold });

    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance, Guid? userId = null)
    {
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = userId ?? Guid.NewGuid(), Balance = balance, Currency = CurrencyCode.Vnd };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private sealed record Sale(Guid OrderId, Guid SubOrderId, WalletAccount Buyer, WalletAccount Seller, decimal Amount);

    /// <summary>A sub-order paid by wallet and already paid out to the seller (escrow gone, seller credited).</summary>
    private static async Task<Sale> PaidOutSaleAsync(TestWalletDbContext context, decimal amount = 100_000m)
    {
        var buyer = AddWallet(context, amount + 50_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(amount) { OrderId = orderId, UserId = buyer.UserId.ToString() }, default);
        var fee = Math.Round(amount * 0.05m);
        var ship = 0m;
        await new SettleOrderPayoutCommandHandler(context).Handle(
            new SettleOrderPayoutCommand("OrderPayout", orderId, subOrderId, seller.UserId.ToString(), amount - fee - ship, fee, ship, 0m, amount, 0m), default);
        return new Sale(orderId, subOrderId, buyer, seller, amount);
    }

    private static PayRefundFundCaseCommand Case(Sale sale, decimal? amount = null, Guid? first = null, Guid? second = null) =>
        new(sale.OrderId, sale.Buyer.UserId.ToString(), amount ?? sale.Amount, (first ?? AdminA).ToString(), second?.ToString()) { SubOrderId = sale.SubOrderId };

    private static async Task FundAsync(TestWalletDbContext context, decimal amount) =>
        await new DepositRefundFundCommandHandler(context).Handle(new DepositRefundFundCommand(amount, "bank-transfer-2026-11-25") { AdminUserId = AdminA }, default);

    private static async Task<RefundFundCaseResult> PayAsync(TestWalletDbContext context, PayRefundFundCaseCommand command, IOptions<RefundFundOptions>? options = null) =>
        await new PayRefundFundCaseCommandHandler(context, options ?? Options()).Handle(command, default);

    private static decimal Balance(TestWalletDbContext context, Guid userId) =>
        context.Wallets.AsNoTracking().Single(wallet => wallet.UserId == userId).Balance;

    private static async Task<string> ErrorCodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<BusinessRuleValidationException>(action)).ErrorCode;

    // ---------------------------------------------------------------- the refund

    [Fact]
    public async Task A_paid_out_sub_order_is_refunded_from_the_fund_and_the_seller_gives_back_what_they_received()
    {
        using var context = new TestWalletDbContext(nameof(A_paid_out_sub_order_is_refunded_from_the_fund_and_the_seller_gives_back_what_they_received));
        var sale = await PaidOutSaleAsync(context);                       // buyer 50k left, seller 95k
        await FundAsync(context, 1_000_000m);

        var result = await PayAsync(context, Case(sale));

        Assert.Equal("Refunded", result.Outcome);
        Assert.False(result.Replayed);
        Assert.Equal(95_000m, result.SellerRecovered);                    // the seller's share of this sub-order, all of it available
        Assert.Equal(0m, result.SellerShortfall);
        Assert.Equal(150_000m, Balance(context, sale.Buyer.UserId));      // 50k left + 100k refunded
        Assert.Equal(0m, Balance(context, sale.Seller.UserId));
        Assert.Equal(1_000_000m - 100_000m + 95_000m, Balance(context, RefundFundAccount.UserId));
        var payout = await context.RefundFundPayouts.AsNoTracking().SingleAsync();
        Assert.Equal(sale.SubOrderId, payout.SubOrderId);
        Assert.Equal(AdminA, payout.FirstApproverId);
    }

    [Fact]
    public async Task The_seller_gives_back_only_the_available_balance_and_never_locked_funds()
    {
        using var context = new TestWalletDbContext(nameof(The_seller_gives_back_only_the_available_balance_and_never_locked_funds));
        var sale = await PaidOutSaleAsync(context);                       // seller has 95k
        var seller = await context.Wallets.SingleAsync(wallet => wallet.Id == sale.Seller.Id);
        seller.LockedBalance = 60_000m;                                   // locked for something else (a withdrawal or an auction hold)
        await context.SaveChangesAsync(default);
        await FundAsync(context, 1_000_000m);

        var result = await PayAsync(context, Case(sale));

        Assert.Equal("Refunded", result.Outcome);
        Assert.Equal(35_000m, result.SellerRecovered);                    // 95k - 60k locked
        Assert.Equal(60_000m, result.SellerShortfall);                    // left for a person to handle
        Assert.Equal(60_000m, context.Wallets.AsNoTracking().Single(wallet => wallet.Id == sale.Seller.Id).LockedBalance);
        Assert.Equal(60_000m, Balance(context, sale.Seller.UserId));      // nothing negative, locked funds intact
    }

    [Fact]
    public async Task A_seller_with_nothing_available_gives_back_nothing_and_the_whole_share_is_the_shortfall()
    {
        using var context = new TestWalletDbContext(nameof(A_seller_with_nothing_available_gives_back_nothing_and_the_whole_share_is_the_shortfall));
        var sale = await PaidOutSaleAsync(context);
        var seller = await context.Wallets.SingleAsync(wallet => wallet.Id == sale.Seller.Id);
        seller.Balance = 0m;                                              // the seller already withdrew everything
        await context.SaveChangesAsync(default);
        await FundAsync(context, 1_000_000m);

        var result = await PayAsync(context, Case(sale));

        Assert.Equal("Refunded", result.Outcome);
        Assert.Equal(0m, result.SellerRecovered);
        Assert.Equal(95_000m, result.SellerShortfall);
        Assert.Equal(150_000m, Balance(context, sale.Buyer.UserId));
    }

    [Fact]
    public async Task Asking_again_returns_the_same_refund_and_moves_no_money()
    {
        using var context = new TestWalletDbContext(nameof(Asking_again_returns_the_same_refund_and_moves_no_money));
        var sale = await PaidOutSaleAsync(context);
        await FundAsync(context, 1_000_000m);
        var first = await PayAsync(context, Case(sale));
        var buyerAfter = Balance(context, sale.Buyer.UserId);
        var fundAfter = Balance(context, RefundFundAccount.UserId);

        var again = await PayAsync(context, Case(sale));

        Assert.True(again.Replayed);
        Assert.Equal(first.BuyerTransactionId, again.BuyerTransactionId);
        Assert.Equal(buyerAfter, Balance(context, sale.Buyer.UserId));
        Assert.Equal(fundAfter, Balance(context, RefundFundAccount.UserId));
        Assert.Equal(1, await context.RefundFundPayouts.CountAsync());
        Assert.Equal("REFUND_FUND_CONFLICT", await ErrorCodeAsync(() => PayAsync(context, Case(sale, amount: 90_000m))));
    }

    // ---------------------------------------------------------------- what stops it

    [Fact]
    public async Task The_fund_is_off_by_default()
    {
        using var context = new TestWalletDbContext(nameof(The_fund_is_off_by_default));
        var sale = await PaidOutSaleAsync(context);
        await FundAsync(context, 1_000_000m);

        Assert.Equal("REFUND_FUND_DISABLED", await ErrorCodeAsync(() => PayAsync(context, Case(sale), Options(enabled: false))));
        Assert.False(new RefundFundOptions().Enabled);
        Assert.Equal(150_000m - 100_000m, Balance(context, sale.Buyer.UserId));
    }

    [Fact]
    public async Task An_empty_fund_pays_nothing_and_the_case_is_answered_as_needs_finance()
    {
        using var context = new TestWalletDbContext(nameof(An_empty_fund_pays_nothing_and_the_case_is_answered_as_needs_finance));
        var sale = await PaidOutSaleAsync(context);
        await FundAsync(context, 40_000m);                                // less than the 100k to refund

        var result = await PayAsync(context, Case(sale));

        Assert.Equal("NeedsFinance", result.Outcome);
        Assert.Equal("FUND_INSUFFICIENT", result.Reason);
        Assert.Equal(40_000m, Balance(context, RefundFundAccount.UserId));
        Assert.Equal(50_000m, Balance(context, sale.Buyer.UserId));       // nothing moved
        Assert.Equal(0, await context.RefundFundPayouts.CountAsync());
    }

    [Fact]
    public async Task A_case_above_the_per_case_cap_is_not_paid()
    {
        using var context = new TestWalletDbContext(nameof(A_case_above_the_per_case_cap_is_not_paid));
        var sale = await PaidOutSaleAsync(context);
        await FundAsync(context, 1_000_000m);

        var result = await PayAsync(context, Case(sale), Options(perCase: 99_999m));

        Assert.Equal("NeedsFinance", result.Outcome);
        Assert.Equal("PER_CASE_CAP_EXCEEDED", result.Reason);
        Assert.Equal(1_000_000m, Balance(context, RefundFundAccount.UserId));
    }

    [Fact]
    public async Task The_daily_cap_counts_what_the_fund_already_paid_today()
    {
        using var context = new TestWalletDbContext(nameof(The_daily_cap_counts_what_the_fund_already_paid_today));
        var first = await PaidOutSaleAsync(context);
        var second = await PaidOutSaleAsync(context);
        await FundAsync(context, 1_000_000m);
        var options = Options(daily: 150_000m);

        Assert.Equal("Refunded", (await PayAsync(context, Case(first), options)).Outcome);
        var blocked = await PayAsync(context, Case(second), options);

        Assert.Equal("NeedsFinance", blocked.Outcome);
        Assert.Equal("DAILY_CAP_EXCEEDED", blocked.Reason);
        var status = await new GetRefundFundStatusQueryHandler(context, options).Handle(new GetRefundFundStatusQuery(), default);
        Assert.Equal(100_000m, status.PaidToday);
        Assert.Equal(50_000m, status.RemainingToday);
    }

    [Fact]
    public async Task Above_the_threshold_a_second_different_admin_is_required()
    {
        using var context = new TestWalletDbContext(nameof(Above_the_threshold_a_second_different_admin_is_required));
        var sale = await PaidOutSaleAsync(context, amount: 300_000m);
        await FundAsync(context, 2_000_000m);
        var options = Options(threshold: 200_000m);

        Assert.Equal("SECOND_APPROVAL_REQUIRED", await ErrorCodeAsync(() => PayAsync(context, Case(sale), options)));
        Assert.Equal("SECOND_APPROVAL_REQUIRED", await ErrorCodeAsync(() => PayAsync(context, Case(sale, second: AdminA), options)));
        Assert.Equal(0, await context.RefundFundPayouts.CountAsync());

        var result = await PayAsync(context, Case(sale, second: AdminB), options);

        Assert.Equal("Refunded", result.Outcome);
        Assert.Equal(AdminB, (await context.RefundFundPayouts.AsNoTracking().SingleAsync()).SecondApproverId);
    }

    [Fact]
    public async Task A_sub_order_that_was_not_paid_out_cannot_use_the_fund()
    {
        using var context = new TestWalletDbContext(nameof(A_sub_order_that_was_not_paid_out_cannot_use_the_fund));
        var buyer = AddWallet(context, 100_000m);
        var orderId = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(new PayOrderWithWalletCommand(50_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() }, default);
        await FundAsync(context, 1_000_000m);
        var notSettled = new Sale(orderId, Guid.NewGuid(), buyer, AddWallet(context, 0m), 50_000m);

        Assert.Equal("SUB_ORDER_NOT_SETTLED", await ErrorCodeAsync(() => PayAsync(context, Case(notSettled))));
    }

    [Fact]
    public async Task Only_the_whole_amount_the_buyer_paid_is_refunded()
    {
        using var context = new TestWalletDbContext(nameof(Only_the_whole_amount_the_buyer_paid_is_refunded));
        var sale = await PaidOutSaleAsync(context);
        await FundAsync(context, 1_000_000m);

        Assert.Equal("PARTIAL_REFUND_NOT_SUPPORTED", await ErrorCodeAsync(() => PayAsync(context, Case(sale, amount: 60_000m))));
        Assert.Equal(0, await context.RefundFundPayouts.CountAsync());
    }

    [Fact]
    public async Task An_order_that_was_already_refunded_by_another_route_is_not_refunded_again()
    {
        using var context = new TestWalletDbContext(nameof(An_order_that_was_already_refunded_by_another_route_is_not_refunded_again));
        var sale = await PaidOutSaleAsync(context);
        context.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = sale.Buyer.Id, Type = WalletTransactionType.RefundCredit, Amount = 100_000m, ReferenceId = sale.OrderId,
            Status = TransactionStatus.Success, UserId = sale.Buyer.UserId.ToString(), Currency = "Vnd"
        });
        await context.SaveChangesAsync(default);
        await FundAsync(context, 1_000_000m);

        var result = await PayAsync(context, Case(sale));

        Assert.Equal("NeedsFinance", result.Outcome);
        Assert.Equal("ALREADY_REFUNDED_OR_UNATTRIBUTABLE", result.Reason);
        Assert.Equal(1_000_000m, Balance(context, RefundFundAccount.UserId));
    }

    [Theory]
    [InlineData("2026-11-25T03:00:00+00:00", "2026-11-24T17:00:00+00:00")]   // 10:00 in Vietnam: today started at 00:00 +07 = 17:00 UTC the day before
    [InlineData("2026-11-25T18:00:00+00:00", "2026-11-25T17:00:00+00:00")]   // 01:00 on the 26th in Vietnam: a new day has started
    public void The_day_starts_at_midnight_in_Vietnam_and_is_given_in_UTC_which_PostgreSQL_accepts(string now, string expected)
    {
        var start = RefundFundClock.StartOfDayUtc(DateTimeOffset.Parse(now));

        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(DateTimeOffset.Parse(expected), start);
    }

    // ---------------------------------------------------------------- the fund and the ledger

    [Fact]
    public async Task An_admin_funds_the_refund_fund_and_the_balance_shows_it()
    {
        using var context = new TestWalletDbContext(nameof(An_admin_funds_the_refund_fund_and_the_balance_shows_it));

        var result = await new DepositRefundFundCommandHandler(context).Handle(new DepositRefundFundCommand(250_000m, "bank-transfer-1") { AdminUserId = AdminA }, default);

        Assert.Equal(250_000m, result.FundBalance);
        var status = await new GetRefundFundStatusQueryHandler(context, Options()).Handle(new GetRefundFundStatusQuery(), default);
        Assert.Equal(250_000m, status.Balance);
        Assert.True(status.Enabled);
        Assert.Equal("FORBIDDEN", await ErrorCodeAsync(() => new DepositRefundFundCommandHandler(context).Handle(new DepositRefundFundCommand(1m, "x") { AdminUserId = Guid.Empty }, default)));
    }

    [Theory]
    [InlineData("RefundFundDeposit", "REFUND_FUND_FUNDING")]
    [InlineData("RefundFundPayout", "REFUND_FUND_CLEARING")]
    [InlineData("RefundFundRefund", "REFUND_FUND_CLEARING")]
    [InlineData("SellerRecoveryDebit", "REFUND_FUND_CLEARING")]
    [InlineData("RefundFundRecovery", "REFUND_FUND_CLEARING")]
    public void The_new_transaction_types_have_their_own_ledger_accounts(string type, string account)
    {
        Assert.Equal(account, LedgerPostingRules.CounterAccount(type));
        Assert.True(LedgerPostingRules.Accounts.ContainsKey(account));
    }

    [Fact]
    public async Task Full_seller_recovery_records_separate_shadow_ledger_entries_for_payout_and_recovery()
    {
        using var context = new TestWalletDbContext(nameof(Full_seller_recovery_records_separate_shadow_ledger_entries_for_payout_and_recovery));
        var buyer = AddWallet(context, 150_000m);
        var seller = AddWallet(context, 0m);
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() }, default);
        await new SettleOrderPayoutCommandHandler(context).Handle(
            new SettleOrderPayoutCommand("OrderPayout", orderId, subOrderId, seller.UserId.ToString(), 100_000m, 0m, 0m, 0m, 100_000m, 0m), default);
        await FundAsync(context, 1_000_000m);

        var fundWallet = await context.Wallets.SingleAsync(wallet => wallet.UserId == RefundFundAccount.UserId);
        context.LedgerSettings.Add(new LedgerSettings { Id = 1, Enabled = true, ShadowEpoch = 1, Version = 1 });
        await context.SaveChangesAsync(default);

        var sale = new Sale(orderId, subOrderId, buyer, seller, 100_000m);
        var result = await PayAsync(context, Case(sale));

        Assert.Equal("Refunded", result.Outcome);
        Assert.Equal(100_000m, result.SellerRecovered);
        Assert.Equal(0m, result.SellerShortfall);
        Assert.Equal(1_000_000m, result.FundBalance);

        var payout = await context.RefundFundPayouts.AsNoTracking().SingleAsync();
        var fundRows = await context.LedgerPostingPending.AsNoTracking()
            .Where(row => row.WalletId == fundWallet.Id && !row.IsEpochOpening)
            .OrderBy(row => row.LedgerSeq)
            .ToListAsync();
        Assert.Equal(2, fundRows.Count);
        Assert.Equal(nameof(WalletTransactionType.RefundFundPayout), fundRows[0].Type);
        Assert.Equal(-100_000m, fundRows[0].Delta);
        Assert.Equal(payout.FundTransactionId, fundRows[0].WalletTransactionId);
        Assert.Equal(nameof(WalletTransactionType.RefundFundRecovery), fundRows[1].Type);
        Assert.Equal(100_000m, fundRows[1].Delta);
        Assert.Equal(payout.RecoveryTransactionId, fundRows[1].WalletTransactionId);

        var buyerRow = await context.LedgerPostingPending.AsNoTracking()
            .SingleAsync(row => row.WalletId == buyer.Id && !row.IsEpochOpening);
        Assert.Equal(nameof(WalletTransactionType.RefundFundRefund), buyerRow.Type);
        Assert.Equal(100_000m, buyerRow.Delta);

        var sellerRow = await context.LedgerPostingPending.AsNoTracking()
            .SingleAsync(row => row.WalletId == seller.Id && !row.IsEpochOpening);
        Assert.Equal(nameof(WalletTransactionType.SellerRecoveryDebit), sellerRow.Type);
        Assert.Equal(-100_000m, sellerRow.Delta);
    }

    [Fact]
    public async Task A_frozen_buyer_still_receives_a_refund_from_the_fund_while_a_frozen_fund_cannot_pay()
    {
        using var context = new TestWalletDbContext(nameof(A_frozen_buyer_still_receives_a_refund_from_the_fund_while_a_frozen_fund_cannot_pay));
        var frozenBuyerSale = await PaidOutSaleAsync(context);
        var activeBuyerSale = await PaidOutSaleAsync(context);
        await FundAsync(context, 1_000_000m);

        var buyerWallet = await context.Wallets.SingleAsync(wallet => wallet.UserId == frozenBuyerSale.Buyer.UserId);
        buyerWallet.Freeze();
        await context.SaveChangesAsync(default);

        var refunded = await PayAsync(context, Case(frozenBuyerSale));
        Assert.Equal("Refunded", refunded.Outcome);
        Assert.Equal(150_000m, Balance(context, frozenBuyerSale.Buyer.UserId));

        var fundWallet = await context.Wallets.SingleAsync(wallet => wallet.UserId == RefundFundAccount.UserId);
        fundWallet.Freeze();
        await context.SaveChangesAsync(default);

        Assert.Equal("WALLET_FROZEN", await ErrorCodeAsync(() => PayAsync(context, Case(activeBuyerSale))));
    }
}
