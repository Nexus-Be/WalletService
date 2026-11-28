namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.AdminWallets.Commands.ResolveStripeAttempt;
using Wallet.Application.Features.AdminWallets.Queries.GetWithdrawalHistory;
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

    [Fact]
    public async Task ReconcilePendingTopups_PromotesStaleCreationPendingToReconciliationRequired_ForAdminResolution()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var staleOrderId = Guid.NewGuid();
        var freshOrderId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 100_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var staleDirect = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = staleOrderId, Amount = 60_000m, Status = TopupStatus.Pending };
        var freshDirect = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = freshOrderId, Amount = 60_000m, Status = TopupStatus.Pending };
        var staleTx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 60_000m,
            Currency = "Vnd",
            BalanceBefore = 100_000m,
            BalanceAfter = 100_000m,
            ReferenceId = staleOrderId,
            Status = TransactionStatus.Pending
        };
        var staleAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            Purpose = StripePaymentPurpose.DirectOrder,
            OwnerId = owner,
            DirectPaymentOrderId = staleDirect.Id,
            Amount = 60_000m,
            RequestFingerprint = "fp-stale",
            ProviderIdempotencyKey = "pk-stale",
            CheckoutSessionId = null,
            Status = StripePaymentAttemptStatus.CreationPending,
            BlocksReplacement = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15)
        };
        var freshAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            Purpose = StripePaymentPurpose.DirectOrder,
            OwnerId = owner,
            DirectPaymentOrderId = freshDirect.Id,
            Amount = 60_000m,
            RequestFingerprint = "fp-fresh",
            ProviderIdempotencyKey = "pk-fresh",
            CheckoutSessionId = null,
            Status = StripePaymentAttemptStatus.CreationPending,
            BlocksReplacement = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.AddRange(staleDirect, freshDirect);
        context.WalletTransactions.Add(staleTx);
        context.StripePaymentAttempts.AddRange(staleAttempt, freshAttempt);
        await context.SaveChangesAsync();

        var promoted = await new ReconcilePendingTopupsCommandHandler(
                context,
                new PaidDirectProvider(staleAttempt),
                NullLogger<ReconcilePendingTopupsCommandHandler>.Instance)
            .Handle(new ReconcilePendingTopupsCommand(), default);

        Assert.Equal(1, promoted);
        Assert.Equal(StripePaymentAttemptStatus.ReconciliationRequired, staleAttempt.Status);
        Assert.Equal(StripePaymentAttemptStatus.CreationPending, freshAttempt.Status);

        var resolved = await new ResolveStripeAttemptCommandHandler(context).Handle(
            new ResolveStripeAttemptCommand("Failed", "Session creation timed out before Stripe session existed")
            {
                AttemptId = staleAttempt.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal("Failed", resolved.Status);
        Assert.False(resolved.BlocksReplacement);
        Assert.Equal(TopupStatus.Failed, staleDirect.Status);
        Assert.Equal(TransactionStatus.Failed, staleTx.Status);
    }

    [Fact]
    public async Task ResolveStripeAttempt_DirectOrderExpiredInvalidStateAndNotFound_WorkAsExpected()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 100_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var directOrder = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = orderId, Amount = 75_000m, Status = TopupStatus.Pending };
        var directTx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 75_000m,
            Currency = "Vnd",
            BalanceBefore = 100_000m,
            BalanceAfter = 100_000m,
            ReferenceId = orderId,
            Status = TransactionStatus.Pending
        };
        var reconAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            Purpose = StripePaymentPurpose.DirectOrder,
            OwnerId = owner,
            DirectPaymentOrderId = directOrder.Id,
            Amount = 75_000m,
            RequestFingerprint = "fp-recon",
            ProviderIdempotencyKey = "pk-recon",
            Status = StripePaymentAttemptStatus.ReconciliationRequired,
            BlocksReplacement = true
        };
        var awaitingAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            Purpose = StripePaymentPurpose.Topup,
            OwnerId = owner,
            WalletTopupId = Guid.NewGuid(),
            Amount = 50_000m,
            RequestFingerprint = "fp-awaiting",
            ProviderIdempotencyKey = "pk-awaiting",
            Status = StripePaymentAttemptStatus.AwaitingPayment,
            BlocksReplacement = true
        };
        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.Add(directOrder);
        context.WalletTransactions.Add(directTx);
        context.StripePaymentAttempts.AddRange(reconAttempt, awaitingAttempt);
        await context.SaveChangesAsync();

        var handler = new ResolveStripeAttemptCommandHandler(context);
        var adminId = Guid.NewGuid().ToString();

        var expired = await handler.Handle(
            new ResolveStripeAttemptCommand("Expired", "Session expired without capture")
            {
                AttemptId = reconAttempt.Id,
                AdminUserId = adminId
            },
            default);
        Assert.Equal("Expired", expired.Status);
        Assert.Equal(TopupStatus.Expired, directOrder.Status);
        Assert.Equal(TransactionStatus.Failed, directTx.Status);

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(
                new ResolveStripeAttemptCommand("Failed", "Still awaiting")
                {
                    AttemptId = awaitingAttempt.Id,
                    AdminUserId = adminId
                },
                default));
        Assert.Equal("INVALID_ATTEMPT_STATE", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);

        var notFound = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(
                new ResolveStripeAttemptCommand("Failed", "Missing attempt")
                {
                    AttemptId = Guid.NewGuid(),
                    AdminUserId = adminId
                },
                default));
        Assert.Equal("PAYMENT_ATTEMPT_NOT_FOUND", notFound.ErrorCode);
    }

    [Fact]
    public async Task ListAdminWallets_FiltersByUserIdSearchAndPaginates_AndHistoryUnknownThrows404()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var u1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var u2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var u3 = Guid.Parse("33333333-3333-3333-3333-333333333333");
        context.Wallets.AddRange(
            new WalletAccount { Id = Guid.NewGuid(), UserId = u1, Balance = 10_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3) },
            new WalletAccount { Id = Guid.NewGuid(), UserId = u2, Balance = 20_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new WalletAccount { Id = Guid.NewGuid(), UserId = u3, Balance = 30_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await context.SaveChangesAsync();

        var query = new ListAdminWalletsQueryHandler(context);
        var byUser = await query.Handle(new ListAdminWalletsQuery(1, 20, UserId: u2), default);
        Assert.Equal(u2.ToString(), Assert.Single(byUser.Items).UserId);

        var bySearch = await query.Handle(new ListAdminWalletsQuery(1, 20, Search: "22222222"), default);
        Assert.Equal(u2.ToString(), Assert.Single(bySearch.Items).UserId);

        var pageTwo = await query.Handle(new ListAdminWalletsQuery(Page: 2, PageSize: 1), default);
        Assert.Equal(3, pageTwo.Pagination.TotalCount);
        Assert.Equal(3, pageTwo.Pagination.TotalPages);
        Assert.True(pageTwo.Pagination.HasPreviousPage);
        Assert.True(pageTwo.Pagination.HasNextPage);
        Assert.Equal(u2.ToString(), Assert.Single(pageTwo.Items).UserId);

        var historyErr = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new GetWithdrawalHistoryQueryHandler(context).Handle(new GetWithdrawalHistoryQuery(Guid.NewGuid()), default));
        Assert.Equal("WITHDRAWAL_NOT_FOUND", historyErr.ErrorCode);
    }

    [Fact]
    public async Task AdjustWalletBalance_EnforcesAvailableBalanceAgainstLocks_WorksOnFrozenAndValidatesReason()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid().ToString();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 100_000m,
            LockedBalance = 60_000m,
            Currency = CurrencyCode.Vnd,
            Status = "FROZEN"
        };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();

        var handler = new AdjustWalletBalanceCommandHandler(context);

        // AvailableBalance is 40,000 even though total Balance is 100,000.
        var insufficient = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            handler.Handle(
                new AdjustWalletBalanceCommand(50_000m, "Debit", "Fee clawback")
                {
                    UserId = userId,
                    AdminUserId = adminId
                },
                default));
        Assert.Equal("INSUFFICIENT_BALANCE", insufficient.ErrorCode);

        var emptyReasonErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(
                new AdjustWalletBalanceCommand(10_000m, "Credit", "   ")
                {
                    UserId = userId,
                    AdminUserId = adminId
                },
                default));
        Assert.Equal("VAL_INVALID_INPUT", emptyReasonErr.ErrorCode);

        // Administrative adjustments can still remediate frozen wallets (chargeback reversal/recovery).
        var frozenCredit = await handler.Handle(
            new AdjustWalletBalanceCommand(25_000m, "Credit", "Dispute settled")
            {
                UserId = userId,
                AdminUserId = adminId
            },
            default);
        Assert.Equal(125_000m, frozenCredit.BalanceAfter);
        Assert.Equal(65_000m, frozenCredit.AvailableBalance);

        var audit = Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.BalanceAdjustment));
        Assert.Contains("\"AdjustmentType\":\"Credit\"", audit.NewValues);
        Assert.Contains("\"Reason\":\"Dispute settled\"", audit.NewValues);
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
