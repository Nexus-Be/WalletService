namespace Wallet.Api.Filters;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wallet.Api.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireAuctionServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var logger = http.RequestServices.GetRequiredService<ILogger<RequireAuctionServiceKeyAttribute>>();
        var expected = configuration["InternalSecurity:AuctionServiceKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError("Auction service authentication is not configured.");
            context.Result = Problem(http, StatusCodes.Status500InternalServerError,
                "INTERNAL_CONFIGURATION_ERROR", "Internal service authentication is not configured.");
            return;
        }

        if (!http.Request.Headers.TryGetValue("X-Internal-Service-Key", out var provided) ||
            provided.Count != 1 || string.IsNullOrWhiteSpace(provided[0]) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(provided[0]!))))
        {
            logger.LogWarning("Auction service authentication failed for {Path}.", http.Request.Path);
            context.Result = Problem(http, StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED", "Internal service authentication is required.");
        }
    }

    private static ObjectResult Problem(HttpContext http, int status, string code, string detail) =>
        new(ApiValidationProblemFactory.CreateProblemDetails(
            status, code, status == StatusCodes.Status401Unauthorized ? "Unauthorized" : "Internal server error",
            detail, http.TraceIdentifier))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
