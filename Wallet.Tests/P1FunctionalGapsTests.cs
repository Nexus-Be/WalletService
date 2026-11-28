namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using Wallet.Api.Controllers;
using Wallet.Api.Middleware;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.AdminWallets.Commands.ResolveStripeAttempt;
using Wallet.Application.Features.AdminWallets.Queries.GetWalletReconciliation;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminWallets;
using Wallet.Application.Features.AdminWallets.Queries.ListAdminWithdrawals;
using Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;
using Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Checkout.Commands.RefundOrderPayment;
using Wallet.Application.Features.Checkout.Commands.ResumeDirectPayment;
using Wallet.Application.Features.Checkout.Commands.SettleSellerOrder;
using Wallet.Application.Features.Holds.Queries.ListMyHolds;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Application.Features.Topups.Commands.ResumeTopup;
using Wallet.Application.Features.Topups.Commands.TopupStripe;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.Wallets.Commands.UnfreezeWallet;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Wallets.Queries.GetTransactionById;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Payments;
using Wallet.Tests.Support;
using Xunit;

public sealed class P1FunctionalGapsTests
{
    private const string WebhookSecret = "whsec_test_only_not_real";

    private static WalletAccount AddWallet(
        TestWalletDbContext context,
        Guid? userId = null,
        decimal balance = 0m,
        decimal lockedBalance = 0m,
        CurrencyCode currency = CurrencyCode.Vnd,
        string status = "ACTIVE")
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Balance = balance,
            LockedBalance = lockedBalance,
            Currency = currency,
            Status = status
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    // ---- P1.A: Checkout & Escrow Polish ----

