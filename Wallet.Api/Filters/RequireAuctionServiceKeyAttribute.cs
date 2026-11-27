namespace Wallet.Api.Filters;

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireAuctionServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var logger = http.RequestServices.GetRequiredService<ILogger<RequireAuctionServiceKeyAttribute>>();
        var expected = ServiceKeyGuard.FirstConfigured(
            configuration["InternalSecurity:AuctionServiceKey"],
            configuration["InternalSecurity:ServiceKey"],
            configuration["INTERNAL_SERVICE_SECRET"]);

        ServiceKeyGuard.Enforce(context, expected, logger, "Auction service");
    }
}

/// <summary>
/// Accepts either the scoped auction service key or the shared internal service key for cross-service read lookups
/// such as Commerce checking an auction deposit hold status.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireInternalOrAuctionServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var logger = http.RequestServices.GetRequiredService<ILogger<RequireInternalOrAuctionServiceKeyAttribute>>();

        var auctionKey = ServiceKeyGuard.FirstConfigured(configuration["InternalSecurity:AuctionServiceKey"]);
        var internalKey = ServiceKeyGuard.FirstConfigured(
            configuration["InternalSecurity:ServiceKey"],
            configuration["INTERNAL_SERVICE_SECRET"]);

        if (string.IsNullOrWhiteSpace(auctionKey) && string.IsNullOrWhiteSpace(internalKey))
        {
            ServiceKeyGuard.Enforce(context, null, logger, "Internal or auction service");
            return;
        }

        if (!string.IsNullOrWhiteSpace(auctionKey) && http.Request.Headers.TryGetValue(ServiceKeyGuard.HeaderName, out var provided) &&
            provided.Count == 1 && ServiceKeyGuard.SecureEquals(provided.ToString(), auctionKey))
        {
            return;
        }

        ServiceKeyGuard.Enforce(context, internalKey ?? auctionKey, logger, "Internal or auction service");
    }
}
