namespace Wallet.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Wallet.Api.Common;
using Wallet.Api.Controllers;
using Wallet.Api.Filters;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Xunit;

public sealed class ReleaseFeatureTests
{
    [Theory]
    [InlineData(ReleaseFeature.Withdrawals, "Production", null, 503)]
    [InlineData(ReleaseFeature.Withdrawals, "Staging", null, 503)]
    [InlineData(ReleaseFeature.Withdrawals, "Development", null, 0)]
    [InlineData(ReleaseFeature.Withdrawals, "Production", "true", 0)]
    [InlineData(ReleaseFeature.Withdrawals, "Development", "false", 503)]
    [InlineData(ReleaseFeature.Withdrawals, "Production", "invalid", 500)]
    [InlineData(ReleaseFeature.AuctionOrderCapture, "Production", null, 503)]
    [InlineData(ReleaseFeature.AuctionOrderCapture, "Development", null, 0)]
    [InlineData(ReleaseFeature.AuctionOrderCapture, "Production", "true", 0)]
    [InlineData(ReleaseFeature.AuctionOrderCapture, "Development", "false", 503)]
    [InlineData(ReleaseFeature.AuctionOrderCapture, "Production", "invalid", 500)]
    [InlineData(ReleaseFeature.LegacyCommerceCredits, "Production", null, 503)]
    [InlineData(ReleaseFeature.LegacyCommerceCredits, "Development", null, 0)]
    [InlineData(ReleaseFeature.LegacyCommerceCredits, "Production", "true", 0)]
    [InlineData(ReleaseFeature.LegacyCommerceCredits, "Development", "false", 503)]
    [InlineData(ReleaseFeature.LegacyCommerceCredits, "Production", "invalid", 500)]
    [InlineData(ReleaseFeature.OrderSettlementV2, "Production", null, 503)]
    [InlineData(ReleaseFeature.OrderSettlementV2, "Development", null, 0)]
    [InlineData(ReleaseFeature.OrderSettlementV2, "Production", "true", 0)]
    [InlineData(ReleaseFeature.OrderSettlementV2, "Development", "false", 503)]
    [InlineData(ReleaseFeature.OrderSettlementV2, "Production", "invalid", 500)]
    [InlineData(ReleaseFeature.LegacyCommerceSettlement, "Production", null, 503)]
    [InlineData(ReleaseFeature.LegacyCommerceSettlement, "Development", null, 0)]
    [InlineData(ReleaseFeature.LegacyCommerceSettlement, "Production", "true", 0)]
    [InlineData(ReleaseFeature.LegacyCommerceSettlement, "Development", "false", 503)]
    public void Gate_UsesExplicitConfigurationOrEnvironmentDefault(
        ReleaseFeature feature, string environment, string? configured, int expected)
    {
        var context = CreateContext(feature, environment, configured, "ORDER_PAYMENT");
        new ReleaseFeatureAttribute(feature).OnActionExecuting(context);

        if (expected == 0)
        {
            Assert.Null(context.Result);
            return;
        }

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(expected, result.StatusCode);
        var problem = Assert.IsType<ApiProblemDetails>(result.Value);
        Assert.Equal(expected == 503 ? "FEATURE_DISABLED" : "INTERNAL_CONFIGURATION_ERROR", problem.ErrorCode);
        if (expected == 503)
            Assert.EndsWith("#section-15.6.4", problem.Type);
    }

    [Theory]
    [InlineData("true", null, 0)]        // only the shared flag set: settlement follows it (no behaviour change)
    [InlineData("false", null, 503)]
    [InlineData("true", "false", 503)]   // settlement closed on its own while refunds stay open
    [InlineData("false", "true", 0)]     // explicit settlement flag wins
    public void LegacySettlement_CanBeClosedWithoutClosingTheSharedLegacyFlag(string credits, string? settlement, int expected)
    {
        var values = new Dictionary<string, string?> { ["ReleaseFeatures:LegacyCommerceCreditsEnabled"] = credits };
        if (settlement is not null) values["ReleaseFeatures:LegacyCommerceSettlementEnabled"] = settlement;
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .AddSingleton<IWebHostEnvironment>(new TestEnvironment { EnvironmentName = "Production" })
            .AddLogging()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [],
            new Dictionary<string, object?>(), new object());

