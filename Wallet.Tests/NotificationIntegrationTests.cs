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
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Services.Notification;
using Wallet.Tests.Support;
using Xunit;

public sealed class NotificationIntegrationTests
{
    [Fact]
    public async Task NotificationClient_PostsExpectedRoutesPayloadsAndOptionalKey_AndNeverThrows()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "{\"notificationId\":\"notif-42\"}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8007/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Notification:ServiceKey"] = "notif-secret"
        }).Build();
        var client = new NotificationClient(http, config, NullLogger<NotificationClient>.Instance);

        var wdEvt = new WithdrawalNotificationEvent(
            "withdrawal:111:Completed",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Completed",
            DateTimeOffset.UtcNow,
            null,
            "BANK-1",
            "trace-1");
        var wdRes = await client.SendWithdrawalEventAsync(wdEvt);
        Assert.True(wdRes.Succeeded);
        Assert.Equal(200, wdRes.StatusCode);
        Assert.Equal("notif-42", wdRes.NotificationId);
        Assert.Equal("/api/v1/notifications/withdrawal-events", handler.LastPath);
        Assert.Equal("notif-secret", handler.LastServiceKey);
        Assert.Contains("\"sourceEventId\":\"withdrawal:111:Completed\"", handler.LastBody);

        var failHandler = new StubHttpHandler(HttpStatusCode.InternalServerError, "down");
        using var failHttp = new HttpClient(failHandler) { BaseAddress = new Uri("http://localhost:8007/") };
        var failClient = new NotificationClient(
            failHttp,
            new ConfigurationBuilder().Build(),
            NullLogger<NotificationClient>.Instance);
        var depRes = await failClient.SendAuctionDepositEventAsync(new AuctionDepositNotificationEvent(
            "hold:222:Held",
            "222",
            "auc-1",
            Guid.NewGuid(),
            "Held",
            500_000m,
            "VND",
            DateTimeOffset.UtcNow,
            "trace-2"));

        Assert.False(depRes.Succeeded);
        Assert.Equal(500, depRes.StatusCode);
        Assert.Null(failHandler.LastServiceKey);
        Assert.Equal("/api/v1/notifications/auction-deposits", failHandler.LastPath);

        var throwingClient = new NotificationClient(
            new HttpClient(new ThrowingHttpHandler()) { BaseAddress = new Uri("http://localhost:8007/") },
            new ConfigurationBuilder().Build(),
            NullLogger<NotificationClient>.Instance);
        var errRes = await throwingClient.SendWithdrawalEventAsync(wdEvt);
        Assert.False(errRes.Succeeded);
        Assert.NotNull(errRes.Error);
    }

    [Fact]
    public async Task ProcessWithdrawal_EmitsForApprovedRejectedCompletedFailed_SkipsStartProcessing_AndSurvivesFailure()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var sellerId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = sellerId, Balance = 500_000m, LockedBalance = 100_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var withdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            Fee = 0m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller",
            Status = WithdrawalStatus.Pending
        };
        context.Wallets.Add(wallet);
        context.WithdrawalRequests.Add(withdrawal);
        await context.SaveChangesAsync();

        var recorder = new RecordingNotificationClient();
        var handler = new ProcessWithdrawalCommandHandler(context, recorder);
        var adminId = Guid.NewGuid().ToString();

        await handler.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = withdrawal.Id, AdminUserId = adminId }, default);
        await handler.Handle(new ProcessWithdrawalCommand("StartProcessing") { WithdrawalId = withdrawal.Id, AdminUserId = adminId }, default);

        Assert.Single(recorder.Withdrawals);
        Assert.Equal($"withdrawal:{withdrawal.Id:D}:Approved", recorder.Withdrawals[0].SourceEventId);

        recorder.ShouldFail = true;
        await handler.Handle(
            new ProcessWithdrawalCommand(
                "ResolveFailed",
                AdminNote: "Bank rejected transfer",
                BankVerifiedNotTransferred: true,
                VerificationEvidence: "SECRET-INTERNAL-LOG")
            { WithdrawalId = withdrawal.Id, AdminUserId = adminId, ActorIsAdmin = true },
            default);

        Assert.Equal(WithdrawalStatus.Failed, withdrawal.Status);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Equal(2, recorder.Withdrawals.Count);
        Assert.Equal("Bank rejected transfer", recorder.Withdrawals[1].DisplayReason);
        Assert.DoesNotContain("SECRET-INTERNAL-LOG", recorder.Withdrawals[1].DisplayReason ?? string.Empty);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.NotificationDispatchFailed));

        recorder.ShouldFail = false;
        wallet.LockFunds(200_000m);
        var rejectedWithdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            Fee = 0m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller",
            Status = WithdrawalStatus.Pending
        };
        var completedWithdrawal = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            Fee = 0m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller",
            Status = WithdrawalStatus.Pending
        };
        context.WithdrawalRequests.AddRange(rejectedWithdrawal, completedWithdrawal);
        await context.SaveChangesAsync();

        await handler.Handle(
            new ProcessWithdrawalCommand("Reject", AdminNote: "Invalid bank name")
            { WithdrawalId = rejectedWithdrawal.Id, AdminUserId = adminId },
            default);
        await handler.Handle(
            new ProcessWithdrawalCommand("Approve")
            { WithdrawalId = completedWithdrawal.Id, AdminUserId = adminId },
            default);
        await handler.Handle(
            new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, TransferReference: "FT-NOTIFY-COMPLETE-1")
            { WithdrawalId = completedWithdrawal.Id, AdminUserId = adminId },
            default);

        Assert.Equal(5, recorder.Withdrawals.Count);
        Assert.Equal($"withdrawal:{rejectedWithdrawal.Id:D}:Rejected", recorder.Withdrawals[2].SourceEventId);
        Assert.Equal("Rejected", recorder.Withdrawals[2].Status);
        Assert.Equal($"withdrawal:{completedWithdrawal.Id:D}:Approved", recorder.Withdrawals[3].SourceEventId);
        Assert.Equal($"withdrawal:{completedWithdrawal.Id:D}:Completed", recorder.Withdrawals[4].SourceEventId);
        Assert.Equal("Completed", recorder.Withdrawals[4].Status);
    }

    [Fact]
    public async Task AuctionHoldLifecycle_EmitsDeterministicPayloadsAcrossCreateCaptureForfeitRefundAndReplay()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var bidderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        context.Wallets.AddRange(
            new WalletAccount { Id = Guid.NewGuid(), UserId = bidderId, Balance = 1_000_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" },
            new WalletAccount { Id = Guid.NewGuid(), UserId = sellerId, Balance = 0m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" });
        await context.SaveChangesAsync();

        var recorder = new RecordingNotificationClient();
        using var metaDoc = JsonDocument.Parse("{\"auctionId\":\"auc-999\"}");

        var createHandler = new CreateHoldCommandHandler(context, recorder);
        var createCmd = new CreateHoldCommand(bidderId, "AUCTION_DEPOSIT", "dep-999", 200_000m, "VND", metaDoc.RootElement.Clone())
        {
            IdempotencyKey = "create-999"
        };
        var created = await createHandler.Handle(createCmd, default);
        await createHandler.Handle(createCmd, default);

        Assert.Equal(2, recorder.Deposits.Count);
        Assert.Equal(recorder.Deposits[0] with { TraceId = null }, recorder.Deposits[1] with { TraceId = null });
        Assert.Equal($"hold:{created.Hold.HoldId:D}:Held", recorder.Deposits[0].SourceEventId);
        Assert.Equal("auc-999", recorder.Deposits[0].AuctionId);

        var captureHandler = new CaptureHoldCommandHandler(context, recorder);
        var orderId = Guid.NewGuid();
        await captureHandler.Handle(
            new CaptureHoldCommand("ORDER_PAYMENT", orderId, new[] { new CaptureTransfer("SYSTEM_ESCROW", null, 200_000m) })
            { HoldId = created.Hold.HoldId, IdempotencyKey = "cap-999" },
            default);
        Assert.Equal($"hold:{created.Hold.HoldId:D}:Captured", recorder.Deposits[2].SourceEventId);

        recorder.ShouldFail = true;
        var refundResult = await new RefundCapturedHoldCommandHandler(context, recorder).Handle(
            new RefundCapturedHoldCommand("SELLER_REJECTED", orderId)
            { HoldId = created.Hold.HoldId, IdempotencyKey = "ref-999" },
            default);

        Assert.Equal("Refunded", refundResult.Status);
        Assert.Equal(1_000_000m, refundResult.BuyerBalanceAfter);
        Assert.Equal($"hold:{created.Hold.HoldId:D}:Refunded", recorder.Deposits[3].SourceEventId);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.NotificationDispatchFailed));
    }

    [Fact]
    public async Task AuctionHold_ReleaseAndForfeit_EmitExpectedStatuses()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var bidderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        context.Wallets.AddRange(
            new WalletAccount { Id = Guid.NewGuid(), UserId = bidderId, Balance = 1_000_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" },
            new WalletAccount { Id = Guid.NewGuid(), UserId = sellerId, Balance = 0m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" });
        await context.SaveChangesAsync();

        var recorder = new RecordingNotificationClient();
        var create = new CreateHoldCommandHandler(context, recorder);
        var h1 = (await create.Handle(
            new CreateHoldCommand(bidderId, "AUCTION_DEPOSIT", "r-rel", 100_000m, "VND", null) { IdempotencyKey = "k-rel-1" },
            default)).Hold.HoldId;
        var h2 = (await create.Handle(
            new CreateHoldCommand(bidderId, "AUCTION_DEPOSIT", "r-forf", 100_000m, "VND", null) { IdempotencyKey = "k-forf-1" },
            default)).Hold.HoldId;

        await new ReleaseHoldCommandHandler(context, recorder).Handle(
            new ReleaseHoldCommand("BUYER_WITHDRAWN") { HoldId = h1, IdempotencyKey = "k-rel-2" },
            default);
        await new CaptureHoldCommandHandler(context, recorder).Handle(
            new CaptureHoldCommand("FORFEIT", null, new[] { new CaptureTransfer("SELLER_WALLET", sellerId, 100_000m) })
            { HoldId = h2, IdempotencyKey = "k-forf-2" },
            default);

        Assert.Contains(recorder.Deposits, e => e.SourceEventId == $"hold:{h1:D}:Released" && e.Status == "Released");
        Assert.Contains(recorder.Deposits, e => e.SourceEventId == $"hold:{h2:D}:Forfeited" && e.Status == "Forfeited");
    }

    private sealed class RecordingNotificationClient : INotificationClient
    {
        public bool ShouldFail { get; set; }
        public List<WithdrawalNotificationEvent> Withdrawals { get; } = new();
        public List<AuctionDepositNotificationEvent> Deposits { get; } = new();

        public Task<NotificationDispatchResult> SendWithdrawalEventAsync(
            WithdrawalNotificationEvent payload,
            CancellationToken cancellationToken = default)
        {
            Withdrawals.Add(payload);
            return Task.FromResult(ShouldFail
                ? new NotificationDispatchResult(false, 503, Error: "Simulated outage")
                : new NotificationDispatchResult(true, 200, "notif-wd"));
        }

        public Task<NotificationDispatchResult> SendAuctionDepositEventAsync(
            AuctionDepositNotificationEvent payload,
            CancellationToken cancellationToken = default)
        {
            Deposits.Add(payload);
            return Task.FromResult(ShouldFail
                ? new NotificationDispatchResult(false, 503, Error: "Simulated outage")
                : new NotificationDispatchResult(true, 200, "notif-dep"));
        }
    }

    private sealed class StubHttpHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastServiceKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastServiceKey = request.Headers.TryGetValues("X-Internal-Service-Key", out var vals)
                ? vals.FirstOrDefault()
                : null;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
