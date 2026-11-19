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
