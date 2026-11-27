namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Payments;
using Wallet.Infrastructure.Services.Notification;
using Wallet.Tests.Support;
using Xunit;

public sealed class WalletTopupNotificationTests
{
    private const string WebhookSecret = "whsec_test_topup_notification_only";

    [Fact]
    public async Task NotificationClient_SendWalletTopupEventAsync_PostsExactContractAndHeaders()
    {
        var handler = new CapturingHttpHandler(
            HttpStatusCode.OK,
            "{\"sourceEventId\":\"wallet-topup:11111111-2222-3333-4444-555555555555:succeeded\",\"notificationId\":\"22222222-3333-4444-5555-666666666666\",\"isReplay\":false,\"deliveries\":[]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8007/") };
        var key = "0123456789abcdef0123456789abcdef";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Notification:ServiceKey"] = key
        }).Build();
        var client = new NotificationClient(http, config, NullLogger<NotificationClient>.Instance);

        var topupId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var txId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var userId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var confirmedAt = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

        var payload = new WalletTopupNotificationEvent(
            SourceEventId: $"wallet-topup:{topupId:D}:succeeded",
            TopupId: topupId,
            TransactionId: txId,
            RecipientUserId: userId,
            Amount: 10_000_000,
            Currency: "VND",
            ConfirmedAt: confirmedAt,
            TraceId: "trace-topup-1");

        var result = await client.SendWalletTopupEventAsync(payload);

        Assert.True(result.Succeeded);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("22222222-3333-4444-5555-666666666666", result.NotificationId);
        Assert.Equal("/api/v1/notifications/wallet-topups", handler.LastPath);
        Assert.Equal(key, handler.LastServiceKey);
        Assert.False(handler.HadAuthorizationHeader);
        Assert.False(handler.HadUserHeader);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[] { "amount", "confirmedAt", "currency", "recipientUserId", "sourceEventId", "topupId", "traceId", "transactionId" },
            props);
        Assert.Equal($"wallet-topup:{topupId:D}:succeeded", doc.RootElement.GetProperty("sourceEventId").GetString());
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("amount").ValueKind);
        Assert.Equal("10000000", doc.RootElement.GetProperty("amount").GetRawText());
        Assert.Equal("VND", doc.RootElement.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task StripeWebhook_SettledTopup_CreatesOneTransactionAndOneNotification_AndReplaysDoNotDuplicate()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopupAsync(context, amount: 250_000m);
        var provider = new FakeStripeProvider(attempt);
        var notificationClient = new ScriptedTopupNotificationClient();

        var body1 = WebhookPayload(attempt, "evt_topup_1", "checkout.session.completed");
        var first = await StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body1), Signature(body1), default,
            NullLogger.Instance, notificationClient);

        // Same webhook replayed
        var duplicate = await StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body1), Signature(body1), default,
            NullLogger.Instance, notificationClient);

        // Different webhook event for the already-settled topup
        var body2 = WebhookPayload(attempt, "evt_topup_2", "checkout.session.async_payment_succeeded");
        var alreadySettled = await StripeWebhookProcessing.ProcessAsync(
            context, Verifier(), provider, Encoding.UTF8.GetBytes(body2), Signature(body2), default,
            NullLogger.Instance, notificationClient);

        // Reconcile worker run after settlement
        var reconcileHandler = new ReconcilePendingTopupsCommandHandler(
            context, provider, NullLogger<ReconcilePendingTopupsCommandHandler>.Instance, notificationClient);
        var reconciledCount = await reconcileHandler.Handle(new ReconcilePendingTopupsCommand(), default);

        Assert.Equal(StripeWebhookProcessingOutcome.Settled, first);
        Assert.Equal(StripeWebhookProcessingOutcome.DuplicateEvent, duplicate);
        Assert.Equal(StripeWebhookProcessingOutcome.AlreadySettled, alreadySettled);
        Assert.Equal(0, reconciledCount);
        Assert.Equal(250_100m, wallet.Balance);

        var tx = Assert.Single(context.WalletTransactions, t => t.Type == WalletTransactionType.TopupStripe);
        Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationQueued && a.EntityName == nameof(WalletTopup));
        Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationDelivered && a.EntityName == nameof(WalletTopup));

        var sent = Assert.Single(notificationClient.Sent);
        Assert.Equal($"wallet-topup:{topup.Id:D}:succeeded", sent.SourceEventId);
        Assert.Equal(topup.Id, sent.TopupId);
        Assert.Equal(tx.Id, sent.TransactionId);
        Assert.Equal(wallet.UserId, sent.RecipientUserId);
        Assert.Equal(250_000L, sent.Amount);
        Assert.Equal("VND", sent.Currency);
    }

    [Fact]
    public async Task StripeSettlement_PendingFailureReconciliationAndDirectOrder_DoNotQueueOrSendNotification()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var notificationClient = new ScriptedTopupNotificationClient();

        // 1. Pending topup
        var (_, _, pendingAttempt) = await SeedTopupAsync(context, amount: 100_000m);
        var pendingOutcome = await StripeSettlement.ApplyAsync(
            context, pendingAttempt.Id, "evt_pending", "checkout.session.completed",
            new StripeSessionSnapshot(pendingAttempt.CheckoutSessionId!, "open", "unpaid", 100_000, "pi_1", pendingAttempt.Id.ToString(), pendingAttempt.Id.ToString(), false),
            new StripePaymentIntentSnapshot("pi_1", "processing", 100_000, 0, pendingAttempt.Id.ToString()),
            default, notificationClient);
        Assert.Equal(StripeSettlementOutcome.Pending, pendingOutcome);

        // 2. Finalized failure topup
        var (_, _, failedAttempt) = await SeedTopupAsync(context, amount: 100_000m);
        var failOutcome = await StripeSettlement.ApplyAsync(
            context, failedAttempt.Id, "evt_expired", "checkout.session.expired",
            new StripeSessionSnapshot(failedAttempt.CheckoutSessionId!, "expired", "unpaid", 100_000, null, failedAttempt.Id.ToString(), failedAttempt.Id.ToString(), false),
            null,
            default, notificationClient);
        Assert.Equal(StripeSettlementOutcome.FinalizedFailure, failOutcome);

        // 3. ReconciliationRequired topup (amount mismatch)
        var (_, _, mismatchAttempt) = await SeedTopupAsync(context, amount: 100_000m);
        var mismatchOutcome = await StripeSettlement.ApplyAsync(
            context, mismatchAttempt.Id, "evt_mismatch", "checkout.session.completed",
            new StripeSessionSnapshot(mismatchAttempt.CheckoutSessionId!, "complete", "paid", 99_999, "pi_3", mismatchAttempt.Id.ToString(), mismatchAttempt.Id.ToString(), false),
            new StripePaymentIntentSnapshot("pi_3", "succeeded", 99_999, 99_999, mismatchAttempt.Id.ToString()),
            default, notificationClient);
        Assert.Equal(StripeSettlementOutcome.ReconciliationRequired, mismatchOutcome);

        // 4. DirectOrder settled
        var directAttempt = await SeedDirectOrderAsync(context, amount: 150_000m);
        var directOutcome = await StripeSettlement.ApplyAsync(
            context, directAttempt.Id, "evt_direct", "checkout.session.completed",
            new StripeSessionSnapshot(directAttempt.CheckoutSessionId!, "complete", "paid", 150_000, "pi_4", directAttempt.Id.ToString(), directAttempt.Id.ToString(), false),
            new StripePaymentIntentSnapshot("pi_4", "succeeded", 150_000, 150_000, directAttempt.Id.ToString()),
            default, notificationClient);
        Assert.Equal(StripeSettlementOutcome.Settled, directOutcome);

        Assert.Empty(notificationClient.Sent);
        Assert.DoesNotContain(context.AuditLogs, a => a.Action == AuditAction.NotificationQueued);
    }

    [Fact]
    public async Task NotificationOutageAndRecovery_CommitsMoney_AndRetryDeliversExactSameEventOnce()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopupAsync(context, amount: 500_000m);
        var notificationClient = new ScriptedTopupNotificationClient { NextStatusCode = 503 };

        var outcome = await StripeSettlement.ApplyAsync(
            context, attempt.Id, "evt_outage", "checkout.session.completed",
            new StripeSessionSnapshot(attempt.CheckoutSessionId!, "complete", "paid", 500_000, "pi_outage", attempt.Id.ToString(), attempt.Id.ToString(), false),
            new StripePaymentIntentSnapshot("pi_outage", "succeeded", 500_000, 500_000, attempt.Id.ToString()),
            default, notificationClient);

        Assert.Equal(StripeSettlementOutcome.Settled, outcome);
        Assert.Equal(500_100m, wallet.Balance);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Single(notificationClient.Sent);
        Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationQueued && a.EntityName == nameof(WalletTopup));
        Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationDispatchFailed && a.EntityName == nameof(WalletTopup));
        Assert.DoesNotContain(context.AuditLogs, a => a.Action == AuditAction.NotificationDelivered);

        var now = DateTimeOffset.UtcNow;
        var tooEarly = await WithdrawalNotificationRetry.RetryPendingAsync(context, notificationClient, now, 20, default);
        Assert.Equal(0, tooEarly);

        // Notification service recovers
        notificationClient.NextStatusCode = 200;
        var recovered = await WithdrawalNotificationRetry.RetryPendingAsync(context, notificationClient, now.AddMinutes(5), 20, default);
        var afterRecovery = await WithdrawalNotificationRetry.RetryPendingAsync(context, notificationClient, now.AddMinutes(30), 20, default);

        Assert.Equal(1, recovered);
        Assert.Equal(0, afterRecovery);
        Assert.Equal(2, notificationClient.Sent.Count);
        Assert.Equal(notificationClient.Sent[0] with { TraceId = null }, notificationClient.Sent[1] with { TraceId = null });
        Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationDelivered && a.EntityName == nameof(WalletTopup));
    }

    [Fact]
    public async Task NotificationConflict409_MarksReconciliationRequiredAndStopsRetrying_WithoutChangingSourceEventId()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var (wallet, topup, attempt) = await SeedTopupAsync(context, amount: 300_000m);
        var notificationClient = new ScriptedTopupNotificationClient { NextStatusCode = 409, NextError = "WALLET_TOPUP_CONFLICT" };

        var outcome = await StripeSettlement.ApplyAsync(
            context, attempt.Id, "evt_conflict", "checkout.session.completed",
            new StripeSessionSnapshot(attempt.CheckoutSessionId!, "complete", "paid", 300_000, "pi_conflict", attempt.Id.ToString(), attempt.Id.ToString(), false),
            new StripePaymentIntentSnapshot("pi_conflict", "succeeded", 300_000, 300_000, attempt.Id.ToString()),
            default, notificationClient, NullLogger.Instance);

        Assert.Equal(StripeSettlementOutcome.Settled, outcome);
        Assert.Equal(300_100m, wallet.Balance);
        Assert.Single(notificationClient.Sent);
        Assert.Equal($"wallet-topup:{topup.Id:D}:succeeded", notificationClient.Sent[0].SourceEventId);

        var failedAudit = Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationDispatchFailed && a.EntityName == nameof(WalletTopup));
        Assert.Contains("\"ReconciliationRequired\":true", failedAudit.NewValues);

        var abandonedAudit = Assert.Single(context.AuditLogs, a => a.Action == AuditAction.NotificationAbandoned && a.EntityName == nameof(WalletTopup));
        Assert.Contains("\"ReconciliationRequired\":true", abandonedAudit.NewValues);
        Assert.Contains("\"StatusCode\":409", abandonedAudit.NewValues);

        // Retry worker must NOT retry a 409 conflict or alter sourceEventId
        var retried = await WithdrawalNotificationRetry.RetryPendingAsync(
            context, notificationClient, DateTimeOffset.UtcNow.AddHours(2), 20, default, NullLogger.Instance);
        Assert.Equal(0, retried);
        Assert.Single(notificationClient.Sent);
    }

    private static async Task<(WalletAccount, WalletTopup, StripePaymentAttempt)> SeedTopupAsync(TestWalletDbContext context, decimal amount)
    {
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 100m, Currency = CurrencyCode.Vnd };
        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = amount, Status = TopupStatus.Pending };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = owner,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = topup.Id,
            Amount = amount,
            RequestFingerprint = $"fp-{Guid.NewGuid():N}",
            ProviderIdempotencyKey = $"key-{Guid.NewGuid():N}",
            CheckoutSessionId = $"cs_test_{Guid.NewGuid():N}"
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return (wallet, topup, attempt);
    }

    private static async Task<StripePaymentAttempt> SeedDirectOrderAsync(TestWalletDbContext context, decimal amount)
    {
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Balance = 100m, Currency = CurrencyCode.Vnd };
        var orderId = Guid.NewGuid();
        var order = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = orderId, Amount = amount, Status = TopupStatus.Pending };
        var tx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = owner.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = amount,
            Currency = CurrencyCode.Vnd.ToString(),
            BalanceBefore = 100m,
            BalanceAfter = 100m,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId
        };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = owner,
            Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = order.Id,
            Amount = amount,
            RequestFingerprint = $"fp-{Guid.NewGuid():N}",
            ProviderIdempotencyKey = $"key-{Guid.NewGuid():N}",
            CheckoutSessionId = $"cs_test_{Guid.NewGuid():N}"
        };
        context.Wallets.Add(wallet);
        context.DirectPaymentOrders.Add(order);
        context.WalletTransactions.Add(tx);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt;
    }

    private static string WebhookPayload(StripePaymentAttempt attempt, string eventId, string type) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            api_version = StripeConfiguration.ApiVersion,
            type,
            livemode = false,
            created = 1900000000,
            data = new
            {
                @object = new
                {
                    id = attempt.CheckoutSessionId,
                    @object = "checkout.session",
                    metadata = new { attempt_id = attempt.Id.ToString() }
                }
            }
        });

    private static string Signature(string body)
    {
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"t={time},v1={EventUtility.ComputeSignature(WebhookSecret, time, body)}";
    }

    private static StripeWebhookVerifier Verifier() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = WebhookSecret }).Build());

    private sealed class FakeStripeProvider(StripePaymentAttempt attempt) : IStripeCheckoutClient
    {
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken token) =>
            Task.FromResult(new StripeSessionSnapshot(
                sessionId, "complete", "paid", (long)attempt.Amount, "pi_topup_123",
                attempt.Id.ToString(), attempt.Id.ToString(), false));

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) =>
            Task.FromResult(new StripePaymentIntentSnapshot(
                id, "succeeded", (long)attempt.Amount, (long)attempt.Amount, attempt.Id.ToString()));
    }

    private sealed class ScriptedTopupNotificationClient : INotificationClient
    {
        public int NextStatusCode { get; set; } = 200;
        public string? NextError { get; set; }
        public List<WalletTopupNotificationEvent> Sent { get; } = new();

        public Task<NotificationDispatchResult> SendWithdrawalEventAsync(WithdrawalNotificationEvent payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationDispatchResult(true, 200));

        public Task<NotificationDispatchResult> SendAuctionDepositEventAsync(AuctionDepositNotificationEvent payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationDispatchResult(true, 200));

        public Task<NotificationDispatchResult> SendWalletTopupEventAsync(WalletTopupNotificationEvent payload, CancellationToken cancellationToken = default)
        {
            Sent.Add(payload);
            var ok = NextStatusCode is >= 200 and < 300;
            return Task.FromResult(ok
                ? new NotificationDispatchResult(true, NextStatusCode, "notif-topup-1")
                : new NotificationDispatchResult(false, NextStatusCode, Error: NextError ?? "Simulated failure"));
        }
    }

    private sealed class CapturingHttpHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastServiceKey { get; private set; }
        public bool HadAuthorizationHeader { get; private set; }
        public bool HadUserHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastServiceKey = request.Headers.TryGetValues("X-Internal-Service-Key", out var vals) ? vals.FirstOrDefault() : null;
            HadAuthorizationHeader = request.Headers.Authorization is not null || request.Headers.Contains("Authorization");
            HadUserHeader = request.Headers.Any(h => h.Key.StartsWith("x-user-", StringComparison.OrdinalIgnoreCase));
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
