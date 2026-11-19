namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wallet.Application.Features.Checkout.Commands.CancelDirectPayment;
using Wallet.Application.Features.Checkout.Commands.ExpireDirectPayment;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.ResumeDirectPayment;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class DirectCheckoutLifecycleTests
{
    [Fact]
    public async Task ResumeDirectPayment_ReturnsExistingUrlWithoutNewSession_AndBlocksOtherUsers()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var wallet = AddWallet(context, owner, 500_000m);
        var provider = new LifecycleProvider(context);

        var created = await StripeDirectPreparation.CreateAsync(context, provider, owner, orderId, 100_000m, default);
        var attempt = context.StripePaymentAttempts.Single();
        provider.BindAttempt(attempt);

        var resumeHandler = new ResumeDirectPaymentCommandHandler(context, provider);
        var resumed = await resumeHandler.Handle(new ResumeDirectPaymentCommand(orderId) { UserId = owner }, default);

        Assert.Equal(orderId, resumed.OrderId);
        Assert.Equal(created.PaymentUrl, resumed.PaymentUrl);
        Assert.Equal(1, provider.CreateCalls);
        Assert.Equal(1, provider.ReadCalls);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            resumeHandler.Handle(new ResumeDirectPaymentCommand(orderId) { UserId = Guid.NewGuid() }, default));

        provider.SessionStatus = "expired";
        provider.PaymentStatus = "unpaid";
        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            resumeHandler.Handle(new ResumeDirectPaymentCommand(orderId) { UserId = owner }, default));
        Assert.Equal("PAYMENT_CONFLICT", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task CancelDirectPayment_ExpiresSessionAndAllowsPayOrderWithWalletAfterwards()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var wallet = AddWallet(context, owner, 500_000m);
        var provider = new LifecycleProvider(context);

        await StripeDirectPreparation.CreateAsync(context, provider, owner, orderId, 100_000m, default);
        var attempt = context.StripePaymentAttempts.Single();
        provider.BindAttempt(attempt);

        var cancelled = await new CancelDirectPaymentCommandHandler(context, provider)
            .Handle(new CancelDirectPaymentCommand(orderId) { UserId = owner }, default);

        Assert.Equal("Failed", cancelled.Status);
        Assert.Equal("Expired", cancelled.AttemptStatus);
        Assert.Equal(1, provider.ExpireCalls);
        Assert.False(attempt.BlocksReplacement);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.DirectPaymentCancel));

        var paid = await new PayOrderWithWalletCommandHandler(context)
            .Handle(new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = owner.ToString() }, default);
        Assert.Equal("PurchaseDebit", paid.Type);
        Assert.Equal(400_000m, wallet.Balance);
    }

    [Fact]
    public async Task CancelDirectPayment_WhenProviderAlreadyPaid_Returns409()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        AddWallet(context, owner, 500_000m);
        var provider = new LifecycleProvider(context);

        await StripeDirectPreparation.CreateAsync(context, provider, owner, orderId, 100_000m, default);
        var attempt = context.StripePaymentAttempts.Single();
        provider.BindAttempt(attempt);
        provider.SessionStatus = "complete";
        provider.PaymentStatus = "paid";

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new CancelDirectPaymentCommandHandler(context, provider)
                .Handle(new CancelDirectPaymentCommand(orderId) { UserId = owner }, default));

        Assert.Equal("PAYMENT_ALREADY_COMPLETED", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(0, provider.ExpireCalls);
    }

    [Fact]
    public async Task ExpireDirectPayment_Internal_ExpiresOpenAttemptIdempotently()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        AddWallet(context, owner, 500_000m);
        var provider = new LifecycleProvider(context);

        await StripeDirectPreparation.CreateAsync(context, provider, owner, orderId, 100_000m, default);
        var attempt = context.StripePaymentAttempts.Single();
        provider.BindAttempt(attempt);

        var expireHandler = new ExpireDirectPaymentCommandHandler(context, provider);
        var first = await expireHandler.Handle(new ExpireDirectPaymentCommand(orderId), default);
        var second = await expireHandler.Handle(new ExpireDirectPaymentCommand(orderId), default);

        Assert.Equal("Failed", first.Status);
        Assert.Equal("Expired", first.AttemptStatus);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(1, provider.ExpireCalls);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.DirectPaymentExpire));
    }

    private static WalletAccount AddWallet(TestWalletDbContext context, Guid userId, decimal balance)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = balance,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private sealed class LifecycleProvider(TestWalletDbContext context) : IStripeCheckoutClient
    {
        private StripePaymentAttempt? _attempt;
        public int CreateCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int ExpireCalls { get; private set; }
        public string SessionStatus { get; set; } = "open";
        public string PaymentStatus { get; set; } = "unpaid";

        public void BindAttempt(StripePaymentAttempt attempt) => _attempt = attempt;

        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
        {
            CreateCalls++;
            return Task.FromResult(new StripeCheckoutSession(
                $"cs_test_{request.AttemptId:N}",
                $"https://checkout.stripe.com/test/{request.AttemptId:N}",
                DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken token)
        {
            ReadCalls++;
            var attempt = _attempt ?? context.StripePaymentAttempts.Single(a => a.CheckoutSessionId == sessionId);
            return Task.FromResult(new StripeSessionSnapshot(
                sessionId,
                SessionStatus,
                PaymentStatus,
                decimal.ToInt64(attempt.Amount),
                null,
                attempt.Id.ToString("D"),
                attempt.Id.ToString("D"),
                false));
        }

        public Task<StripeSessionSnapshot> ExpireSessionAsync(string sessionId, CancellationToken token)
        {
            ExpireCalls++;
            SessionStatus = "expired";
            PaymentStatus = "unpaid";
            var attempt = _attempt ?? context.StripePaymentAttempts.Single(a => a.CheckoutSessionId == sessionId);
            return Task.FromResult(new StripeSessionSnapshot(
                sessionId,
                SessionStatus,
                PaymentStatus,
                decimal.ToInt64(attempt.Amount),
                null,
                attempt.Id.ToString("D"),
                attempt.Id.ToString("D"),
                false));
        }

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
