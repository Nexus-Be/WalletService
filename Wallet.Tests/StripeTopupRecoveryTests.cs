namespace Wallet.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Application.Features.Topups.Commands.ResumeTopup;
using Wallet.Application.Features.Topups.Queries.ListTopups;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeTopupRecoveryTests
{
    [Fact]
    public async Task History_IsOwnedNewestFirstAndPaginated()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var (older, _) = await Seed(context, owner, DateTimeOffset.UtcNow.AddMinutes(-3));
        var (newer, _) = await Seed(context, owner, DateTimeOffset.UtcNow.AddMinutes(-2));
        await Seed(context, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));

        var handler = new ListTopupsQueryHandler(context);
        var first = await handler.Handle(new ListTopupsQuery(1, 1) { UserId = owner }, default);
        var second = await handler.Handle(new ListTopupsQuery(2, 1) { UserId = owner }, default);

        Assert.Equal(2, first.Pagination.TotalCount);
        Assert.True(first.Pagination.HasNextPage);
        Assert.Equal(newer.Id, Assert.Single(first.Items).TransactionId);
        Assert.True(first.Items[0].CanResume);
        Assert.Equal(older.Id, Assert.Single(second.Items).TransactionId);
    }

    [Fact]
    public async Task Resume_ReturnsExistingSessionOnlyToOwner()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var (topup, attempt) = await Seed(context, owner, DateTimeOffset.UtcNow.AddMinutes(-3));
        var provider = new Provider(attempt);
        var handler = new ResumeTopupCommandHandler(context, provider);

        var result = await handler.Handle(new ResumeTopupCommand(topup.Id) { UserId = owner }, default);
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new ResumeTopupCommand(topup.Id) { UserId = Guid.NewGuid() }, default));

        Assert.Equal(topup.Id, result.TransactionId);
        Assert.Equal(attempt.CheckoutUrl, result.PaymentUrl);
        Assert.Equal(1, provider.SessionReads);
        Assert.Equal(0, provider.Creates);
    }

    [Fact]
    public async Task Resume_RejectsProviderSessionThatIsNoLongerOpen()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var (topup, attempt) = await Seed(context, owner, DateTimeOffset.UtcNow.AddMinutes(-3));
        var provider = new Provider(attempt) { SessionStatus = "complete", PaymentStatus = "paid" };
        var handler = new ResumeTopupCommandHandler(context, provider);

        await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(new ResumeTopupCommand(topup.Id) { UserId = owner }, default));
        Assert.Equal(0, provider.Creates);
    }

    [Fact]
    public async Task Reconciliation_SettlesPaidTopupOnce()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var (_, attempt) = await Seed(context, owner, DateTimeOffset.UtcNow.AddMinutes(-3));
        var provider = new Provider(attempt) { SessionStatus = "complete", PaymentStatus = "paid" };
        var handler = new ReconcilePendingTopupsCommandHandler(
            context, provider, NullLogger<ReconcilePendingTopupsCommandHandler>.Instance);

        Assert.Equal(1, await handler.Handle(new ReconcilePendingTopupsCommand(), default));
        Assert.Equal(0, await handler.Handle(new ReconcilePendingTopupsCommand(), default));
        Assert.Equal(100000m, context.Wallets.Single(w => w.UserId == owner).Balance);
        Assert.Single(context.WalletTransactions);
    }

    [Fact]
    public async Task Reconciliation_IsolatesMismatchedProviderSessionWithoutAbortingBatch()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, attempt) = await Seed(context, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-3));
        var provider = new Provider(attempt) { ReturnedSessionId = "cs_test_other" };
        var handler = new ReconcilePendingTopupsCommandHandler(
            context, provider, NullLogger<ReconcilePendingTopupsCommandHandler>.Instance);

        var processed = await handler.Handle(new ReconcilePendingTopupsCommand(), default);
        Assert.Equal(0, processed);
        Assert.Empty(context.WalletTransactions);
    }

    [Fact]
    public async Task Reconciliation_RotatesAfterProviderFailureWithinSingleBatch()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (_, first) = await Seed(context, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-4));
        var (_, second) = await Seed(context, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-3));
        first.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        second.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        await context.SaveChangesAsync();
        var provider = new FailingProvider();
        var handler = new ReconcilePendingTopupsCommandHandler(
            context, provider, NullLogger<ReconcilePendingTopupsCommandHandler>.Instance);

        var processed = await handler.Handle(new ReconcilePendingTopupsCommand(), default);

        Assert.Equal(0, processed);
        Assert.Equal(new[] { first.CheckoutSessionId, second.CheckoutSessionId }, provider.Sessions);
    }

    private static async Task<(WalletTopup Topup, StripePaymentAttempt Attempt)> Seed(
        TestWalletDbContext context, Guid owner, DateTimeOffset created)
    {
        var wallet = context.Wallets.SingleOrDefault(item => item.UserId == owner);
        if (wallet is null)
        {
            wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Currency = CurrencyCode.Vnd };
            context.Wallets.Add(wallet);
        }
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000,
            Status = TopupStatus.Pending, CreatedAt = created
        };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = topup.Id, Amount = topup.Amount, RequestFingerprint = "fingerprint",
            ProviderIdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = created,
            Status = StripePaymentAttemptStatus.AwaitingPayment, CheckoutSessionId = $"cs_test_{Guid.NewGuid():N}",
            CheckoutUrl = "https://checkout.stripe.com/test", SessionExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return (topup, attempt);
    }

    private sealed class Provider(StripePaymentAttempt attempt) : IStripeCheckoutClient
    {
        public int Creates { get; private set; }
        public int SessionReads { get; private set; }
        public string SessionStatus { get; init; } = "open";
        public string PaymentStatus { get; init; } = "unpaid";
        public string? ReturnedSessionId { get; init; }

        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
        {
            Creates++;
            throw new NotSupportedException();
        }

        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token)
        {
            SessionReads++;
            return Task.FromResult(new StripeSessionSnapshot(ReturnedSessionId ?? id, SessionStatus, PaymentStatus,
                100000, PaymentStatus == "paid" ? "pi_test_123" : null,
                attempt.Id.ToString("D"), attempt.Id.ToString("D"), false));
        }

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) =>
            Task.FromResult(new StripePaymentIntentSnapshot(id, "succeeded", 100000, 100000, attempt.Id.ToString("D")));
    }

    private sealed class FailingProvider : IStripeCheckoutClient
    {
        public List<string> Sessions { get; } = [];

        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token)
        {
            Sessions.Add(id);
            throw new InvalidOperationException("Simulated provider failure.");
        }

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
