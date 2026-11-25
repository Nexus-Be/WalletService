namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Controllers;
using Wallet.Api.Filters;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using Wallet.Application.Features.Topups.Commands.TopupStripe;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.Wallets.Commands.FreezeWallet;
using Wallet.Application.Features.Wallets.Commands.UnfreezeWallet;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Payments;
using Wallet.Tests.Support;
using Xunit;

public sealed class WalletFreezeAndSecurityTests
{
    [Fact]
    public async Task FreezeAndUnfreeze_AutoCreatesWallet_IsIdempotent_AndAuditsOnlyOnStateChange()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        var freezeHandler = new FreezeWalletCommandHandler(context);
        var unfreezeHandler = new UnfreezeWalletCommandHandler(context);

        var frozen1 = await freezeHandler.Handle(new FreezeWalletCommand(userId, "CHARGEBACK"), default);
        var frozen2 = await freezeHandler.Handle(new FreezeWalletCommand(userId, "CHARGEBACK"), default);

        Assert.Equal("Frozen", frozen1.Status);
        Assert.Equal("Frozen", frozen2.Status);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WalletCreate));
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WalletFrozen));

        var unfrozen1 = await unfreezeHandler.Handle(new UnfreezeWalletCommand(userId, null), default);
        var unfrozen2 = await unfreezeHandler.Handle(new UnfreezeWalletCommand(userId, null), default);

        Assert.Equal("Active", unfrozen1.Status);
        Assert.Equal("DISPUTE_RESOLVED", unfrozen1.Reason);
        Assert.Equal("Active", unfrozen2.Status);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WalletUnfrozen));
    }

    [Fact]
    public async Task FrozenWallet_BlocksPayOrderTransferRequestWithdrawalAndAdjustOrderPayment()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var frozenUser = Guid.NewGuid();
        var activeUser = Guid.NewGuid();
        var frozenWallet = AddWallet(context, frozenUser, 500_000m, status: "FROZEN");
        var activeWallet = AddWallet(context, activeUser, 500_000m, status: "ACTIVE");

        var payErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new PayOrderWithWalletCommandHandler(context).Handle(
                new PayOrderWithWalletCommand(50_000m) { OrderId = Guid.NewGuid(), UserId = frozenUser.ToString() },
                default));
        Assert.Equal("WALLET_FROZEN", payErr.ErrorCode);
        Assert.Equal(403, payErr.StatusCode);

        var transferSenderErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new TransferCommandHandler(context).Handle(
                new TransferCommand(activeUser.ToString(), 50_000m) { SenderUserId = frozenUser.ToString() },
                default));
        Assert.Equal("WALLET_FROZEN", transferSenderErr.ErrorCode);
        Assert.Equal(403, transferSenderErr.StatusCode);

        var transferRecipientErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new TransferCommandHandler(context).Handle(
                new TransferCommand(frozenUser.ToString(), 50_000m) { SenderUserId = activeUser.ToString() },
                default));
        Assert.Equal("WALLET_FROZEN", transferRecipientErr.ErrorCode);
        Assert.Equal(403, transferRecipientErr.StatusCode);

        var wdErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new RequestWithdrawalCommandHandler(context).Handle(
                new RequestWithdrawalCommand(100_000m, "VND", "VCB", "1234567890", "Seller")
                { UserId = frozenUser.ToString(), IdempotencyKey = "frozen-wd" },
                default));
        Assert.Equal("WALLET_FROZEN", wdErr.ErrorCode);
        Assert.Equal(403, wdErr.StatusCode);

        // Seed an order paid by activeUser then freeze activeUser and try AdjustOrderPayment
        var orderId = Guid.NewGuid();
        await new PayOrderWithWalletCommandHandler(context).Handle(
            new PayOrderWithWalletCommand(100_000m) { OrderId = orderId, UserId = activeUser.ToString() },
            default);
        activeWallet.Freeze();
        await context.SaveChangesAsync();

        var adjustErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new AdjustOrderPaymentCommandHandler(context).Handle(
                new AdjustOrderPaymentCommand(activeUser.ToString(), 20_000m, "Shipping delta")
                { OrderId = orderId, IdempotencyKey = "adj-frozen" },
                default));
        Assert.Equal("WALLET_FROZEN", adjustErr.ErrorCode);
        Assert.Equal(403, adjustErr.StatusCode);

        var stripeClient = new FakeStripeCheckoutClient();
        var topupFrozen = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new TopupStripeCommandHandler(context, stripeClient).Handle(
                new TopupStripeCommand(50_000m) { UserId = frozenUser, IdempotencyKey = "freeze-topup" },
                default));
        Assert.Equal("WALLET_FROZEN", topupFrozen.ErrorCode);
        Assert.Equal(403, topupFrozen.StatusCode);

        var directFrozen = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new CheckoutDirectCommandHandler(context, stripeClient).Handle(
                new CheckoutDirectCommand(Guid.NewGuid().ToString(), 50_000m) { UserId = frozenUser },
                default));
        Assert.Equal("WALLET_FROZEN", directFrozen.ErrorCode);
        Assert.Equal(403, directFrozen.StatusCode);

        // Frozen wallet blocks Approve/Complete withdrawals but permits Reject/Fail so locked or debited funds return safely.
        frozenWallet.Unfreeze();
        frozenWallet.LockFunds(200_000m);
        var pendingForApproveAndReject = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = frozenWallet.Id,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Frozen User",
            Status = WithdrawalStatus.Pending
        };
        var approvedForCompleteAndFail = new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = frozenWallet.Id,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Frozen User",
            Status = WithdrawalStatus.Approved
        };
        context.WithdrawalRequests.AddRange(pendingForApproveAndReject, approvedForCompleteAndFail);
        await context.SaveChangesAsync();

        frozenWallet.Freeze();
        await context.SaveChangesAsync();

        var wdProc = new ProcessWithdrawalCommandHandler(context);
        var adminId = Guid.NewGuid().ToString();
        var approveErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            wdProc.Handle(new ProcessWithdrawalCommand("Approve") { WithdrawalId = pendingForApproveAndReject.Id, AdminUserId = adminId }, default));
        Assert.Equal("WALLET_FROZEN", approveErr.ErrorCode);

        var completeErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            wdProc.Handle(new ProcessWithdrawalCommand("Complete", TransferConfirmed: true, TransferReference: "REF-FRZ-1")
            { WithdrawalId = approvedForCompleteAndFail.Id, AdminUserId = adminId }, default));
        Assert.Equal("WALLET_FROZEN", completeErr.ErrorCode);

        var rejected = await wdProc.Handle(
            new ProcessWithdrawalCommand("Reject", AdminNote: "Account frozen") { WithdrawalId = pendingForApproveAndReject.Id, AdminUserId = adminId },
            default);
        Assert.Equal("Rejected", rejected.Status);

        var failed = await wdProc.Handle(
            new ProcessWithdrawalCommand("Fail", AdminNote: "Returned to frozen wallet") { WithdrawalId = approvedForCompleteAndFail.Id, AdminUserId = adminId },
            default);
        Assert.Equal("Failed", failed.Status);
    }

    [Fact]
    public async Task PayOrderWithWallet_ChecksAvailableBalanceNotTotalBalance()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 200_000m,
            LockedBalance = 150_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            new PayOrderWithWalletCommandHandler(context).Handle(
                new PayOrderWithWalletCommand(100_000m) { OrderId = Guid.NewGuid(), UserId = userId.ToString() },
                default));

        Assert.Contains("50000", error.Message);
    }

    [Theory]
    [InlineData(null, "secret", 500)]
    [InlineData("<REPLACE_WITH_INTERNAL_SERVICE_SECRET>", "secret", 500)]
    [InlineData("expected-secret", null, 401)]
    [InlineData("expected-secret", "wrong", 401)]
    public void RequireInternalServiceKey_RejectsUnconfiguredMissingOrWrongKey(
        string? configured,
        string? provided,
        int expectedStatus)
    {
        var context = CreateAuthContext("InternalSecurity:ServiceKey", configured, provided);
        new RequireInternalServiceKeyAttribute().OnAuthorization(context);
        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void RequireInternalServiceKey_AllowsMatchingSecret()
    {
        var context = CreateAuthContext("INTERNAL_SERVICE_SECRET", "s2s-secret-123", "s2s-secret-123");
        new RequireInternalServiceKeyAttribute().OnAuthorization(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void RequireAuctionServiceKey_FallsBackToServiceKey()
    {
        var context = CreateAuthContext("InternalSecurity:ServiceKey", "shared-secret", "shared-secret");
        new RequireAuctionServiceKeyAttribute().OnAuthorization(context);
        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData(nameof(WalletsController.GetUserWalletInternal), "~/api/v1/internal/wallets/{userId:guid}")]
    [InlineData(nameof(WalletsController.FreezePurchaseInternal), "~/api/v1/internal/wallets/freeze-purchase")]
    [InlineData(nameof(WalletsController.UnfreezePurchaseInternal), "~/api/v1/internal/wallets/unfreeze-purchase")]
    [InlineData(nameof(WalletsController.PayOrderInternal), "~/api/v1/internal/wallets/orders/{orderId:guid}/payment")]
    [InlineData(nameof(WalletsController.SettleSellerOrderInternal), "~/api/v1/internal/wallets/sub-orders/{subOrderId:guid}/settle")]
    [InlineData(nameof(WalletsController.RefundOrderInternal), "~/api/v1/internal/wallets/orders/{orderId:guid}/refund")]
    [InlineData(nameof(WalletsController.RefundSubOrderInternal), "~/api/v1/internal/wallets/sub-orders/{subOrderId:guid}/refund")]
    [InlineData(nameof(WalletsController.ExpireDirectPaymentInternal), "~/api/v1/internal/wallets/checkout/direct/{orderId:guid}/expire")]
    [InlineData(nameof(WalletsController.AdjustOrderPaymentInternal), "~/api/v1/internal/wallets/orders/{orderId:guid}/adjust-payment")]
    [InlineData(nameof(WalletsController.CancelOrderEscrowInternal), "~/api/v1/internal/wallets/orders/{orderId:guid}/escrow/cancel")]
    public void InternalRoutes_RequireInternalServiceKeyAndHideFromPublicSwagger(string actionName, string expectedRoute)
    {
        var method = typeof(WalletsController).GetMethod(actionName)!;
        Assert.NotNull(method.GetCustomAttribute<RequireInternalServiceKeyAttribute>());
        var explorer = method.GetCustomAttribute<ApiExplorerSettingsAttribute>();
        Assert.True(explorer?.IgnoreApi);
        Assert.Contains(
            method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>(),
            attr => attr.Template == expectedRoute);
    }

    [Fact]
    public async Task AdminAdjustBalance_RejectsStaffWith403_AllowsAdmin()
    {
        var staffController = CreateAdminController("staff");
        var staffResult = await staffController.AdjustBalance(
            Guid.NewGuid(),
            new AdjustWalletBalanceCommand(10_000m, "Credit", "Test"),
            default);
        Assert.Equal(403, Assert.IsType<ObjectResult>(staffResult).StatusCode);

        var adminController = CreateAdminController("admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adminController.AdjustBalance(
                Guid.NewGuid(),
                new AdjustWalletBalanceCommand(10_000m, "Credit", "Test"),
                default));
    }

    [Fact]
    public void StripeSettings_ValidatesPlaceholdersAndLocalhostHttp()
    {
        var unconfigured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "<SET_VIA_ENVIRONMENT>",
            ["Stripe:WebhookSecret"] = "<SET_VIA_ENVIRONMENT>"
        }).Build();
        Assert.False(StripeSettings.Validate(unconfigured, NullLogger.Instance));

        var partialInvalid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_123",
            ["Stripe:WebhookSecret"] = ""
        }).Build();
        Assert.Throws<InvalidOperationException>(() => StripeSettings.Validate(partialInvalid));

        var devValid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Stripe:SecretKey"] = "sk_test_123",
            ["Stripe:WebhookSecret"] = "whsec_123",
            ["Stripe:SuccessUrl"] = "http://localhost:3000/success",
            ["Stripe:CancelUrl"] = "http://localhost:3000/cancel"
        }).Build();
        Assert.True(StripeSettings.Validate(devValid));
    }

    private static AuthorizationFilterContext CreateAuthContext(string configKey, string? configured, string? provided)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [configKey] = configured
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (provided is not null)
        {
            http.Request.Headers["X-Internal-Service-Key"] = provided;
        }

        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }

    private static AdminWalletsController CreateAdminController(string role)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, role)
                },
                "test"))
        };
        return new AdminWalletsController(
            DispatchProxy.Create<ISender, WalletContractRegressionTests.ThrowingSenderProxy>(),
            NullLogger<AdminWalletsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static WalletAccount AddWallet(TestWalletDbContext context, Guid userId, decimal balance, string status = "ACTIVE")
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = balance,
            LockedBalance = 0m,
            Currency = CurrencyCode.Vnd,
            Status = status
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }
}
