namespace Wallet.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Api.Filters;
using Xunit;

public sealed class AuctionServiceKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("incorrect")]
    public void MissingOrWrongKey_IsUnauthorized(string? provided)
    {
        var context = CreateContext("expected-test-key", provided);
        new RequireAuctionServiceKeyAttribute().OnAuthorization(context);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void CorrectKey_IsAllowed()
    {
        var context = CreateContext("expected-test-key", "expected-test-key");
        new RequireAuctionServiceKeyAttribute().OnAuthorization(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void MissingConfiguration_FailsClosed()
    {
        var context = CreateContext(null, "some-key");
        new RequireAuctionServiceKeyAttribute().OnAuthorization(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void MultipleHeaderValues_AreRejected()
    {
        var context = CreateContext("expected-test-key", null);
        context.HttpContext.Request.Headers.Append("X-Internal-Service-Key", "expected-test-key");
        context.HttpContext.Request.Headers.Append("X-Internal-Service-Key", "other");
        new RequireAuctionServiceKeyAttribute().OnAuthorization(context);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    private static AuthorizationFilterContext CreateContext(string? configured, string? provided)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InternalSecurity:AuctionServiceKey"] = configured
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddLogging().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (provided is not null)
            http.Request.Headers["X-Internal-Service-Key"] = provided;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(action, new List<IFilterMetadata>());
    }
}