        new ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceSettlement).OnActionExecuting(context);

        if (expected == 0) Assert.Null(context.Result);
        else Assert.Equal(expected, Assert.IsType<ObjectResult>(context.Result).StatusCode);

        // The refund routes keep following the shared flag only.
        var refund = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [],
            new Dictionary<string, object?>(), new object());
        new ReleaseFeatureAttribute(ReleaseFeature.LegacyCommerceCredits).OnActionExecuting(refund);
        if (credits == "true") Assert.Null(refund.Result); else Assert.NotNull(refund.Result);
    }

    [Fact]
    public void Forfeiture_IsNotBlockedByOrderCaptureGate()
    {
        var context = CreateContext(ReleaseFeature.AuctionOrderCapture, "Production", null, "FORFEIT");
        new ReleaseFeatureAttribute(ReleaseFeature.AuctionOrderCapture).OnActionExecuting(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void UnrelatedRecoveryActions_AreNotGated()
    {
        Assert.NotNull(typeof(WalletsController).GetMethod(nameof(WalletsController.Withdraw))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.NotNull(typeof(WalletsController).GetMethod(nameof(WalletsController.CaptureHold))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.NotNull(typeof(WalletsController).GetMethod(nameof(WalletsController.SettleSellerOrder))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        // Both V1 payout routes must follow the dedicated settlement flag, not the flag shared with the refund routes.
        foreach (var name in new[] { nameof(WalletsController.SettleSellerOrder), nameof(WalletsController.SettleSellerOrderInternal) })
        {
            var attribute = typeof(WalletsController).GetMethod(name)!.GetCustomAttributes<ReleaseFeatureAttribute>().Single();
            Assert.Equal(ReleaseFeature.LegacyCommerceSettlement, attribute.Feature);
        }
        // The V2 payout and the subsidy hold are gated; releasing a subsidy is a recovery action and is not.
        foreach (var name in new[] { nameof(WalletsController.SettleOrderPayout), nameof(WalletsController.HoldPlatformSubsidy) })
        {
            Assert.Equal(ReleaseFeature.OrderSettlementV2,
                typeof(WalletsController).GetMethod(name)!.GetCustomAttributes<ReleaseFeatureAttribute>().Single().Feature);
        }
        Assert.Null(typeof(WalletsController).GetMethod(nameof(WalletsController.ReleasePlatformSubsidy))!.GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.NotNull(typeof(WalletsController).GetMethod(nameof(WalletsController.RefundOrder))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.NotNull(typeof(WalletsController).GetMethod(nameof(WalletsController.RefundSubOrder))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.Null(typeof(WalletsController).GetMethod(nameof(WalletsController.ReleaseHold))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.Null(typeof(WalletsController).GetMethod(nameof(WalletsController.RefundCapturedHold))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
        Assert.Null(typeof(AdminWalletsController).GetMethod(nameof(AdminWalletsController.ProcessWithdrawal))!
            .GetCustomAttribute<ReleaseFeatureAttribute>());
    }

    private static ActionExecutingContext CreateContext(
        ReleaseFeature feature, string environment, string? configured, string purpose)
    {
        var key = feature switch
        {
            ReleaseFeature.Withdrawals => "ReleaseFeatures:WithdrawalsEnabled",
            ReleaseFeature.AuctionOrderCapture => "ReleaseFeatures:AuctionOrderCaptureEnabled",
            ReleaseFeature.LegacyCommerceCredits => "ReleaseFeatures:LegacyCommerceCreditsEnabled",
            ReleaseFeature.OrderSettlementV2 => "ReleaseFeatures:OrderSettlementV2Enabled",
            ReleaseFeature.LegacyCommerceSettlement => "ReleaseFeatures:LegacyCommerceSettlementEnabled",
            _ => throw new ArgumentOutOfRangeException(nameof(feature))
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [key] = configured }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IWebHostEnvironment>(new TestEnvironment { EnvironmentName = environment })
            .AddLogging()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var arguments = new Dictionary<string, object?>
        {
            ["command"] = new CaptureHoldCommand(purpose, null, [])
        };
        return new ActionExecutingContext(action, [], arguments, new object());
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
