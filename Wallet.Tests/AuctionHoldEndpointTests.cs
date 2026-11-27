namespace Wallet.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Wallet.Api.Controllers;
using Wallet.Api.Filters;
using Xunit;

public sealed class AuctionHoldEndpointTests
{
    [Theory]
    [InlineData(nameof(WalletsController.CreateHold), "holds")]
    [InlineData(nameof(WalletsController.ReleaseHold), "holds/{holdId:guid}/release")]
    [InlineData(nameof(WalletsController.CaptureHold), "holds/{holdId:guid}/capture")]
    [InlineData(nameof(WalletsController.RefundCapturedHold), "holds/{holdId:guid}/refund-captured")]
    [InlineData(nameof(WalletsController.GetHold), "holds/{holdId:guid}")]
    public void ServiceRoutes_RequireScopedServiceKey(string name, string route)
    {
        var method = typeof(WalletsController).GetMethod(name)!;
        Assert.NotNull(method.GetCustomAttribute<RequireAuctionServiceKeyAttribute>());
        Assert.Contains(method.GetCustomAttributes<HttpMethodAttribute>(), attribute => attribute.Template == route);
    }

    [Fact]
    public void GetHoldByReference_AcceptsInternalOrAuctionServiceKey()
    {
        var method = typeof(WalletsController).GetMethod(nameof(WalletsController.GetHoldByReference))!;
        Assert.NotNull(method.GetCustomAttribute<RequireInternalOrAuctionServiceKeyAttribute>());
        Assert.Contains(method.GetCustomAttributes<HttpMethodAttribute>(), attribute => attribute.Template == "holds/by-reference/{referenceType}/{referenceId}");
    }

    [Fact]
    public void BuyerList_RequiresUserIdentity()
    {
        var method = typeof(WalletsController).GetMethod(nameof(WalletsController.ListMyHolds))!;
        Assert.NotNull(method.GetCustomAttribute<RequireRoleAttribute>());
        Assert.Null(method.GetCustomAttribute<RequireAuctionServiceKeyAttribute>());
    }
}
