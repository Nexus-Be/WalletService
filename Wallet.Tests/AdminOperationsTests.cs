namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.Features.AdminWallets.Commands.ResolveStripeAttempt;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminHolds;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminWallets;
using Wallet.Application.Features.AdminWallets.Queries.ListStripeReconciliationRequired;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class AdminOperationsTests
{
    [Fact]
    public async Task ListAdminWallets_AndListAdminHolds_FilterAndPaginateCorrectly()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var w1 = new WalletAccount { Id = Guid.NewGuid(), UserId = u1, Balance = 100_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var w2 = new WalletAccount { Id = Guid.NewGuid(), UserId = u2, Balance = 200_000m, LockedBalance = 50_000m, Currency = CurrencyCode.Vnd, Status = "FROZEN" };
        context.Wallets.AddRange(w1, w2);
        context.AuctionHolds.Add(new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = w2.Id,
            OwnerUserId = u2,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "dep-1",
            Amount = 50_000m,
            MetadataJson = "{\"auctionId\":\"auc-123\"}",
            Status = AuctionHoldStatus.Held
        });
        await context.SaveChangesAsync();

        var walletsPage = await new ListAdminWalletsQueryHandler(context)
            .Handle(new ListAdminWalletsQuery(1, 20, Status: "Frozen", Search: u2.ToString()), default);
        var walletRow = Assert.Single(walletsPage.Items);
        Assert.Equal("Frozen", walletRow.Status);
        Assert.Equal(u2.ToString(), walletRow.UserId);

        var holdsPage = await new ListAdminHoldsQueryHandler(context)
            .Handle(new ListAdminHoldsQuery(1, 20, UserId: u2, Status: "Held"), default);
        var holdRow = Assert.Single(holdsPage.Items);
        Assert.Equal("auc-123", holdRow.AuctionId);
        Assert.Equal(50_000m, holdRow.Amount);
    }

    [Fact]
    public async Task ResolveStripeAttempt_MarksFailedWithoutCreditingWallet()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 100_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 50_000m, Status = TopupStatus.Pending };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            Purpose = StripePaymentPurpose.Topup,
            OwnerId = owner,
            WalletTopupId = topup.Id,
            Amount = 50_000m,
            RequestFingerprint = "fp",
            ProviderIdempotencyKey = "pk",
            Status = StripePaymentAttemptStatus.ReconciliationRequired,
            BlocksReplacement = true
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();

        var listed = await new ListStripeReconciliationRequiredQueryHandler(context)
            .Handle(new ListStripeReconciliationRequiredQuery(), default);
        Assert.Single(listed.Items);

        var resolved = await new ResolveStripeAttemptCommandHandler(context).Handle(
            new ResolveStripeAttemptCommand("Failed", "Confirmed unpaid in Stripe dashboard")
            {
                AttemptId = attempt.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal("Failed", resolved.Status);
        Assert.False(resolved.BlocksReplacement);
        Assert.Equal(TopupStatus.Failed, topup.Status);
        Assert.Equal(100_000m, wallet.Balance);
        Assert.Empty(context.WalletTransactions);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.StripeAttemptResolve));
    }

    [Fact]
    public async Task ReconcilePendingTopups_AlsoReconcilesPendingDirectOrders()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 0m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var directOrder = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = orderId, Amount = 80_000m, Status = TopupStatus.Pending };
        var tx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 80_000m,
            Currency = "Vnd",
            BalanceBefore = 0m,
            BalanceAfter = 0m,
            ReferenceId = orderId,
            Status = TransactionStatus.Pending
        };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            Purpose = StripePaymentPurpose.DirectOrder,
            OwnerId = owner,
            DirectPaymentOrderId = directOrder.Id,
            Amount = 80_000m,
            RequestFingerprint = "fp",
            ProviderIdempotencyKey = "pk",
            CheckoutSessionId = "cs_test_direct_1",
            Status = StripePaymentAttemptStatus.AwaitingPayment,
            BlocksReplacement = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.Add(directOrder);
        context.WalletTransactions.Add(tx);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();

        var provider = new PaidDirectProvider(attempt);
        var count = await new ReconcilePendingTopupsCommandHandler(
            context, provider, NullLogger<ReconcilePendingTopupsCommandHandler>.Instance)
            .Handle(new ReconcilePendingTopupsCommand(), default);

        Assert.Equal(1, count);
        Assert.Equal(TopupStatus.Success, directOrder.Status);
        Assert.Equal(TransactionStatus.Success, tx.Status);
    }

    [Fact]
    public async Task AdjustOrderPayment_SupportsPositiveAndNegativeDeltas_Idempotency_AndEscrowSettlement()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var buyer = new WalletAccount { Id = Guid.NewGuid(), UserId = buyerId, Balance = 500_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        context.Wallets.Add(buyer);
        await context.SaveChangesAsync();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(200_000m) { OrderId = orderId, UserId = buyerId.ToString() },
            default);

        var adjustHandler = new AdjustOrderPaymentCommandHandler(context);
        var plus = await adjustHandler.Handle(
            new AdjustOrderPaymentCommand(buyerId.ToString(), 30_000m, "Address change increased shipping")
            { OrderId = orderId, IdempotencyKey = "adj-1" },
            default);
        var replay = await adjustHandler.Handle(
            new AdjustOrderPaymentCommand(buyerId.ToString(), 30_000m, "Address change increased shipping")
            { OrderId = orderId, IdempotencyKey = "adj-1" },
            default);

        Assert.Equal(plus.Id, replay.Id);
        Assert.Equal(270_000m, buyer.Balance);

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            adjustHandler.Handle(
                new AdjustOrderPaymentCommand(buyerId.ToString(), 40_000m, "Different")
                { OrderId = orderId, IdempotencyKey = "adj-1" },
                default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", conflict.ErrorCode);

        var minus = await adjustHandler.Handle(
            new AdjustOrderPaymentCommand(buyerId.ToString(), -10_000m, "Shipping promo applied")
            { OrderId = orderId, IdempotencyKey = "adj-2" },
            default);
        Assert.Equal("OrderPaymentAdjustmentRefund", minus.Type);
        Assert.Equal(280_000m, buyer.Balance);

        // Adjusted escrow is now 220,000; settle 120,000 and refund 100,000 cleanly.
        await new SettleSellerOrderCommandHandler(context).Handle(
            new SettleSellerOrderCommand(sellerId.ToString(), 120_000m) { SubOrderId = orderId },
            default);
        var refund = await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyerId.ToString(), 100_000m, "Partial cancel") { OrderId = orderId },
            default);
        Assert.Equal(380_000m, refund.BalanceAfter);
    }

    private sealed class PaidDirectProvider(StripePaymentAttempt attempt) : IStripeCheckoutClient
    {
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken token) =>
            Task.FromResult(new StripeSessionSnapshot(
                sessionId,
                "complete",
                "paid",
                decimal.ToInt64(attempt.Amount),
                "pi_test_direct_1",
                attempt.Id.ToString("D"),
                attempt.Id.ToString("D"),
                false));

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken token) =>
            Task.FromResult(new StripePaymentIntentSnapshot(
                paymentIntentId,
                "succeeded",
                decimal.ToInt64(attempt.Amount),
                decimal.ToInt64(attempt.Amount),
                attempt.Id.ToString("D")));
    }
}
