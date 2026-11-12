namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeSettlementPostgresTests
{
    [DisposablePostgresFact]
    public async Task TopupStatus_QueryEnforcesOwnershipOnPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Currency = CurrencyCode.Vnd };
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000, Status = TopupStatus.Success
        };
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(wallet);
            setup.WalletTopups.Add(topup);
            setup.StripePaymentAttempts.Add(new StripePaymentAttempt
            {
                Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.Topup,
                WalletTopupId = topup.Id, Amount = topup.Amount, RequestFingerprint = "fingerprint",
                ProviderIdempotencyKey = $"key_{Guid.NewGuid():N}",
                Status = StripePaymentAttemptStatus.Successful
            });
            await setup.SaveChangesAsync();
        }

        await using var verify = new WalletDbContext(options);
        var result = await StripeTopupStatus.GetAsync(verify, owner, topup.Id, default);
        Assert.Equal("Success", result.Status);
        Assert.Equal(topup.Id, result.TransactionId);
        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            StripeTopupStatus.GetAsync(verify, Guid.NewGuid(), topup.Id, default));
        Assert.Equal("PAYMENT_NOT_FOUND", exception.ErrorCode);
    }

    [DisposablePostgresFact]
    public async Task DirectStatus_ReturnsWinningStripeIntentFromPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(), OrderId = orderId, Amount = 100000, Status = TopupStatus.Success
        };
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Currency = CurrencyCode.Vnd, Balance = 0 };
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(wallet);
            setup.DirectPaymentOrders.Add(order);
            setup.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = owner.ToString(), ReferenceId = orderId,
                Type = WalletTransactionType.DirectGatewayPayment, Amount = 100000, Currency = "Vnd",
                Status = TransactionStatus.Success
            });
            setup.StripePaymentAttempts.AddRange(
                new StripePaymentAttempt
                {
                    Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.DirectOrder,
                    DirectPaymentOrderId = order.Id, Amount = 100000, RequestFingerprint = "fingerprint",
                    ProviderIdempotencyKey = $"key_{Guid.NewGuid():N}", Status = StripePaymentAttemptStatus.Successful,
                    PaymentIntentId = $"pi_{Guid.NewGuid():N}", BlocksReplacement = false,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                },
                new StripePaymentAttempt
                {
                    Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.DirectOrder,
                    DirectPaymentOrderId = order.Id, Amount = 100000, RequestFingerprint = "fingerprint",
                    ProviderIdempotencyKey = $"key_{Guid.NewGuid():N}", Status = StripePaymentAttemptStatus.ReconciliationRequired,
                    PaymentIntentId = $"pi_{Guid.NewGuid():N}", BlocksReplacement = false,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            await setup.SaveChangesAsync();
        }

        await using var verify = new WalletDbContext(options);
        var expected = await verify.StripePaymentAttempts
            .Where(item => item.DirectPaymentOrderId == order.Id && item.Status == StripePaymentAttemptStatus.Successful)
            .Select(item => item.PaymentIntentId).SingleAsync();
        var result = await new GetDirectPaymentStatusQueryHandler(verify).Handle(
            new GetDirectPaymentStatusQuery(orderId.ToString()) { UserId = owner.ToString() }, default);
        Assert.Equal(expected, result.GatewayTransactionNo);
    }

    [DisposablePostgresFact]
    public async Task PaidWebhookBeforeSessionBinding_DoesNotRegressSettlement()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var order = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount { Id = walletId, UserId = owner, Currency = CurrencyCode.Vnd, Balance = 0 });
            await setup.SaveChangesAsync();
        }
        var provider = new EarlyPaymentProvider(options);
        await using (var topupContext = new WalletDbContext(options))
        {
            var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                StripeTopupPreparation.CreateAsync(topupContext, provider, owner, 100000, "early-payment", default));
            Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        }
        await using (var directContext = new WalletDbContext(options))
        {
            var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                StripeDirectPreparation.CreateAsync(directContext, provider, owner, order, 100000, default));
            Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        }
        await using var verify = new WalletDbContext(options);
        Assert.Equal(100000m, (await verify.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
        Assert.Equal(2, await verify.StripePaymentAttempts.CountAsync(item =>
            item.OwnerId == owner && item.Status == StripePaymentAttemptStatus.Successful));
        Assert.Equal(1, await verify.WalletTransactions.CountAsync(item =>
            item.WalletId == walletId && item.Type == WalletTransactionType.TopupStripe && item.Status == TransactionStatus.Success));
        Assert.Equal(1, await verify.WalletTransactions.CountAsync(item =>
            item.ReferenceId == order && item.Type == WalletTransactionType.DirectGatewayPayment && item.Status == TransactionStatus.Success));
    }

    private sealed class EarlyPaymentProvider(DbContextOptions<WalletDbContext> options) : IStripeCheckoutClient
    {
        public async Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
        {
            var reference = request.AttemptId.ToString();
            var sessionId = $"cs_test_{request.AttemptId:N}";
            var intentId = $"pi_{request.AttemptId:N}";
            var session = new StripeSessionSnapshot(sessionId, "complete", "paid", request.AmountVnd,
                intentId, reference, reference, false);
            var intent = new StripePaymentIntentSnapshot(intentId, "succeeded", request.AmountVnd, request.AmountVnd, reference);
            await using var webhookContext = new WalletDbContext(options);
            Assert.Equal(StripeSettlementOutcome.Settled, await StripeSettlement.ApplyAsync(webhookContext,
                request.AttemptId, $"evt_{request.AttemptId:N}", "checkout.session.completed", session, intent, token));
            return new StripeCheckoutSession(sessionId, "https://checkout.stripe.com/test", DateTimeOffset.UtcNow.AddHours(1));
        }
        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }

    [DisposablePostgresFact]
    public async Task DirectCreation_CommitsBeforeProviderCallAndBlocksConcurrentRetry()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = Guid.NewGuid(), UserId = owner, Currency = CurrencyCode.Vnd, Balance = 0
            });
            await setup.SaveChangesAsync();
        }
        var provider = new BlockingCreationProvider(options);
        await using var first = new WalletDbContext(options);
        await using var concurrent = new WalletDbContext(options);
        var creation = StripeDirectPreparation.CreateAsync(first, provider, owner, order, 100000, default);
        try
        {
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                StripeDirectPreparation.CreateAsync(concurrent, provider, owner, order, 100000, default));
            Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        }
        finally
        {
            provider.Release.TrySetResult();
        }
        var result = await creation;
        await using var replayContext = new WalletDbContext(options);
        var replay = await StripeDirectPreparation.CreateAsync(replayContext, provider, owner, order, 100000, default);
        Assert.Equal(result, replay);
        Assert.Equal(1, provider.Calls);
    }

    [DisposablePostgresFact]
    public async Task TopupCreation_CommitsBeforeProviderCallAndConcurrentRetryCannotCreateTwice()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = Guid.NewGuid(), UserId = owner, Currency = CurrencyCode.Vnd, Balance = 0
            });
            await setup.SaveChangesAsync();
        }
        var provider = new BlockingCreationProvider(options);
        await using var first = new WalletDbContext(options);
        await using var concurrent = new WalletDbContext(options);
        var creation = StripeTopupPreparation.CreateAsync(first, provider, owner, 100000, "same-key", default);
        try
        {
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                StripeTopupPreparation.CreateAsync(concurrent, provider, owner, 100000, "same-key", default));
            Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        }
        finally
        {
            provider.Release.TrySetResult();
        }
        var result = await creation;
        await using var replayContext = new WalletDbContext(options);
        var replay = await StripeTopupPreparation.CreateAsync(replayContext, provider, owner, 100000, "same-key", default);
        Assert.Equal(result, replay);
        Assert.Equal(1, provider.Calls);
    }

    private sealed class BlockingCreationProvider(DbContextOptions<WalletDbContext> options) : IStripeCheckoutClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
        {
            Calls++;
            await using var verify = new WalletDbContext(options);
            await using var transaction = await verify.Database.BeginTransactionAsync(token);
            await verify.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1s'", token);
            var attempt = await verify.LockStripePaymentAttemptAsync(request.AttemptId, token);
            Assert.NotNull(attempt);
            Assert.Equal(request.IdempotencyKey, attempt.ProviderIdempotencyKey);
            await transaction.CommitAsync(token);
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new StripeCheckoutSession($"cs_test_{request.AttemptId:N}", "https://checkout.stripe.com/test",
                DateTimeOffset.UtcNow.AddHours(1));
        }

        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }

    [DisposablePostgresFact]
    public async Task LateSuccessWhileReplacementIsCreating_SettlesOnceAndEscalatesSecondCharge()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var owner = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = walletId, UserId = owner, Currency = CurrencyCode.Vnd, Balance = 200000
            });
            await setup.SaveChangesAsync();
        }

        var firstProvider = new BlockingCreationProvider(options);
        firstProvider.Release.SetResult();
        await using (var creationContext = new WalletDbContext(options))
            await StripeDirectPreparation.CreateAsync(creationContext, firstProvider, owner, orderId, 100000, default);

        Guid firstId;
        string firstSessionId;
        await using (var read = new WalletDbContext(options))
        {
            var first = await read.StripePaymentAttempts.SingleAsync(item => item.OwnerId == owner);
            firstId = first.Id;
            firstSessionId = first.CheckoutSessionId!;
        }
        var firstReference = firstId.ToString();
        await using (var expiryContext = new WalletDbContext(options))
        {
            var expired = new StripeSessionSnapshot(firstSessionId, "expired", "unpaid", 100000,
                null, firstReference, firstReference, false);
            Assert.Equal(StripeSettlementOutcome.FinalizedFailure, await StripeSettlement.ApplyAsync(
                expiryContext, firstId, $"evt_{Guid.NewGuid():N}", "checkout.session.expired", expired, null, default));
        }

        var replacementProvider = new BlockingCreationProvider(options);
        await using var replacementContext = new WalletDbContext(options);
        var replacement = StripeDirectPreparation.CreateAsync(
            replacementContext, replacementProvider, owner, orderId, 100000, default);
        try
        {
            await replacementProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var firstIntentId = $"pi_{Guid.NewGuid():N}";
            var latePaid = new StripeSessionSnapshot(firstSessionId, "complete", "paid", 100000,
                firstIntentId, firstReference, firstReference, false);
            var firstIntent = new StripePaymentIntentSnapshot(firstIntentId, "succeeded", 100000, 100000, firstReference);
            await using var lateContext = new WalletDbContext(options);
            Assert.Equal(StripeSettlementOutcome.Settled, await StripeSettlement.ApplyAsync(
                lateContext, firstId, $"evt_{Guid.NewGuid():N}", "checkout.session.async_payment_succeeded",
                latePaid, firstIntent, default));
        }
        finally
        {
            replacementProvider.Release.TrySetResult();
        }
        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(async () => await replacement);
        Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);

        Guid replacementId;
        await using (var read = new WalletDbContext(options))
            replacementId = (await read.StripePaymentAttempts.SingleAsync(item => item.OwnerId == owner && item.Id != firstId)).Id;
        var replacementReference = replacementId.ToString();
        var replacementSessionId = $"cs_test_{replacementId:N}";
        var replacementIntentId = $"pi_{Guid.NewGuid():N}";
        var replacementPaid = new StripeSessionSnapshot(replacementSessionId, "complete", "paid", 100000,
            replacementIntentId, replacementReference, replacementReference, false);
        var replacementIntent = new StripePaymentIntentSnapshot(
            replacementIntentId, "succeeded", 100000, 100000, replacementReference);
        var eventId = $"evt_{Guid.NewGuid():N}";
        await using (var secondContext = new WalletDbContext(options))
            Assert.Equal(StripeSettlementOutcome.ReconciliationRequired, await StripeSettlement.ApplyAsync(
                secondContext, replacementId, eventId, "checkout.session.completed", replacementPaid, replacementIntent, default));

        await using var verify = new WalletDbContext(options);
        Assert.Equal(200000m, (await verify.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
        Assert.Equal(TopupStatus.Success, (await verify.DirectPaymentOrders.SingleAsync(item => item.OrderId == orderId)).Status);
        Assert.Equal(TransactionStatus.Success, (await verify.WalletTransactions.SingleAsync(item =>
            item.ReferenceId == orderId && item.Type == WalletTransactionType.DirectGatewayPayment)).Status);
        Assert.Equal(StripePaymentAttemptStatus.ReconciliationRequired,
            (await verify.StripePaymentAttempts.SingleAsync(item => item.Id == replacementId)).Status);
        Assert.Equal(nameof(StripeSettlementOutcome.ReconciliationRequired),
            (await verify.StripeWebhookEvents.SingleAsync(item => item.ProviderEventId == eventId)).Outcome);
    }

    [DisposablePostgresFact]
    public async Task DirectInitiationAndWalletPayment_OnNewOrder_CannotBothProceed()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var directUser = Guid.NewGuid();
        var walletUser = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.AddRange(
                new WalletAccount { Id = Guid.NewGuid(), UserId = directUser, Currency = CurrencyCode.Vnd, Balance = 200000 },
                new WalletAccount { Id = Guid.NewGuid(), UserId = walletUser, Currency = CurrencyCode.Vnd, Balance = 200000 });
            await setup.SaveChangesAsync();
        }

        await using var directContext = new WalletDbContext(options);
        await using var walletContext = new WalletDbContext(options);
        var directTask = InitiateDirect(directContext, directUser, orderId);
        var walletTask = PayWallet(walletContext, walletUser, orderId);
        await Task.WhenAll(directTask, walletTask);

        Assert.NotEqual(directTask.Result, walletTask.Result);
        await using var verify = new WalletDbContext(options);
        Assert.Equal(directTask.Result ? 1 : 0,
            await verify.DirectPaymentOrders.CountAsync(item => item.OrderId == orderId));
        Assert.Equal(walletTask.Result ? 1 : 0,
            await verify.WalletTransactions.CountAsync(item => item.ReferenceId == orderId &&
                item.Type == WalletTransactionType.PurchaseDebit && item.Status == TransactionStatus.Success));
    }

    private static async Task<bool> InitiateDirect(WalletDbContext context, Guid user, Guid orderId)
    {
        try
        {
            await new CheckoutDirectCommandHandler(context, new FakeStripeCheckoutClient()).Handle(
                new CheckoutDirectCommand(orderId.ToString(), 100000)
                { UserId = user }, default);
            return true;
        }
        catch (BusinessRuleValidationException exception) when (exception.ErrorCode == "PAYMENT_CONFLICT")
        {
            return false;
        }
    }

    [DisposablePostgresFact]
    public async Task WalletPaymentAndLateDirectSuccess_SerializeWithoutDoubleSettlement()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var user = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var otherWalletId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var directId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var sessionId = $"cs_test_{Guid.NewGuid():N}";
        var intentId = $"pi_{Guid.NewGuid():N}";
        var reference = attemptId.ToString();
        var session = new StripeSessionSnapshot(sessionId, "complete", "paid", 100000,
            intentId, reference, reference, false);
        var intent = new StripePaymentIntentSnapshot(intentId, "succeeded", 100000, 100000, reference);
        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = walletId, UserId = user, Currency = CurrencyCode.Vnd, Balance = 200000
            });
            setup.Wallets.Add(new WalletAccount
            {
                Id = otherWalletId, UserId = otherUser, Currency = CurrencyCode.Vnd, Balance = 200000
            });
            setup.DirectPaymentOrders.Add(new DirectPaymentOrder
            {
                Id = directId, OrderId = orderId, Amount = 100000, Status = TopupStatus.Failed
            });
            setup.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(), WalletId = walletId, UserId = user.ToString(),
                Type = WalletTransactionType.DirectGatewayPayment, ReferenceId = orderId,
                Amount = 100000, Status = TransactionStatus.Failed
            });
            setup.StripePaymentAttempts.Add(new StripePaymentAttempt
            {
                Id = attemptId, OwnerId = user, Purpose = StripePaymentPurpose.DirectOrder,
                DirectPaymentOrderId = directId, Amount = 100000, RequestFingerprint = "fingerprint",
                ProviderIdempotencyKey = Guid.NewGuid().ToString(), CheckoutSessionId = sessionId,
                Status = StripePaymentAttemptStatus.Failed, BlocksReplacement = false
            });
            await setup.SaveChangesAsync();
        }

        await using var stripeContext = new WalletDbContext(options);
        await using var walletContext = new WalletDbContext(options);
        var stripeTask = StripeSettlement.ApplyAsync(stripeContext, attemptId, $"evt_{Guid.NewGuid():N}",
            "checkout.session.async_payment_succeeded", session, intent, default);
        var walletTask = PayWallet(walletContext, otherUser, orderId);
        await Task.WhenAll(stripeTask, walletTask);

        await using var verify = new WalletDbContext(options);
        var successes = await verify.WalletTransactions.CountAsync(item => item.ReferenceId == orderId &&
            (item.Type == WalletTransactionType.PurchaseDebit || item.Type == WalletTransactionType.DirectGatewayPayment) &&
            item.Status == TransactionStatus.Success);
        Assert.Equal(1, successes);
        if (walletTask.Result)
            Assert.Equal(StripeSettlementOutcome.ReconciliationRequired, stripeTask.Result);
        else
            Assert.Equal(StripeSettlementOutcome.Settled, stripeTask.Result);
    }

    private static async Task<bool> PayWallet(WalletDbContext context, Guid user, Guid orderId)
    {
        try
        {
            await new PayOrderWithWalletCommandHandler(context).Handle(
                new PayOrderWithWalletCommand(100000) { UserId = user.ToString(), OrderId = orderId }, default);
            return true;
        }
        catch (BusinessRuleValidationException exception) when (exception.ErrorCode == "PAYMENT_ORDER_CONFLICT")
        {
            return false;
        }
    }

    [DisposablePostgresFact]
    public async Task ConcurrentDuplicateDeliveries_AndFailedCommit_AreAtomic()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var userId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var topupId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var reference = attemptId.ToString();
        var eventId = $"evt_{Guid.NewGuid():N}";
        var sessionId = $"cs_test_{Guid.NewGuid():N}";
        var intentId = $"pi_{Guid.NewGuid():N}";
        var session = new StripeSessionSnapshot(sessionId, "complete", "paid", 100000,
            intentId, reference, reference, false);
        var intent = new StripePaymentIntentSnapshot(intentId, "succeeded", 100000, 100000, reference);

        await using (var setup = new WalletDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Wallets.Add(new WalletAccount
            {
                Id = walletId, UserId = userId, Currency = CurrencyCode.Vnd, Balance = 100
            });
            setup.WalletTopups.Add(new WalletTopup
            {
                Id = topupId, WalletId = walletId, Amount = 100000, Status = TopupStatus.Pending
            });
            setup.StripePaymentAttempts.Add(new StripePaymentAttempt
            {
                Id = attemptId, OwnerId = userId, Purpose = StripePaymentPurpose.Topup, WalletTopupId = topupId,
                Amount = 100000, RequestFingerprint = "fingerprint", ProviderIdempotencyKey = Guid.NewGuid().ToString(),
                CheckoutSessionId = sessionId
            });
            await setup.SaveChangesAsync();
        }

        var failingOptions = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(new FailAfterSave()).Options;
        await using (var failed = new WalletDbContext(failingOptions))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                StripeSettlement.ApplyAsync(failed, attemptId, eventId, "checkout.session.completed", session, intent, default));
        }
        await using (var verify = new WalletDbContext(options))
        {
            Assert.Equal(100m, (await verify.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
            Assert.False(await verify.StripeWebhookEvents.AnyAsync(item => item.ProviderEventId == eventId));
            Assert.False(await verify.WalletTransactions.AnyAsync(item => item.ReferenceId == topupId));
        }

        await using var first = new WalletDbContext(options);
        await using var second = new WalletDbContext(options);
        var outcomes = await Task.WhenAll(
            StripeSettlement.ApplyAsync(first, attemptId, eventId, "checkout.session.completed", session, intent, default),
            StripeSettlement.ApplyAsync(second, attemptId, eventId, "checkout.session.completed", session, intent, default));
        Assert.Contains(StripeSettlementOutcome.Settled, outcomes);
        Assert.Contains(StripeSettlementOutcome.DuplicateEvent, outcomes);

        await using var final = new WalletDbContext(options);
        Assert.Equal(100100m, (await final.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
        Assert.Equal(1, await final.StripeWebhookEvents.CountAsync(item => item.ProviderEventId == eventId));
        Assert.Equal(1, await final.WalletTransactions.CountAsync(item => item.ReferenceId == topupId));
        Assert.Equal(1, await final.AuditLogs.CountAsync(item => item.EntityId == topupId.ToString()));

        var nextTopupId = Guid.NewGuid();
        var nextAttemptId = Guid.NewGuid();
        var nextSessionId = $"cs_test_{Guid.NewGuid():N}";
        var nextIntentId = $"pi_{Guid.NewGuid():N}";
        var nextReference = nextAttemptId.ToString();
        await using (var setup = new WalletDbContext(options))
        {
            setup.WalletTopups.Add(new WalletTopup
            {
                Id = nextTopupId, WalletId = walletId, Amount = 100000, Status = TopupStatus.Pending
            });
            setup.StripePaymentAttempts.Add(new StripePaymentAttempt
            {
                Id = nextAttemptId, OwnerId = userId, Purpose = StripePaymentPurpose.Topup,
                WalletTopupId = nextTopupId, Amount = 100000, RequestFingerprint = "fingerprint",
                ProviderIdempotencyKey = Guid.NewGuid().ToString(), CheckoutSessionId = nextSessionId
            });
            await setup.SaveChangesAsync();
        }
        var nextSession = new StripeSessionSnapshot(nextSessionId, "complete", "paid", 100000,
            nextIntentId, nextReference, nextReference, false);
        var nextIntent = new StripePaymentIntentSnapshot(nextIntentId, "succeeded", 100000, 100000, nextReference);
        await using var third = new WalletDbContext(options);
        await using var fourth = new WalletDbContext(options);
        var distinctOutcomes = await Task.WhenAll(
            StripeSettlement.ApplyAsync(third, nextAttemptId, $"evt_{Guid.NewGuid():N}",
                "checkout.session.completed", nextSession, nextIntent, default),
            StripeSettlement.ApplyAsync(fourth, nextAttemptId, $"evt_{Guid.NewGuid():N}",
                "checkout.session.async_payment_succeeded", nextSession, nextIntent, default));
        Assert.Contains(StripeSettlementOutcome.Settled, distinctOutcomes);
        Assert.Contains(StripeSettlementOutcome.AlreadySettled, distinctOutcomes);
        await using var verifyDistinct = new WalletDbContext(options);
        Assert.Equal(200100m, (await verifyDistinct.Wallets.SingleAsync(item => item.Id == walletId)).Balance);
        Assert.Equal(2, await verifyDistinct.StripeWebhookEvents.CountAsync(item => item.PaymentAttemptId == nextAttemptId));
        Assert.Equal(1, await verifyDistinct.WalletTransactions.CountAsync(item => item.ReferenceId == nextTopupId));
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure before transaction commit.");
    }

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
        }
    }
}
