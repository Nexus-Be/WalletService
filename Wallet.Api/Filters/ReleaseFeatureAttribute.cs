namespace Wallet.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wallet.Api.Common;
using Wallet.Application.Features.Holds.Commands.CaptureHold;

public enum ReleaseFeature
{
    Withdrawals,
    AuctionOrderCapture,
    LegacyCommerceCredits,
    /// <summary>Legacy (V1) seller payout route only. Falls back to LegacyCommerceCredits when not configured, so it can be closed without closing the refund routes.</summary>
    LegacyCommerceSettlement,
    /// <summary>Atomic seller payout (protocol V2) and the platform subsidy hold. Off by default outside Development.</summary>
    OrderSettlementV2,
    /// <summary>
    /// The four refund routes (order and sub-order, each on the service route and the internal route). Unlike every other feature it is
    /// off by default in every environment, Development included, and never falls back to another setting: it is opened only by an
    /// explicit <c>ReleaseFeatures:OrderRefundEnabled=true</c>, after the refund path has been checked.
    /// </summary>
    OrderRefund
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ReleaseFeatureAttribute(ReleaseFeature feature) : Attribute, IActionFilter
{
    public ReleaseFeature Feature => feature;

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
            ReleaseFeature.OrderSettlementV2 => "ReleaseFeatures:OrderSettlementV2Enabled",
            ReleaseFeature.OrderRefund => "ReleaseFeatures:OrderRefundEnabled",
            ReleaseFeature.LegacyCommerceSettlement =>
                configuration["ReleaseFeatures:LegacyCommerceSettlementEnabled"] is not null
                    ? "ReleaseFeatures:LegacyCommerceSettlementEnabled"
                    : "ReleaseFeatures:LegacyCommerceCreditsEnabled",
            _ => throw new InvalidOperationException("The release feature is invalid.")
        };
        var configured = configuration[setting];
        // Every feature is open in Development unless configured; OrderRefund is the exception and is closed everywhere until it is set to true.
        var enabled = feature != ReleaseFeature.OrderRefund && environment.IsDevelopment();
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