    [Fact]
    public async Task RefundOrderPayment_WithDistinctIdempotencyKeys_SupportsMultiplePartialRefundsAndReplaysSafely()
    {
        using var context = new TestWalletDbContext(nameof(RefundOrderPayment_WithDistinctIdempotencyKeys_SupportsMultiplePartialRefundsAndReplaysSafely));
        var buyer = AddWallet(context, balance: 200_000m);
        var orderId = Guid.NewGuid();

        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = buyer.UserId.ToString() },
            default);

        var handler = new RefundOrderPaymentCommandHandler(context);
        var first = await handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 30_000m, "Partial 1")
            {
                OrderId = orderId,
                IdempotencyKey = "ref-1"
            },
            default);
        var replayFirst = await handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 30_000m, "Partial 1")
            {
                OrderId = orderId,
                IdempotencyKey = "ref-1"
            },
            default);
        var second = await handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 70_000m, "Partial 2")
            {
                OrderId = orderId,
                IdempotencyKey = "ref-2"
            },
            default);

        Assert.Equal(first.Id, replayFirst.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(200_000m, buyer.Balance);
        var escrow = context.OrderEscrows.Single(e => e.OrderId == orderId);
        Assert.Equal(0m, escrow.RemainingAmount);
        Assert.Equal(OrderEscrowStatus.Refunded, escrow.Status);

        var reusedErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 10_000m, "Partial 1")
            {
                OrderId = orderId,
                IdempotencyKey = "ref-1"
            },
            default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", reusedErr.ErrorCode);
        Assert.Equal(409, reusedErr.StatusCode);

        var orderLegacy = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(50_000m) { OrderId = orderLegacy, UserId = buyer.UserId.ToString() },
            default);
        var legacy1 = await handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 20_000m) { OrderId = orderLegacy },
            default);
        var legacyReplay = await handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 20_000m) { OrderId = orderLegacy },
            default);
        Assert.Equal(legacy1.Id, legacyReplay.Id);

        var conflictErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 25_000m) { OrderId = orderLegacy },
            default));
        Assert.Equal("REFUND_CONFLICT", conflictErr.ErrorCode);
    }

    [Fact]
    public async Task AdjustOrderPayment_NegativeDelta_AllowsFrozenWallet_AndDistinguishesRefundedVsSettled()
    {
        using var context = new TestWalletDbContext(nameof(AdjustOrderPayment_NegativeDelta_AllowsFrozenWallet_AndDistinguishesRefundedVsSettled));
        var buyer = AddWallet(context, balance: 300_000m);
        var seller = AddWallet(context, balance: 0m);

        // Order A: partial refund (30k) then negative adjustment (-70k) without settlement -> Refunded
        var orderRefundOnly = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderRefundOnly, UserId = buyer.UserId.ToString() },
            default);
        await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 30_000m, "Partial refund")
            {
                OrderId = orderRefundOnly,
                IdempotencyKey = "refund-a"
            },
            default);

        buyer.Status = "FROZEN";
        await context.SaveChangesAsync();

        var positiveFrozenErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new AdjustOrderPaymentCommandHandler(context).Handle(
                new AdjustOrderPaymentCommand(buyer.UserId.ToString(), 5_000m, "Add shipping")
                {
                    OrderId = orderRefundOnly,
                    IdempotencyKey = "adj-pos-frozen"
                },
                default));
        Assert.Equal("WALLET_FROZEN", positiveFrozenErr.ErrorCode);

        await new AdjustOrderPaymentCommandHandler(context).Handle(
            new AdjustOrderPaymentCommand(buyer.UserId.ToString(), -70_000m, "Lower shipping")
            {
                OrderId = orderRefundOnly,
                IdempotencyKey = "adj-neg-frozen"
            },
            default);
        Assert.Equal(OrderEscrowStatus.Refunded, context.OrderEscrows.Single(e => e.OrderId == orderRefundOnly).Status);

        // Order B: partial settlement (40k) then negative adjustment (-60k) -> Settled
        buyer.Status = "ACTIVE";
        await context.SaveChangesAsync();

        var orderWithSettlement = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderWithSettlement, UserId = buyer.UserId.ToString() },
            default);
        await new SettleSellerOrderCommandHandler(context).Handle(
            new SettleSellerOrderCommand(seller.UserId.ToString(), 40_000m) { SubOrderId = orderWithSettlement },
            default);
        await new AdjustOrderPaymentCommandHandler(context).Handle(
            new AdjustOrderPaymentCommand(buyer.UserId.ToString(), -60_000m, "Discount after partial settle")
            {
                OrderId = orderWithSettlement,
                IdempotencyKey = "adj-neg-settled"
            },
            default);
        Assert.Equal(OrderEscrowStatus.Settled, context.OrderEscrows.Single(e => e.OrderId == orderWithSettlement).Status);
    }

    [Fact]
    public async Task PayOrderWithWallet_BlocksReplayAfterCancelOrFullRefund()
    {
        using var context = new TestWalletDbContext(nameof(PayOrderWithWallet_BlocksReplayAfterCancelOrFullRefund));
        var buyer = AddWallet(context, balance: 500_000m);
        var payHandler = new PayOrderWithWalletCommandHandler(context);

        var cancelledOrder = Guid.NewGuid();
        await payHandler.Handle(new PayOrderWithWalletCommand(100_000m) { OrderId = cancelledOrder, UserId = buyer.UserId.ToString() }, default);
        await new CancelOrderEscrowCommandHandler(context).Handle(
            new CancelOrderEscrowCommand("Cancel order") { OrderId = cancelledOrder, IdempotencyKey = "cancel-replay-check" },
            default);

        var cancelErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            payHandler.Handle(new PayOrderWithWalletCommand(100_000m) { OrderId = cancelledOrder, UserId = buyer.UserId.ToString() }, default));
        Assert.Equal("ORDER_ESCROW_CANCELLED", cancelErr.ErrorCode);
        Assert.Equal(409, cancelErr.StatusCode);

        var refundedOrder = Guid.NewGuid();
        await payHandler.Handle(new PayOrderWithWalletCommand(100_000m) { OrderId = refundedOrder, UserId = buyer.UserId.ToString() }, default);
        await new RefundOrderPaymentCommandHandler(context).Handle(
            new RefundOrderPaymentCommand(buyer.UserId.ToString(), 100_000m, "Full refund")
            {
                OrderId = refundedOrder,
                IdempotencyKey = "refund-replay-check"
            },
            default);

        var refundErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            payHandler.Handle(new PayOrderWithWalletCommand(100_000m) { OrderId = refundedOrder, UserId = buyer.UserId.ToString() }, default));
        Assert.Equal("ORDER_ESCROW_ALREADY_RESOLVED", refundErr.ErrorCode);
        Assert.Equal(409, refundErr.StatusCode);
    }

    // ---- P1.B: Stripe & Topups Polish ----

    [Fact]
    public async Task GetTransactionById_ResolvesStripeTopupPaymentIntent_AndCrossWalletAuctionForfeitHold()
    {
        using var context = new TestWalletDbContext(nameof(GetTransactionById_ResolvesStripeTopupPaymentIntent_AndCrossWalletAuctionForfeitHold));
        var buyer = AddWallet(context, balance: 100_000m);
        var seller = AddWallet(context, balance: 50_000m);

        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = buyer.Id, Amount = 100_000m, Status = TopupStatus.Success };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = buyer.UserId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = topup.Id,
            Amount = 100_000m,
            RequestFingerprint = "fp-1",
            ProviderIdempotencyKey = "pk-1",
            PaymentIntentId = "pi_test_topup_999",
            Status = StripePaymentAttemptStatus.Successful
        };
        var topupTx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = buyer.Id,
            UserId = buyer.UserId.ToString(),
            Type = WalletTransactionType.TopupStripe,
            Amount = 100_000m,
            Currency = "Vnd",
            BalanceBefore = 0m,
            BalanceAfter = 100_000m,
            Status = TransactionStatus.Success,
            ReferenceId = topup.Id
        };

        var hold = new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = buyer.Id,
            OwnerUserId = buyer.UserId,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "auction-deposit-42",
            Amount = 50_000m,
            MetadataJson = "{}",
            Status = AuctionHoldStatus.Captured
        };
        var forfeitTx = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = seller.Id,
            UserId = seller.UserId.ToString(),
            Type = WalletTransactionType.AuctionForfeitCredit,
            Amount = 50_000m,
            Currency = "Vnd",
            BalanceBefore = 0m,
            BalanceAfter = 50_000m,
            Status = TransactionStatus.Success,
            ReferenceId = hold.Id
        };

        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        context.AuctionHolds.Add(hold);
        context.WalletTransactions.AddRange(topupTx, forfeitTx);
        await context.SaveChangesAsync();

        var handler = new GetTransactionByIdQueryHandler(context);
        var topupDetail = await handler.Handle(new GetTransactionByIdQuery(topupTx.Id) { UserId = buyer.UserId }, default);
        Assert.Equal("Success", topupDetail.GatewayStatus);
        Assert.Equal("pi_test_topup_999", topupDetail.GatewayReference);

        var forfeitDetail = await handler.Handle(new GetTransactionByIdQuery(forfeitTx.Id) { UserId = seller.UserId }, default);
        Assert.Equal("Captured", forfeitDetail.GatewayStatus);
        Assert.Equal("auction-deposit-42", forfeitDetail.GatewayReference);
    }

    [Fact]
    public async Task StripeCheckoutClient_SetsExpiresAt_DirectReturnUrlContext_AndMaps4xxRejection()
    {
        var handler = new CapturingStripeHttpHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_not-a-real-key",
            ["Stripe:SuccessUrl"] = "https://example.test/result?checkout=return",
            ["Stripe:CancelUrl"] = "https://example.test/result?checkout=cancel",
            ["Stripe:SessionExpiryMinutes"] = "45"
        }).Build();
        var client = new StripeCheckoutClient(http, config);
        var orderId = Guid.NewGuid();
        var explicitExpiry = DateTimeOffset.UtcNow.AddMinutes(60);

        await client.CreateSessionAsync(
            new StripeCheckoutRequest(Guid.NewGuid(), 100_000, "direct-key-1", OrderId: orderId, ExpiresAt: explicitExpiry),
            default);

        var decodedBody = WebUtility.UrlDecode(handler.LastBody);
        Assert.Contains("kind=direct", decodedBody);
        Assert.Contains($"orderId={orderId:D}", decodedBody);
        Assert.Contains($"expires_at={explicitExpiry.ToUnixTimeSeconds()}", decodedBody);

        var badReqHandler = new CapturingStripeHttpHandler(HttpStatusCode.BadRequest);
        using var badHttp = new HttpClient(badReqHandler);
        var badClient = new StripeCheckoutClient(badHttp, config);
        var err = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            badClient.CreateSessionAsync(new StripeCheckoutRequest(Guid.NewGuid(), 100_000, "bad-req-1"), default));
        Assert.Equal("STRIPE_REQUEST_REJECTED", err.ErrorCode);
        Assert.Equal(400, err.StatusCode);
    }

    [Fact]
    public async Task ReconcilePendingTopups_IsolatesPerCandidateErrorsAndContinuesRemainingBatch()
    {
        using var context = new TestWalletDbContext(nameof(ReconcilePendingTopups_IsolatesPerCandidateErrorsAndContinuesRemainingBatch));
        var w1 = AddWallet(context);
        var w2 = AddWallet(context);
        var firstTopup = new WalletTopup { Id = Guid.NewGuid(), WalletId = w1.Id, Amount = 100_000m, Status = TopupStatus.Pending };
        var secondTopup = new WalletTopup { Id = Guid.NewGuid(), WalletId = w2.Id, Amount = 200_000m, Status = TopupStatus.Pending };
        var firstAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = w1.UserId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = firstTopup.Id,
            Amount = 100_000m,
            RequestFingerprint = "fp1",
            ProviderIdempotencyKey = "pk1",
            CheckoutSessionId = "cs_test_fail_1",
            Status = StripePaymentAttemptStatus.AwaitingPayment,
            BlocksReplacement = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var secondAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = w2.UserId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = secondTopup.Id,
            Amount = 200_000m,
            RequestFingerprint = "fp2",
            ProviderIdempotencyKey = "pk2",
            CheckoutSessionId = "cs_test_ok_2",
            Status = StripePaymentAttemptStatus.AwaitingPayment,
            BlocksReplacement = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        context.WalletTopups.AddRange(firstTopup, secondTopup);
        context.StripePaymentAttempts.AddRange(firstAttempt, secondAttempt);
        await context.SaveChangesAsync();

        var provider = new MixedReconcileProvider(firstAttempt.CheckoutSessionId!, secondAttempt);
        var processed = await new ReconcilePendingTopupsCommandHandler(
                context, provider, NullLogger<ReconcilePendingTopupsCommandHandler>.Instance)
            .Handle(new ReconcilePendingTopupsCommand(), default);

        Assert.Equal(1, processed);
        Assert.Equal(0m, w1.Balance);
        Assert.Equal(200_000m, w2.Balance);
        Assert.Equal(TopupStatus.Success, secondTopup.Status);
    }

    [Fact]
    public async Task StripeWebhookProcessing_AuditsFailureAndDisputeEvents_AndDeduplicatesReplays()
    {
        using var context = new TestWalletDbContext(nameof(StripeWebhookProcessing_AuditsFailureAndDisputeEvents_AndDeduplicatesReplays));
        var wallet = AddWallet(context);
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = wallet.UserId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = Guid.NewGuid(),
            Amount = 100_000m,
            RequestFingerprint = "fp",
            ProviderIdempotencyKey = "pk"
        };
        var verifier = new StripeWebhookVerifier(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = WebhookSecret }).Build());
        var payload = BuildWebhookJson("evt_dispute_1", "charge.dispute.created", attempt.Id);
        var sig = ComputeSignature(payload);
        var provider = new MixedReconcileProvider("none", attempt);

        var firstOutcome = await StripeWebhookProcessing.ProcessAsync(
            context, verifier, provider, Encoding.UTF8.GetBytes(payload), sig, default, NullLogger.Instance);
        var secondOutcome = await StripeWebhookProcessing.ProcessAsync(
            context, verifier, provider, Encoding.UTF8.GetBytes(payload), sig, default, NullLogger.Instance);

        Assert.Equal(StripeWebhookProcessingOutcome.Ignored, firstOutcome);
        Assert.Equal(StripeWebhookProcessingOutcome.DuplicateEvent, secondOutcome);
        var audited = Assert.Single(context.StripeWebhookEvents);
        Assert.Equal("evt_dispute_1", audited.ProviderEventId);
        Assert.Equal("charge.dispute.created", audited.EventType);
        Assert.Equal("AuditedIgnored", audited.Outcome);
        Assert.Null(audited.PaymentAttemptId);
    }

    [Fact]
    public async Task GlobalExceptionHandlerMiddleware_MapsStripeWebhookVersionExceptionTo400()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new StripeWebhookVersionException(new InvalidOperationException("Old API version")),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        httpContext.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal("VAL_UNSUPPORTED_WEBHOOK_VERSION", doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ResolveStripeAttempt_ExpiredSetsParentExpired_AndAuditsOriginalBlocksReplacement()
    {
        using var context = new TestWalletDbContext(nameof(ResolveStripeAttempt_ExpiredSetsParentExpired_AndAuditsOriginalBlocksReplacement));
        var wallet = AddWallet(context);
        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100_000m, Status = TopupStatus.Pending };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = wallet.UserId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = topup.Id,
            Amount = 100_000m,
            RequestFingerprint = "fp",
            ProviderIdempotencyKey = "pk",
            Status = StripePaymentAttemptStatus.ReconciliationRequired,
            BlocksReplacement = false
        };
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();

        var resolved = await new ResolveStripeAttemptCommandHandler(context).Handle(
            new ResolveStripeAttemptCommand("Expired", "Confirmed expired in Stripe")
            {
                AttemptId = attempt.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal("Expired", resolved.Status);
        Assert.Equal(TopupStatus.Expired, topup.Status);
        var audit = Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.StripeAttemptResolve));
        Assert.Contains("\"BlocksReplacement\":false", audit.OldValues);
    }

    // ---- P1.C: Auction Holds Polish ----

    [Fact]
    public async Task ListMyHolds_ParsesCaseInsensitiveStatus_ToleratesMalformedMetadata_AndReturnsResolvedAt()
    {
        using var context = new TestWalletDbContext(nameof(ListMyHolds_ParsesCaseInsensitiveStatus_ToleratesMalformedMetadata_AndReturnsResolvedAt));
        var wallet = AddWallet(context);
        var resolvedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        context.AuctionHolds.Add(new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            OwnerUserId = wallet.UserId,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "bid-1",
            Amount = 50_000m,
            MetadataJson = "not-valid-json",
            Status = AuctionHoldStatus.Released,
            ResolvedAt = resolvedAt
        });
        await context.SaveChangesAsync();

        var page = await new ListMyHoldsQueryHandler(context).Handle(
            new ListMyHoldsQuery(1, 10, Status: "released") { UserId = wallet.UserId },
            default);

        var row = Assert.Single(page.Items);
        Assert.Null(row.AuctionId);
        Assert.Equal("Released", row.Status);
        Assert.Equal(resolvedAt, row.ResolvedAt);
    }

    // ---- P1.D: Withdrawals & Bank Accounts Polish ----

    [Fact]
    public async Task RequestWithdrawal_EnforcesVndWallet_ExcludesDualBankInputs_AndValidatesTrimmedAccountNumber()
    {
        using var context = new TestWalletDbContext(nameof(RequestWithdrawal_EnforcesVndWallet_ExcludesDualBankInputs_AndValidatesTrimmedAccountNumber));
        var usdWallet = AddWallet(context, balance: 500_000m, currency: CurrencyCode.Usd);
        var vndWallet = AddWallet(context, balance: 500_000m, currency: CurrencyCode.Vnd);
        var handler = new RequestWithdrawalCommandHandler(context);

        var usdErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
            {
                UserId = usdWallet.UserId.ToString(),
                IdempotencyKey = "wd-usd-1"
            },
            default));
        Assert.Equal("VAL_INVALID_CURRENCY", usdErr.ErrorCode);

        var dualErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller", BankAccountId: Guid.NewGuid())
            {
                UserId = vndWallet.UserId.ToString(),
                IdempotencyKey = "wd-dual-1"
            },
            default));
        Assert.Equal("VAL_INVALID_INPUT", dualErr.ErrorCode);

        var shortManualCmd = new RequestWithdrawalCommand(100_000m, "VND", "VCB", " 12 ", "Seller")
        {
            UserId = vndWallet.UserId.ToString(),
            IdempotencyKey = "wd-short-1"
        };
        Assert.False(RequestValidation.TryValidate(shortManualCmd, new List<ValidationResult>()));
        await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(shortManualCmd, default));
    }

    [Fact]
    public async Task ProcessWithdrawal_EnforcesFreezeOnApproveComplete_UsesSpecific404_AndClearsRefOnFail()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_EnforcesFreezeOnApproveComplete_UsesSpecific404_AndClearsRefOnFail));
        var adminId = Guid.NewGuid().ToString();
        var handler = new ProcessWithdrawalCommandHandler(context);

        var notFoundErr = await Assert.ThrowsAsync<EntityNotFoundException>(() => handler.Handle(
            new ProcessWithdrawalCommand("Approve") { WithdrawalId = Guid.NewGuid(), AdminUserId = adminId, ActorIsAdmin = true },
            default));
        Assert.Equal("WITHDRAWAL_NOT_FOUND", notFoundErr.ErrorCode);

        var wallet = AddWallet(context, balance: 500_000m, lockedBalance: 200_000m, status: "ACTIVE");
        var pending = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller",
            Status = WithdrawalStatus.Pending
        };
        var approved = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller",
            Status = WithdrawalStatus.Approved,
            GatewayReferenceId = "BANK-REF-FAIL-1"
        };
        context.WithdrawalRequests.AddRange(pending, approved);
        await context.SaveChangesAsync();

        wallet.Status = "FROZEN";
        await context.SaveChangesAsync();

        var approveFrozen = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new ProcessWithdrawalCommand("Approve", TransferReference: "BANK-REF-1")
            {
                WithdrawalId = pending.Id,
                AdminUserId = adminId,
                ActorIsAdmin = true
            },
            default));
        Assert.Equal("WALLET_FROZEN", approveFrozen.ErrorCode);

        var completeFrozen = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, TransferReference: "BANK-REF-FAIL-1")
            {
                WithdrawalId = approved.Id,
                AdminUserId = adminId,
                ActorIsAdmin = true
            },
            default));
        Assert.Equal("WALLET_FROZEN", completeFrozen.ErrorCode);

        await handler.Handle(
            new ProcessWithdrawalCommand("Fail", AdminNote: "Bank rejected account number")
            {
                WithdrawalId = approved.Id,
                AdminUserId = adminId,
                ActorIsAdmin = true
            },
            default);
        Assert.Null(approved.GatewayReferenceId);
        var failAudit = Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WithdrawalFail));
        Assert.Contains("BANK-REF-FAIL-1", failAudit.OldValues);
    }

    [Fact]
    public async Task BankAccounts_RejectDuplicate_MaskAccountNumber_ValidateTrimmedLength_AndCaseInsensitiveDeleteGuard()
    {
        using var context = new TestWalletDbContext(nameof(BankAccounts_RejectDuplicate_MaskAccountNumber_ValidateTrimmedLength_AndCaseInsensitiveDeleteGuard));
        var wallet = AddWallet(context, balance: 500_000m);
        var createHandler = new CreateBankAccountCommandHandler(context);

        var badTrimmed = new CreateBankAccountCommand("VCB", " 12 ", "Seller") { UserId = wallet.UserId };
        Assert.False(RequestValidation.TryValidate(badTrimmed, new List<ValidationResult>()));
        await Assert.ThrowsAsync<BusinessRuleValidationException>(() => createHandler.Handle(badTrimmed, default));

        var created = await createHandler.Handle(
            new CreateBankAccountCommand("vcb", "1234567890", "Seller One", IsDefault: true) { UserId = wallet.UserId },
            default);
        Assert.Equal("******7890", created.AccountNumber);

        var dupErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => createHandler.Handle(
            new CreateBankAccountCommand("VCB", "1234567890", "Seller One", IsDefault: false) { UserId = wallet.UserId },
            default));
        Assert.Equal("BANK_ACCOUNT_DUPLICATE", dupErr.ErrorCode);
        Assert.Equal(409, dupErr.StatusCode);

        context.WithdrawalRequests.Add(new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            BankAccountId = null,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller One",
            Status = WithdrawalStatus.Pending
        });
        await context.SaveChangesAsync();

        var inUseErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new DeleteBankAccountCommandHandler(context).Handle(
                new DeleteBankAccountCommand(created.Id) { UserId = wallet.UserId },
                default));
        Assert.Equal("BANK_ACCOUNT_IN_USE", inUseErr.ErrorCode);
    }

    // ---- P1.E: Transfers Hardening ----

    [Fact]
    public async Task Transfer_SupportsIdempotencyKey_SharedReferenceId_AuditActionTransfer_AndNoteMaxLength()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_SupportsIdempotencyKey_SharedReferenceId_AuditActionTransfer_AndNoteMaxLength));
        var sender = AddWallet(context, balance: 200_000m);
        var recipient = AddWallet(context, balance: 50_000m);
        var handler = new TransferCommandHandler(context);

        var overlongNoteCmd = new TransferCommand(recipient.UserId.ToString(), 10_000m, new string('N', 501))
        {
            SenderUserId = sender.UserId.ToString()
        };
        Assert.False(RequestValidation.TryValidate(overlongNoteCmd, new List<ValidationResult>()));

        var first = await handler.Handle(
            new TransferCommand(recipient.UserId.ToString(), 25_000m, "Shared rent")
            {
                SenderUserId = sender.UserId.ToString(),
                IdempotencyKey = "tr-1"
            },
            default);
        var replay = await handler.Handle(
            new TransferCommand(recipient.UserId.ToString(), 25_000m, "Shared rent")
            {
                SenderUserId = sender.UserId.ToString(),
                IdempotencyKey = "tr-1"
            },
            default);

        Assert.Equal(first.TransactionId, replay.TransactionId);
        Assert.Equal(175_000m, sender.Balance);
        Assert.Equal(75_000m, recipient.Balance);

        var transferOut = context.WalletTransactions.Single(t => t.Type == WalletTransactionType.TransferOut);
        var transferIn = context.WalletTransactions.Single(t => t.Type == WalletTransactionType.TransferIn);
        Assert.NotNull(transferOut.ReferenceId);
        Assert.Equal(transferOut.ReferenceId, transferIn.ReferenceId);
        Assert.Equal("tr-1", transferOut.ClientIdempotencyKey);
        Assert.Null(transferIn.ClientIdempotencyKey);
        Assert.Equal(transferOut.CreatedAt, transferIn.CreatedAt);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.Transfer));

        var reuseErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new TransferCommand(recipient.UserId.ToString(), 30_000m, "Shared rent")
            {
                SenderUserId = sender.UserId.ToString(),
                IdempotencyKey = "tr-1"
            },
            default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", reuseErr.ErrorCode);
    }

    // ---- P1.F: Freeze, Read-Only Lookups, Reconciliation, Adjustments & Queries ----

    [Fact]
    public async Task FrozenWallet_BlocksStripeTopupAndDirectCheckoutAndResumeAfter404Check()
    {
        using var context = new TestWalletDbContext(nameof(FrozenWallet_BlocksStripeTopupAndDirectCheckoutAndResumeAfter404Check));
        var ownerWallet = AddWallet(context, status: "FROZEN");
        var provider = new CapturingStubProvider();

        var topupErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new TopupStripeCommandHandler(context, provider).Handle(
                new TopupStripeCommand(50_000m) { UserId = ownerWallet.UserId, IdempotencyKey = "fr-topup-1" },
                default));
        Assert.Equal("WALLET_FROZEN", topupErr.ErrorCode);

        var directErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new CheckoutDirectCommandHandler(context, provider).Handle(
                new CheckoutDirectCommand(Guid.NewGuid().ToString(), 50_000m) { UserId = ownerWallet.UserId },
                default));
        Assert.Equal("WALLET_FROZEN", directErr.ErrorCode);

        // 404 takes precedence for non-owner before freeze check on ResumeTopup & ResumeDirectPayment
        var missingTopup = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new ResumeTopupCommandHandler(context, provider).Handle(
                new ResumeTopupCommand(Guid.NewGuid()) { UserId = ownerWallet.UserId },
                default));
        Assert.Equal("PAYMENT_NOT_FOUND", missingTopup.ErrorCode);

        var topup = new WalletTopup { Id = Guid.NewGuid(), WalletId = ownerWallet.Id, Amount = 50_000m, Status = TopupStatus.Pending };
        context.WalletTopups.Add(topup);
        await context.SaveChangesAsync();
        var resumeTopupFrozen = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new ResumeTopupCommandHandler(context, provider).Handle(
                new ResumeTopupCommand(topup.Id) { UserId = ownerWallet.UserId },
                default));
        Assert.Equal("WALLET_FROZEN", resumeTopupFrozen.ErrorCode);

        var directOrder = new DirectPaymentOrder { Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), Amount = 50_000m, Status = TopupStatus.Pending };
        var directAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerWallet.UserId,
            Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = directOrder.Id,
            Amount = 50_000m,
            RequestFingerprint = "fp",
            ProviderIdempotencyKey = "pk"
        };
        context.DirectPaymentOrders.Add(directOrder);
        context.StripePaymentAttempts.Add(directAttempt);
        await context.SaveChangesAsync();

        var resumeDirectFrozen = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new ResumeDirectPaymentCommandHandler(context, provider).Handle(
                new ResumeDirectPaymentCommand(directOrder.OrderId) { UserId = ownerWallet.UserId },
                default));
        Assert.Equal("WALLET_FROZEN", resumeDirectFrozen.ErrorCode);
    }

    [Fact]
    public async Task UnfreezeAndReadOnlyAdminInternalLookups_Return404WithoutCreatingPhantomWallets()
    {
        using var context = new TestWalletDbContext(nameof(UnfreezeAndReadOnlyAdminInternalLookups_Return404WithoutCreatingPhantomWallets));
        var missingUserId = Guid.NewGuid();

        await Assert.ThrowsAsync<WalletNotFoundException>(() =>
            new UnfreezeWalletCommandHandler(context).Handle(
                new UnfreezeWalletCommand(missingUserId, "Dispute cleared") { ActorUserId = Guid.NewGuid().ToString() },
                default));
        Assert.Empty(context.Wallets);

        var senderProxy = new RecordingSenderProxy();
        var adminController = new AdminWalletsController(senderProxy, NullLogger<AdminWalletsController>.Instance);
        var walletsController = new WalletsController(senderProxy, NullLogger<WalletsController>.Instance);

        await Assert.ThrowsAsync<WalletNotFoundException>(() =>
            new GetMyWalletQueryHandler(context, senderProxy).Handle(
                new GetMyWalletQuery(missingUserId.ToString()) { AutoCreate = false },
                default));
        Assert.Empty(context.Wallets);

        await adminController.GetUserWallet(missingUserId, default);
        var adminSent = Assert.IsType<GetMyWalletQuery>(senderProxy.LastSent);
        Assert.False(adminSent.AutoCreate);

        await walletsController.GetUserWalletInternal(missingUserId, default);
        var internalSent = Assert.IsType<GetMyWalletQuery>(senderProxy.LastSent);
        Assert.False(internalSent.AutoCreate);
    }

    [Fact]
    public async Task GetWalletReconciliation_ChecksLockedBalance_ExcludesDirectGateway_AndFiltersByUserAndResult()
    {
        using var context = new TestWalletDbContext(nameof(GetWalletReconciliation_ChecksLockedBalance_ExcludesDirectGateway_AndFiltersByUserAndResult));
        var balanced = AddWallet(context, balance: 100_000m, lockedBalance: 50_000m);
        context.WalletTransactions.AddRange(
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = balanced.Id,
                Type = WalletTransactionType.TopupStripe,
                Amount = 100_000m,
                Currency = "Vnd",
                BalanceBefore = 0m,
                BalanceAfter = 100_000m,
                Status = TransactionStatus.Success,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            },
            // Zero-delta DirectGatewayPayment with stale BalanceAfter should be ignored
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = balanced.Id,
                Type = WalletTransactionType.DirectGatewayPayment,
                Amount = 50_000m,
                Currency = "Vnd",
                BalanceBefore = 0m,
                BalanceAfter = 0m,
                Status = TransactionStatus.Success,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        context.AuctionHolds.Add(new AuctionHold
        {
            Id = Guid.NewGuid(),
            WalletId = balanced.Id,
            OwnerUserId = balanced.UserId,
            ReferenceType = "AUCTION_DEPOSIT",
            ReferenceId = "ref-balanced",
            Amount = 30_000m,
            MetadataJson = "{}",
            Status = AuctionHoldStatus.Held
        });
        context.WithdrawalRequests.Add(new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = balanced.Id,
            Amount = 20_000m,
            NetAmount = 20_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Holder",
            Status = WithdrawalStatus.Pending
        });

        var nonZeroNoHistory = AddWallet(context, balance: 25_000m, lockedBalance: 0m);
        await context.SaveChangesAsync();

        var handler = new GetWalletReconciliationQueryHandler(context);
        var discrepancies = await handler.Handle(
            new GetWalletReconciliationQuery(UserId: nonZeroNoHistory.UserId, Result: "Discrepancy"),
            default);
        var discItem = Assert.Single(discrepancies.Items);
        Assert.Equal("Discrepancy", discItem.Result);
        Assert.Equal(nonZeroNoHistory.UserId, discItem.UserId);

        var balancedPage = await handler.Handle(
            new GetWalletReconciliationQuery(UserId: balanced.UserId, Result: "Balanced"),
            default);
        var balItem = Assert.Single(balancedPage.Items);
        Assert.Equal(1, balItem.SuccessfulTransactionCount);
        Assert.Equal(50_000m, balItem.ExpectedLockedBalance);
        Assert.Equal(0m, balItem.LockedDifference);
    }

    [Fact]
    public async Task AdjustWalletBalance_EnforcesIdempotency_Bounds_AndAuditsReason()
    {
        using var context = new TestWalletDbContext(nameof(AdjustWalletBalance_EnforcesIdempotency_Bounds_AndAuditsReason));
        var wallet = AddWallet(context, balance: 100_000m, lockedBalance: 20_000m);
        var adminId = Guid.NewGuid().ToString();
        var handler = new AdjustWalletBalanceCommandHandler(context);

        var overMaxCmd = new AdjustWalletBalanceCommand(500_000_001m, "Credit", "Over ceiling")
        {
            UserId = wallet.UserId,
            AdminUserId = adminId
        };
        Assert.False(RequestValidation.TryValidate(overMaxCmd, new List<ValidationResult>()));
        await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(overMaxCmd, default));

        var first = await handler.Handle(
            new AdjustWalletBalanceCommand(50_000m, "Credit", "Compensation bonus")
            {
                UserId = wallet.UserId,
                AdminUserId = adminId,
                IdempotencyKey = "adj-admin-1"
            },
            default);
        var replay = await handler.Handle(
            new AdjustWalletBalanceCommand(50_000m, "Credit", "Compensation bonus")
            {
                UserId = wallet.UserId,
                AdminUserId = adminId,
                IdempotencyKey = "adj-admin-1"
            },
            default);

        Assert.Equal(first.AdjustmentId, replay.AdjustmentId);
        Assert.Equal(150_000m, first.BalanceAfter);
        Assert.Equal("Credit", first.AdjustmentType);
        Assert.Equal("Vnd", first.Currency);
        Assert.Equal(130_000m, first.AvailableBalance);

        var audit = Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.BalanceAdjustment));
        Assert.Contains("Compensation bonus", audit.NewValues);

        var reuseErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new AdjustWalletBalanceCommand(10_000m, "Credit", "Compensation bonus")
            {
                UserId = wallet.UserId,
                AdminUserId = adminId,
                IdempotencyKey = "adj-admin-1"
            },
            default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", reuseErr.ErrorCode);
    }

    [Fact]
    public async Task ListingQueries_SupportExpandedAliasesAndFilters()
    {
        using var context = new TestWalletDbContext(nameof(ListingQueries_SupportExpandedAliasesAndFilters));
        var w1 = AddWallet(context, balance: 100_000m, currency: CurrencyCode.Vnd, status: "ACTIVE");
        var w2 = AddWallet(context, balance: 50m, currency: CurrencyCode.Usd, status: "FROZEN");
        var refA = Guid.NewGuid();
        var withdrawalId = Guid.NewGuid();

        context.WalletTransactions.AddRange(
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = w1.Id,
                UserId = w1.UserId.ToString(),
                Type = WalletTransactionType.TransferOut,
                Amount = 10_000m,
                Currency = null,
                Status = TransactionStatus.Success,
                ReferenceId = refA
            },
            new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = w1.Id,
                UserId = w1.UserId.ToString(),
                Type = WalletTransactionType.AuctionHoldCapture,
                Amount = 20_000m,
                Currency = "Vnd",
                Status = TransactionStatus.Success,
                ReferenceId = Guid.NewGuid()
            });
        context.WithdrawalRequests.Add(new WithdrawalRequest
        {
            Id = withdrawalId,
            WalletId = w1.Id,
            Amount = 50_000m,
            NetAmount = 50_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Seller",
            Status = WithdrawalStatus.Completed,
            GatewayReferenceId = "GW-TX-99",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        });
        await context.SaveChangesAsync();

        var txHandler = new ListTransactionsQueryHandler(context);
        var transferPage = await txHandler.Handle(
            new ListTransactionsQuery(Type: "TRANSFER", Status: "Success", ReferenceId: refA) { UserId = w1.UserId.ToString() },
            default);
        var transferRow = Assert.Single(transferPage.Items);
        Assert.Equal("Vnd", transferRow.Currency);

        var paymentPage = await txHandler.Handle(
            new ListTransactionsQuery(Type: "PAYMENT") { UserId = w1.UserId.ToString() },
            default);
        Assert.Single(paymentPage.Items);

        var adminWallets = await new ListAdminWalletsQueryHandler(context).Handle(
            new ListAdminWalletsQuery(Currency: "Usd", Search: w2.UserId.ToString()[..8]),
            default);
        Assert.Equal(w2.UserId.ToString(), Assert.Single(adminWallets.Items).UserId);

        var ledgerHandler = new ListWalletLedgerQueryHandler(context);
        var ledgerErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            ledgerHandler.Handle(new ListWalletLedgerQuery(Type: "NO_SUCH_TYPE"), default));
        Assert.Equal("VAL_INVALID_TYPE", ledgerErr.ErrorCode);

        var ledgerPage = await ledgerHandler.Handle(
            new ListWalletLedgerQuery(UserId: w1.UserId, ReferenceId: refA),
            default);
        var ledgerEntry = Assert.Single(ledgerPage.Items);
        Assert.Equal("Vnd", ledgerEntry.Currency);
        Assert.Equal(w1.UserId.ToString(), ledgerEntry.UserId);

        var wdPage = await new ListAdminWithdrawalsQueryHandler(context).Handle(
            new ListAdminWithdrawalsQuery(UserId: w1.UserId, TransferReference: "GW-TX-99"),
            default);
        Assert.Equal(withdrawalId, Assert.Single(wdPage.Items).WithdrawalId);
    }

    private static string BuildWebhookJson(string eventId, string type, Guid attemptId) =>
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
                    id = "cs_test_evt",
                    @object = "checkout.session",
                    metadata = new { attempt_id = attemptId.ToString("D") }
                }
            }
        });

    private static string ComputeSignature(string body)
    {
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"t={time},v1={EventUtility.ComputeSignature(WebhookSecret, time, body)}";
    }

    private sealed class CapturingStripeHttpHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var decoded = WebUtility.UrlDecode(LastBody);
            var reference = decoded.Split('&')
                .Single(part => part.StartsWith("client_reference_id=", StringComparison.Ordinal))
                .Split('=', 2)[1];
            var json = statusCode == HttpStatusCode.OK
                ? JsonSerializer.Serialize(new
                {
                    id = "cs_test_created_1",
                    @object = "checkout.session",
                    url = "https://checkout.stripe.com/test",
                    expires_at = 1900000000,
                    livemode = false,
                    mode = "payment",
                    currency = "vnd",
                    amount_total = 100000,
                    client_reference_id = reference,
                    after_expiration = new { recovery = new { enabled = false } },
                    metadata = new Dictionary<string, string> { ["attempt_id"] = reference }
                })
                : """{"error":{"message":"Amount too small","type":"invalid_request_error","code":"amount_too_small"}}""";
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CapturingStubProvider : IStripeCheckoutClient
    {
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new StripeCheckoutSession("cs_test_stub", "https://checkout.stripe.com/stub", DateTimeOffset.UtcNow.AddMinutes(30)));

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(new StripeSessionSnapshot(sessionId, "open", "unpaid", 50_000, null, Guid.Empty.ToString("D"), Guid.Empty.ToString("D"), false));

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class MixedReconcileProvider(string failingSessionId, StripePaymentAttempt passingAttempt) : IStripeCheckoutClient
    {
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (sessionId == failingSessionId)
            {
                throw new PaymentProviderUnavailableException(new HttpRequestException("Transient outage"));
            }

            return Task.FromResult(new StripeSessionSnapshot(
                sessionId,
                "complete",
                "paid",
                decimal.ToInt64(passingAttempt.Amount),
                "pi_test_mixed_2",
                passingAttempt.Id.ToString("D"),
                passingAttempt.Id.ToString("D"),
                false));
        }

        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(new StripePaymentIntentSnapshot(
                paymentIntentId,
                "succeeded",
                decimal.ToInt64(passingAttempt.Amount),
                decimal.ToInt64(passingAttempt.Amount),
                passingAttempt.Id.ToString("D")));
    }

    private sealed class RecordingSenderProxy : ISender
    {
        public object? LastSent { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastSent = request;
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastSent = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastSent = request;
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
