namespace Wallet.Api.Filters;

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wallet.Api.Common;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireInternalServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var logger = http.RequestServices.GetRequiredService<ILogger<RequireInternalServiceKeyAttribute>>();
        var expected = ServiceKeyGuard.FirstConfigured(
            configuration["INTERNAL_SERVICE_SECRET"],
            configuration["InternalSecurity:ServiceKey"]);

        ServiceKeyGuard.Enforce(context, expected, logger, "Internal service");
    }
}

internal static class ServiceKeyGuard
{
    internal static string? FirstConfigured(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !candidate.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) &&
                !candidate.Contains("SET_VIA_ENVIRONMENT", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    internal static void Enforce(
        AuthorizationFilterContext context,
        string? expected,
        ILogger logger,
        string scopeLabel)
    {
        var http = context.HttpContext;
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError("{Scope} authentication is not configured.", scopeLabel);
            context.Result = Problem(
                http,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_CONFIGURATION_ERROR",
                "Internal service authentication is not configured.");
            return;
        }

        if (!http.Request.Headers.TryGetValue("X-Internal-Service-Key", out var provided) ||
            provided.Count != 1 ||
            string.IsNullOrWhiteSpace(provided[0]) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(provided[0]!))))
        {
            logger.LogWarning("{Scope} authentication failed for {Path}.", scopeLabel, http.Request.Path);
            context.Result = Problem(
                http,
                StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED",
                "Internal service authentication is required.");
        }
    }

    private static ObjectResult Problem(HttpContext http, int status, string code, string detail) =>
        new(ApiValidationProblemFactory.CreateProblemDetails(
            status,
            code,
            status == StatusCodes.Status401Unauthorized ? "Unauthorized" : "Internal server error",
            detail,
            http.TraceIdentifier))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
