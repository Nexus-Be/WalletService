namespace Wallet.Tests;

using Wallet.Application.Features.Stripe;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeSettlementTests
{
    [Fact]
    public async Task PaidTopup_ReplayAndDifferentEvent_CreditOnce()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.Settled, await Apply(context, attempt, session, intent, "evt_1"));
        Assert.Equal(StripeSettlementOutcome.DuplicateEvent, await Apply(context, attempt, session, intent, "evt_1"));
        Assert.Equal(StripeSettlementOutcome.AlreadySettled, await Apply(context, attempt, session, intent, "evt_2"));

        Assert.Equal(100100m, wallet.Balance);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Equal(StripePaymentAttemptStatus.Successful, attempt.Status);
        var transaction = Assert.Single(context.WalletTransactions);
        Assert.Equal(WalletTransactionType.TopupStripe, transaction.Type);
        Assert.Equal(topup.Id, transaction.ReferenceId);
        Assert.Equal(100m, transaction.BalanceBefore);
        Assert.Equal(100100m, transaction.BalanceAfter);
        Assert.Equal(2, context.StripeWebhookEvents.Count());
        Assert.Single(context.AuditLogs);
    }

    [Fact]
    public async Task Settlement_RefreshesPreviouslyTrackedWalletAfterLocking()
    {
        var database = Guid.NewGuid().ToString();
        using var seed = new TestWalletDbContext(database);
        var (wallet, topup, attempt) = await SeedTopup(seed);
        using var other = new TestWalletDbContext(database);
        _ = other.Wallets.Single(item => item.Id == wallet.Id);
        wallet.Balance = 500;
        await seed.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.Settled, await Apply(other, attempt, session, intent, "evt_refresh"));
        Assert.Equal(100500m, other.Wallets.Single(item => item.Id == wallet.Id).Balance);
        Assert.Equal(500m, other.WalletTransactions.Single(item => item.ReferenceId == topup.Id).BalanceBefore);
    }

    [Fact]
    public async Task Settlement_RejectsUnsavedChangesWithoutDiscardingThem()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        var unrelatedWallet = Wallet(Guid.NewGuid());
        context.Wallets.Add(unrelatedWallet);
        await context.SaveChangesAsync();
        unrelatedWallet.Balance = 300m;
        var (session, intent) = Snapshots(attempt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(context, attempt, session, intent, "evt_dirty"));
        Assert.Equal(300m, unrelatedWallet.Balance);
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Empty(context.StripeWebhookEvents);
        Assert.Empty(context.WalletTransactions);
    }

    [Fact]
    public async Task UnpaidAndMismatchedSnapshots_DoNotCredit()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.Pending,
            await Apply(context, attempt, session with { PaymentStatus = "unpaid" }, intent with { Status = "processing" }, "evt_pending"));
        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired,
            await Apply(context, attempt, session with { AmountVnd = 1 }, intent, "evt_mismatch"));
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Equal(StripePaymentAttemptStatus.ReconciliationRequired, attempt.Status);
        Assert.Empty(context.WalletTransactions);
    }

    [Fact]
    public async Task SecondPaidAttempt_ForSettledParent_IsEscalated()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, first) = await SeedTopup(context);
        var (session, intent) = Snapshots(first);
        await Apply(context, first, session, intent, "evt_1");
        var second = Attempt(first.OwnerId, topup.Id);
        second.CheckoutSessionId = "cs_test_456";
        context.StripePaymentAttempts.Add(second);
        await context.SaveChangesAsync();
        var (secondSession, secondIntent) = Snapshots(second, "pi_456");

        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired,
            await Apply(context, second, secondSession, secondIntent, "evt_2"));
        Assert.Equal(100100m, wallet.Balance);
        Assert.Single(context.WalletTransactions);
        Assert.Equal(StripePaymentAttemptStatus.ReconciliationRequired, second.Status);
    }

    [Fact]
    public async Task PreviouslyEscalatedAttempt_IsNotAutomaticallySettled()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        attempt.Status = StripePaymentAttemptStatus.ReconciliationRequired;
        await context.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired,
            await Apply(context, attempt, session, intent, "evt_escalated"));
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Empty(context.WalletTransactions);
    }

    [Fact]
    public async Task LateSuccessAfterExpiry_SettlesUnpaidParent()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        topup.Status = TopupStatus.Expired;
        attempt.Status = StripePaymentAttemptStatus.Expired;
        await context.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.Settled, await Apply(context, attempt, session, intent, "evt_late"));
        Assert.Equal(100100m, wallet.Balance);
        Assert.Equal(TopupStatus.Success, topup.Status);
    }

    [Fact]
    public async Task ConfirmedExpiredTopup_ReleasesAttemptAndRecordsFailure()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.FinalizedFailure, await Apply(context, attempt,
            session with { SessionStatus = "expired", PaymentStatus = "unpaid" },
            intent with { Status = "canceled", AmountReceived = 0 }, "evt_expired"));
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(TopupStatus.Expired, topup.Status);
        Assert.Equal(StripePaymentAttemptStatus.Expired, attempt.Status);
        Assert.False(attempt.BlocksReplacement);
        Assert.Empty(context.WalletTransactions);
        Assert.Single(context.StripeWebhookEvents);
        Assert.Single(context.AuditLogs);
    }

    [Fact]
    public async Task ExpiryWithRecoveryEnabled_DoesNotReleaseAttempt()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, topup, attempt) = await SeedTopup(context);
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.Pending, await Apply(context, attempt,
            session with { SessionStatus = "expired", PaymentStatus = "unpaid", RecoveryEnabled = true },
            intent with { Status = "canceled", AmountReceived = 0 }, "evt_recovery"));
        Assert.True(attempt.BlocksReplacement);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Empty(context.StripeWebhookEvents);
    }

    [Fact]
    public async Task OldFinalFailure_DoesNotOverwriteNewActiveAttempt()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, topup, attempt) = await SeedTopup(context);
        attempt.Status = StripePaymentAttemptStatus.Expired;
        attempt.BlocksReplacement = false;
        var replacement = Attempt(attempt.OwnerId, topup.Id);
        replacement.CheckoutSessionId = "cs_test_replacement";
        context.StripePaymentAttempts.Add(replacement);
        await context.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.FinalizedFailure, await Apply(context, attempt,
            session with { SessionStatus = "expired", PaymentStatus = "unpaid" },
            intent with { Status = "canceled", AmountReceived = 0 }, "evt_old_expiry"));
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.True(replacement.BlocksReplacement);
        Assert.Equal(StripePaymentAttemptStatus.CreationPending, replacement.Status);
    }

    [Fact]
    public async Task ExpiredWithoutPaymentIntent_CanFinalize()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, topup, attempt) = await SeedTopup(context);
        var (session, _) = Snapshots(attempt);

        var outcome = await StripeSettlement.ApplyAsync(context, attempt.Id, "evt_no_intent",
            "checkout.session.expired", session with
            {
                SessionStatus = "expired", PaymentStatus = "unpaid", PaymentIntentId = null
            }, null, default);
        Assert.Equal(StripeSettlementOutcome.FinalizedFailure, outcome);
        Assert.Equal(TopupStatus.Expired, topup.Status);
        Assert.False(attempt.BlocksReplacement);
    }

    [Fact]
    public async Task EarlierFailureAfterSuccess_DoesNotDowngradePayment()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        var (session, intent) = Snapshots(attempt);
        await Apply(context, attempt, session, intent, "evt_paid");

        var outcome = await Apply(context, attempt,
            session with { SessionStatus = "expired", PaymentStatus = "unpaid" },
            intent with { Status = "canceled", AmountReceived = 0 }, "evt_stale");
        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired, outcome);
        Assert.Equal(StripePaymentAttemptStatus.Successful, attempt.Status);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Equal(100100m, wallet.Balance);
        Assert.Single(context.WalletTransactions);
    }

    [Fact]
    public async Task SuccessfulParentWithMissingLedger_IsEscalated()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopup(context);
        topup.Status = TopupStatus.Success;
        attempt.Status = StripePaymentAttemptStatus.Successful;
        await context.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired,
            await Apply(context, attempt, session, intent, "evt_inconsistent"));
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(StripePaymentAttemptStatus.Successful, attempt.Status);
        Assert.Empty(context.WalletTransactions);
    }

    [Fact]
    public async Task DirectPaid_SettlesOrderWithoutChangingWalletBalance()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = Wallet(owner);
        var order = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Amount = 100000, Status = TopupStatus.Pending };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = order.Id, Amount = order.Amount,
            RequestFingerprint = "fingerprint", ProviderIdempotencyKey = "key",
            CheckoutSessionId = "cs_test_123"
        };
        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment, ReferenceId = order.OrderId,
            Amount = order.Amount, Status = TransactionStatus.Pending
        };
        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.Add(order);
        context.WalletTransactions.Add(transaction);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.Settled, await Apply(context, attempt, session, intent, "evt_direct"));
        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(TopupStatus.Success, order.Status);
        Assert.Equal(TransactionStatus.Success, transaction.Status);
        Assert.Single(context.WalletTransactions);
    }

    [Fact]
    public async Task DirectLateSuccess_AfterWalletPayment_IsEscalated()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = Wallet(owner);
        var order = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Amount = 100000, Status = TopupStatus.Failed
        };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = order.Id, Amount = order.Amount, Status = StripePaymentAttemptStatus.Failed,
            BlocksReplacement = false, RequestFingerprint = "fingerprint", ProviderIdempotencyKey = "key",
            CheckoutSessionId = "cs_test_123"
        };
        var direct = new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment, ReferenceId = order.OrderId,
            Amount = order.Amount, Status = TransactionStatus.Failed
        };
        var walletPayment = new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = owner.ToString(),
            Type = WalletTransactionType.PurchaseDebit, ReferenceId = order.OrderId,
            Amount = order.Amount, Status = TransactionStatus.Success
        };
        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.Add(order);
        context.WalletTransactions.AddRange(direct, walletPayment);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        var (session, intent) = Snapshots(attempt);

        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired,
            await Apply(context, attempt, session, intent, "evt_late_direct"));
        Assert.Equal(TopupStatus.Failed, order.Status);
        Assert.Equal(TransactionStatus.Failed, direct.Status);
        Assert.Equal(TransactionStatus.Success, walletPayment.Status);
        Assert.Equal(StripePaymentAttemptStatus.ReconciliationRequired, attempt.Status);
        Assert.Equal(100m, wallet.Balance);
    }

    [Theory]
    [InlineData(true, StripePaymentAttemptStatus.CreationPending)]
    [InlineData(false, StripePaymentAttemptStatus.ReconciliationRequired)]
    public async Task WalletPayment_IsBlockedByUnresolvedStripeAttempt(
        bool blocksReplacement, StripePaymentAttemptStatus attemptStatus)
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = Wallet(owner);
        wallet.Balance = 200000;
        var order = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Amount = 100000, Status = TopupStatus.Failed
        };
        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.Add(order);
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment, ReferenceId = order.OrderId,
            Amount = order.Amount, Status = TransactionStatus.Failed
        });
        context.StripePaymentAttempts.Add(new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = order.Id, Amount = order.Amount, BlocksReplacement = blocksReplacement,
            Status = attemptStatus,
            RequestFingerprint = "fingerprint", ProviderIdempotencyKey = "key"
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new PayOrderWithWalletCommandHandler(context).Handle(
                new PayOrderWithWalletCommand(100000) { UserId = owner.ToString(), OrderId = order.OrderId }, default));
        Assert.Equal("PAYMENT_ORDER_CONFLICT", exception.ErrorCode);
        Assert.Equal(200000m, wallet.Balance);
        Assert.DoesNotContain(context.WalletTransactions, item => item.Type == WalletTransactionType.PurchaseDebit);
    }

    [Fact]
    public async Task DirectInitiation_IsBlockedAfterWalletPayment()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = Wallet(owner);
        wallet.Balance = 200000;
        var orderId = Guid.NewGuid();
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100000) { UserId = owner.ToString(), OrderId = orderId }, default);

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new CheckoutDirectCommandHandler(context, new FakeStripeCheckoutClient()).Handle(
                new CheckoutDirectCommand(orderId.ToString(), 100000)
                { UserId = owner }, default));
        Assert.Equal("PAYMENT_CONFLICT", exception.ErrorCode);
        Assert.Empty(context.DirectPaymentOrders);
    }

    private static Task<StripeSettlementOutcome> Apply(TestWalletDbContext context, StripePaymentAttempt attempt,
        StripeSessionSnapshot session, StripePaymentIntentSnapshot intent, string eventId) =>
        StripeSettlement.ApplyAsync(context, attempt.Id, eventId, "checkout.session.completed", session, intent, default);

    private static async Task<(WalletAccount, WalletTopup, StripePaymentAttempt)> SeedTopup(TestWalletDbContext context)
    {
        var owner = Guid.NewGuid();
        var wallet = Wallet(owner);
        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000, Status = TopupStatus.Pending };
        var attempt = Attempt(owner, topup.Id);
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return (wallet, topup, attempt);
    }

    private static WalletAccount Wallet(Guid owner) => new()
    {
        Id = Guid.NewGuid(), UserId = owner, Balance = 100, Currency = CurrencyCode.Vnd
    };

    private static StripePaymentAttempt Attempt(Guid owner, Guid topupId) => new()
    {
        Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.Topup, WalletTopupId = topupId,
        Amount = 100000, RequestFingerprint = "fingerprint", ProviderIdempotencyKey = Guid.NewGuid().ToString(),
        CheckoutSessionId = "cs_test_123"
    };

    private static (StripeSessionSnapshot, StripePaymentIntentSnapshot) Snapshots(StripePaymentAttempt attempt, string id = "pi_123") =>
        (new(attempt.CheckoutSessionId!, "complete", "paid", 100000, id,
            attempt.Id.ToString(), attempt.Id.ToString(), false),
         new(id, "succeeded", 100000, 100000, attempt.Id.ToString()));
}
