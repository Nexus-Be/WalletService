namespace Wallet.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wallet.Api.Common;
using Wallet.Application.Features.Holds.Commands.CaptureHold;

public enum ReleaseFeature
{
    Withdrawals,
    AuctionOrderCapture,
    LegacyCommerceCredits
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ReleaseFeatureAttribute(ReleaseFeature feature) : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (feature == ReleaseFeature.AuctionOrderCapture &&
            !context.ActionArguments.Values.OfType<CaptureHoldCommand>()
                .Any(command => command.Purpose == "ORDER_PAYMENT"))
            return;

        var http = context.HttpContext;
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var environment = http.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var logger = http.RequestServices.GetRequiredService<ILogger<ReleaseFeatureAttribute>>();
        var setting = feature switch
        {
            ReleaseFeature.Withdrawals => "ReleaseFeatures:WithdrawalsEnabled",
            ReleaseFeature.AuctionOrderCapture => "ReleaseFeatures:AuctionOrderCaptureEnabled",
            ReleaseFeature.LegacyCommerceCredits => "ReleaseFeatures:LegacyCommerceCreditsEnabled",
            _ => throw new InvalidOperationException("The release feature is invalid.")
        };
        var configured = configuration[setting];
        var enabled = environment.IsDevelopment();
        if (configured is not null && !bool.TryParse(configured, out enabled))
        {
            logger.LogError("Release feature setting {Setting} is invalid.", setting);
            context.Result = Problem(http, StatusCodes.Status500InternalServerError,
                "INTERNAL_CONFIGURATION_ERROR", "Release feature configuration is invalid.");
            return;
        }

        if (!enabled)
        {
            logger.LogInformation("Release feature {Feature} is disabled.", feature);
            context.Result = Problem(http, StatusCodes.Status503ServiceUnavailable,
                "FEATURE_DISABLED", "This operation is not available.");
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static ObjectResult Problem(HttpContext http, int status, string code, string detail)
    {
        var problem = ApiValidationProblemFactory.CreateProblemDetails(
            status, code, status == StatusCodes.Status503ServiceUnavailable ? "Service unavailable" : "Internal server error",
            detail, http.TraceIdentifier);
        if (status == StatusCodes.Status503ServiceUnavailable)
            problem = problem with { Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4" };
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
