namespace Wallet.Tests;

using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class OrderEscrowAccountingTests
{
    [Fact]
    public async Task PartialPayoutAndRefund_ConserveWalletPaymentEscrow()
    {
        using var context = new TestWalletDbContext(nameof(PartialPayoutAndRefund_ConserveWalletPaymentEscrow));
        var buyer = AddWallet(context, balance: 1000m);
        var seller = AddWallet(context, balance: 0m);
        var orderId = Guid.NewGuid();

        var payment = await new PayOrderWithWalletCommandHandler(context).Handle(
            Payment(buyer.UserId, orderId, 100m),
            default);
        var payout = await new SettleSellerOrderCommandHandler(context).Handle(
            Settlement(seller.UserId, orderId, 60m),
            default);
        var refund = await new RefundOrderPaymentCommandHandler(context).Handle(
            Refund(buyer.UserId, orderId, 40m),
            default);

        Assert.Equal(940m, buyer.Balance);
        Assert.Equal(60m, seller.Balance);
        Assert.Equal(0m, context.OrderEscrows.Single(item => item.OrderId == orderId).RemainingAmount);
        Assert.NotEqual(payment.Id, payout.Id);
        Assert.NotEqual(payout.Id, refund.Id);
        Assert.Single(context.WalletTransactions.Where(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.PurchaseDebit));
        Assert.Single(context.WalletTransactions.Where(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowRelease));
        Assert.Single(context.WalletTransactions.Where(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.RefundCredit));
        Assert.Equal(1, context.AuditLogs.Count(item => item.Action == AuditAction.OrderPayment));
        Assert.Equal(1, context.AuditLogs.Count(item => item.Action == AuditAction.OrderSettlement));
        Assert.Equal(1, context.AuditLogs.Count(item => item.Action == AuditAction.OrderRefund));
    }

    [Fact]
    public async Task PayoutAboveRemainingEscrow_DoesNotCreditSeller()
    {
        using var context = new TestWalletDbContext(nameof(PayoutAboveRemainingEscrow_DoesNotCreditSeller));
        var buyer = AddWallet(context, balance: 1000m);
        var seller = AddWallet(context, balance: 0m);
        var orderId = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            Payment(buyer.UserId, orderId, 100m),
            default);

        var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => new SettleSellerOrderCommandHandler(context)
                .Handle(Settlement(seller.UserId, orderId, 101m), default));

        Assert.Equal("ORDER_ESCROW_INSUFFICIENT", error.ErrorCode);
        Assert.Equal(900m, buyer.Balance);
        Assert.Equal(0m, seller.Balance);
        Assert.Equal(100m, context.OrderEscrows.Single(item => item.OrderId == orderId).RemainingAmount);
        Assert.Empty(context.WalletTransactions.Where(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.EscrowRelease));
        Assert.Empty(context.AuditLogs.Where(item => item.Action == AuditAction.OrderSettlement));
    }

    [Fact]
    public async Task HistoricalPaymentWithoutResolution_IsReconciledBeforeSettlement()
    {
        using var context = new TestWalletDbContext(
            nameof(HistoricalPaymentWithoutResolution_IsReconciledBeforeSettlement));
        var buyer = AddWallet(context, balance: 900m);
        var seller = AddWallet(context, balance: 0m);
        var orderId = Guid.NewGuid();
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = buyer.Id,
            UserId = buyer.UserId.ToString(),
            Type = WalletTransactionType.PurchaseDebit,
            Amount = 100m,
            Currency = buyer.Currency.ToString(),
            BalanceBefore = 1000m,
            BalanceAfter = 900m,
            Status = TransactionStatus.Success,
            ReferenceId = orderId
        });
        context.SaveChanges();

        await new SettleSellerOrderCommandHandler(context).Handle(
            Settlement(seller.UserId, orderId, 40m),
            default);

        Assert.Equal(60m, context.OrderEscrows.Single(item => item.OrderId == orderId).RemainingAmount);
        Assert.Equal(40m, seller.Balance);
        Assert.Single(context.AuditLogs.Where(item => item.Action == AuditAction.OrderEscrowReconciliation));
    }

    [Fact]
    public async Task UnmappedMarketplaceChildOperations_KeepTheirLegacyBehavior()
    {
        using var context = new TestWalletDbContext(nameof(UnmappedMarketplaceChildOperations_KeepTheirLegacyBehavior));
        var buyer = AddWallet(context, balance: 100m);
        var seller = AddWallet(context, balance: 0m);
        var childOrderId = Guid.NewGuid();

        await new SettleSellerOrderCommandHandler(context).Handle(
            Settlement(seller.UserId, childOrderId, 40m),
            default);
        await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 20m)
            {
                OrderId = childOrderId,
                IsSubOrder = true
            },
            default);

        Assert.Equal(120m, buyer.Balance);
        Assert.Equal(40m, seller.Balance);
        Assert.Empty(context.OrderEscrows);
        Assert.Single(context.WalletTransactions.Where(item =>
            item.ReferenceId == childOrderId && item.Type == WalletTransactionType.EscrowRelease));
        Assert.Single(context.WalletTransactions.Where(item =>
            item.ReferenceId == childOrderId && item.Type == WalletTransactionType.RefundCredit));
    }

    [Fact]
    public async Task MultiSellerSubOrderSettlements_AndSubOrderRefund_RespectParentOrderEscrow()
    {
        using var context = new TestWalletDbContext(
            nameof(MultiSellerSubOrderSettlements_AndSubOrderRefund_RespectParentOrderEscrow));
        var buyer = AddWallet(context, balance: 1000m);
        var seller1 = AddWallet(context, balance: 0m);
        var seller2 = AddWallet(context, balance: 0m);
        var seller3 = AddWallet(context, balance: 0m);
        var parentOrderId = Guid.NewGuid();
        var sub1 = Guid.NewGuid();
        var sub2 = Guid.NewGuid();
        var sub3 = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            Payment(buyer.UserId, parentOrderId, 100m),
            default);

        var settleHandler = new SettleSellerOrderCommandHandler(context);
        var s1 = await settleHandler.Handle(
            new SettleSellerOrderCommand(seller1.UserId.ToString(), 40m, parentOrderId) { SubOrderId = sub1 },
            default);
        var s2 = await settleHandler.Handle(
            new SettleSellerOrderCommand(seller2.UserId.ToString(), 40m, parentOrderId) { SubOrderId = sub2 },
            default);

        Assert.NotEqual(s1.Id, s2.Id);
        Assert.Equal(40m, seller1.Balance);
        Assert.Equal(40m, seller2.Balance);
        Assert.Equal(20m, context.OrderEscrows.Single(item => item.OrderId == parentOrderId).RemainingAmount);

        var overdraw = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            settleHandler.Handle(
                new SettleSellerOrderCommand(seller3.UserId.ToString(), 30m, parentOrderId) { SubOrderId = sub3 },
                default));
        Assert.Equal("ORDER_ESCROW_INSUFFICIENT", overdraw.ErrorCode);
        Assert.Equal(0m, seller3.Balance);

        var subRefund = await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString().ToUpperInvariant(), 20m, "Sub-order 3 cancelled", parentOrderId)
            {
                OrderId = sub3,
                IsSubOrder = true
            },
            default);
        Assert.Equal(920m, subRefund.BalanceAfter);

        var escrow = context.OrderEscrows.Single(item => item.OrderId == parentOrderId);
        Assert.Equal(0m, escrow.RemainingAmount);
        Assert.Equal(OrderEscrowStatus.Refunded, escrow.Status);
    }

    [Fact]
    public async Task SubOrderSettlementAndRefund_WithUnknownParentOrderId_Return404()
    {
        using var context = new TestWalletDbContext(
            nameof(SubOrderSettlementAndRefund_WithUnknownParentOrderId_Return404));
        var buyer = AddWallet(context, balance: 100m);
        var seller = AddWallet(context, balance: 0m);
        var unknownParent = Guid.NewGuid();

        var settleNotFound = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new SettleSellerOrderCommandHandler(context).Handle(
                new SettleSellerOrderCommand(seller.UserId.ToString(), 40m, unknownParent)
                {
                    SubOrderId = Guid.NewGuid()
                },
                default));
        Assert.Equal(404, settleNotFound.StatusCode);
        Assert.Equal("PAYMENT_NOT_FOUND", settleNotFound.ErrorCode);

        var refundNotFound = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new RefundOrderPaymentCommandHandler(context).Handle(
                new RefundOrderPaymentCommand(buyer.UserId.ToString(), 20m, "Missing parent", unknownParent)
                {
                    OrderId = Guid.NewGuid(),
                    IsSubOrder = true
                },
                default));
        Assert.Equal(404, refundNotFound.StatusCode);
        Assert.Equal("PAYMENT_NOT_FOUND", refundNotFound.ErrorCode);
    }

    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = balance,
            Currency = CurrencyCode.Vnd
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static PayOrderWithWalletCommand Payment(Guid buyerId, Guid orderId, decimal amount) =>
        new(amount) { UserId = buyerId.ToString(), OrderId = orderId };

    private static SettleSellerOrderCommand Settlement(Guid sellerId, Guid orderId, decimal amount) =>
        new(sellerId.ToString(), amount) { SubOrderId = orderId };

    private static RefundOrderPaymentCommand Refund(Guid buyerId, Guid orderId, decimal amount) =>
        new(buyerId.ToString(), amount) { OrderId = orderId };
}
