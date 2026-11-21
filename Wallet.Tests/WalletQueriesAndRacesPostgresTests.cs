namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Commands.ExpireDirectPayment;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class WalletQueriesAndRacesPostgresTests
{
    [DisposablePostgresFact]
    public async Task AdjustOrderPayment_PositiveNegativeAndIdempotency_EnforcedByPostgresConstraints()
    {
        var (options, buyerId) = await SetupWalletAsync(500_000m);
        var orderId = Guid.NewGuid();

        await using (var payContext = new WalletDbContext(options))
        {
            await new PayOrderWithWalletCommandHandler(payContext).Handle(
                new PayOrderWithWalletCommand(200_000m) { OrderId = orderId, UserId = buyerId.ToString() },
                default);
        }

        string plusTxId;
        await using (var plusContext = new WalletDbContext(options))
        {
            var adjustHandler = new AdjustOrderPaymentCommandHandler(plusContext);
            var plus = await adjustHandler.Handle(
                new AdjustOrderPaymentCommand(buyerId.ToString(), 30_000m, "Shipping upgrade")
                {
                    OrderId = orderId,
                    IdempotencyKey = "pg-adj-1"
                },
                default);
            plusTxId = plus.Id;
        }

        await using (var replayContext = new WalletDbContext(options))
        {
            var adjustHandler = new AdjustOrderPaymentCommandHandler(replayContext);
            var replay = await adjustHandler.Handle(
                new AdjustOrderPaymentCommand(buyerId.ToString(), 30_000m, "Shipping upgrade")
                {
                    OrderId = orderId,
                    IdempotencyKey = "pg-adj-1"
                },
                default);
            Assert.Equal(plusTxId, replay.Id);

            var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                adjustHandler.Handle(
                    new AdjustOrderPaymentCommand(buyerId.ToString(), 35_000m, "Different")
                    {
                        OrderId = orderId,
                        IdempotencyKey = "pg-adj-1"
                    },
                    default));
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", conflict.ErrorCode);
            Assert.Equal(409, conflict.StatusCode);
        }

        await using (var minusContext = new WalletDbContext(options))
        {
            var adjustHandler = new AdjustOrderPaymentCommandHandler(minusContext);
            var minus = await adjustHandler.Handle(
                new AdjustOrderPaymentCommand(buyerId.ToString(), -10_000m, "Shipping discount")
                {
                    OrderId = orderId,
                    IdempotencyKey = "pg-adj-2"
                },
                default);
            Assert.Equal("OrderPaymentAdjustmentRefund", minus.Type);

            var overRefund = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                adjustHandler.Handle(
                    new AdjustOrderPaymentCommand(buyerId.ToString(), -300_000m, "Too large")
                    {
                        OrderId = orderId,
                        IdempotencyKey = "pg-adj-over"
                    },
                    default));
            Assert.Equal("ORDER_ESCROW_INSUFFICIENT", overRefund.ErrorCode);
            Assert.Equal(409, overRefund.StatusCode);
        }

        await using var verify = new WalletDbContext(options);
        var wallet = await verify.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyerId);
        var escrow = await verify.OrderEscrows.AsNoTracking().SingleAsync(e => e.OrderId == orderId);
        var txCount = await verify.WalletTransactions.AsNoTracking().CountAsync(t => t.ReferenceId == orderId);

        Assert.Equal(280_000m, wallet.Balance);
        Assert.Equal(220_000m, escrow.OriginalAmount);
        Assert.Equal(220_000m, escrow.RemainingAmount);
        Assert.Equal(OrderEscrowStatus.Active, escrow.Status);
        Assert.Equal(3, txCount);
    }

    [DisposablePostgresFact]
    public async Task DirectCheckout_CancelRetryExpireAndLateWebhook_RespectsPostgresPartialUniqueIndexes()
    {
        var (options, buyerId) = await SetupWalletAsync(100_000m);
        var orderId = Guid.NewGuid();
        var provider = new PostgresDirectProvider();

        await using (var createContext = new WalletDbContext(options))
        {
            var created = await new CheckoutDirectCommandHandler(createContext, provider).Handle(
                new CheckoutDirectCommand(orderId.ToString(), 80_000m) { UserId = buyerId },
                default);
            Assert.Equal("Pending", created.Status);
        }

        await using (var cancelContext = new WalletDbContext(options))
        {
            var cancelled = await new CancelDirectPaymentCommandHandler(cancelContext, provider).Handle(
                new CancelDirectPaymentCommand(orderId) { UserId = buyerId },
                default);
            Assert.Equal("Expired", cancelled.AttemptStatus);
        }

        string replacementSessionId;
        Guid replacementAttemptId;
        await using (var retryContext = new WalletDbContext(options))
        {
            var retried = await new CheckoutDirectCommandHandler(retryContext, provider).Handle(
                new CheckoutDirectCommand(orderId.ToString(), 80_000m) { UserId = buyerId },
                default);
            Assert.Equal("Pending", retried.Status);

            var attempts = await retryContext.StripePaymentAttempts.AsNoTracking()
                .Where(a => a.OwnerId == buyerId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
            Assert.Equal(2, attempts.Count);
            Assert.False(attempts[0].BlocksReplacement);
            Assert.True(attempts[1].BlocksReplacement);
            replacementAttemptId = attempts[1].Id;
            replacementSessionId = attempts[1].CheckoutSessionId!;
        }

        await using (var expireContext = new WalletDbContext(options))
        {
            var expired = await new ExpireDirectPaymentCommandHandler(expireContext, provider).Handle(
                new ExpireDirectPaymentCommand(orderId),
                default);
            Assert.Equal("Expired", expired.AttemptStatus);
        }

        await using (var settleContext = new WalletDbContext(options))
        {
            var paymentIntentId = $"pi_pg_late_{Guid.NewGuid():N}";
            var session = new StripeSessionSnapshot(
                replacementSessionId,
                "complete",
                "paid",
                80_000,
                paymentIntentId,
                replacementAttemptId.ToString("D"),
                replacementAttemptId.ToString("D"),
                false);
            var intent = new StripePaymentIntentSnapshot(
                paymentIntentId,
                "succeeded",
                80_000,
                80_000,
                replacementAttemptId.ToString("D"));

            var outcome = await StripeSettlement.ApplyAsync(
                settleContext,
                replacementAttemptId,
                $"evt_pg_{Guid.NewGuid():N}",
                "checkout.session.completed",
                session,
                intent,
                default);
            Assert.Equal(StripeSettlementOutcome.Settled, outcome);
        }

        await using var verify = new WalletDbContext(options);
        var order = await verify.DirectPaymentOrders.AsNoTracking().SingleAsync(o => o.OrderId == orderId);
        var ledger = await verify.WalletTransactions.AsNoTracking().SingleAsync(t =>
            t.ReferenceId == orderId && t.Type == WalletTransactionType.DirectGatewayPayment);
        var winningAttempt = await verify.StripePaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == replacementAttemptId);

        Assert.Equal(TopupStatus.Success, order.Status);
        Assert.Equal(TransactionStatus.Success, ledger.Status);
        Assert.Equal(StripePaymentAttemptStatus.Successful, winningAttempt.Status);
    }

    private static async Task<(DbContextOptions<WalletDbContext> Options, Guid BuyerId)> SetupWalletAsync(decimal balance)
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var buyerId = Guid.NewGuid();

        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        context.Wallets.Add(new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = buyerId,
            Balance = balance,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        });
        await context.SaveChangesAsync();
        return (options, buyerId);
    }

    private sealed class PostgresDirectProvider : IStripeCheckoutClient
    {
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token) =>
            Task.FromResult(new StripeCheckoutSession(
                $"cs_test_{request.AttemptId:N}",
                $"https://checkout.stripe.com/test/{request.AttemptId:N}",
                DateTimeOffset.UtcNow.AddHours(1)));

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken token) =>
            Task.FromResult(new StripeSessionSnapshot(
                sessionId, "open", "unpaid", 80_000, null, null, null, false));

        public Task<StripeSessionSnapshot> ExpireSessionAsync(string sessionId, CancellationToken token) =>
            Task.FromResult(new StripeSessionSnapshot(
                sessionId, "expired", "unpaid", 80_000, null, null, null, false));

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken token) =>
            throw new NotSupportedException();
    }

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
            {
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
            }
        }
    }
}
