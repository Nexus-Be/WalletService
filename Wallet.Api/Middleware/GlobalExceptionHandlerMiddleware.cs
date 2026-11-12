namespace Wallet.Api.Middleware;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Wallet.Api.Common;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Exceptions;

public sealed class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var (statusCode, errorCode, title, detail) = exception switch
        {
            StripeWebhookSignatureException => (
                StatusCodes.Status400BadRequest,
                "VAL_INVALID_SIGNATURE",
                "Invalid webhook signature",
                "Stripe webhook signature is invalid."),
            PaymentProviderUnavailableException providerException => (
                StatusCodes.Status503ServiceUnavailable,
                "PAYMENT_PROVIDER_UNAVAILABLE",
                "Payment provider unavailable",
                providerException.Message),
            RequestValidationException rve => (
                rve.StatusCode,
                rve.ErrorCode,
                GetStatusTitle(rve.StatusCode, ApiValidationProblemFactory.GenericDetail),
                rve.Message),
            WalletNotFoundException wnfe => (
                wnfe.StatusCode,
                wnfe.ErrorCode,
                GetStatusTitle(wnfe.StatusCode, "Wallet not found"),
                wnfe.Message),
            InsufficientBalanceException ibe => (
                ibe.StatusCode,
                ibe.ErrorCode,
                GetStatusTitle(ibe.StatusCode, "Insufficient balance"),
                ibe.Message),
            BusinessRuleValidationException bve => (
                bve.StatusCode,
                bve.ErrorCode,
                GetStatusTitle(bve.StatusCode, "Business rule violation"),
                bve.Message),
            EntityNotFoundException nfe => (
                nfe.StatusCode,
                nfe.ErrorCode,
                GetStatusTitle(nfe.StatusCode, "Resource not found"),
                nfe.Message),
            DomainException dex => (
                dex.StatusCode,
                dex.ErrorCode,
                GetStatusTitle(dex.StatusCode, "Domain error"),
                dex.Message),
            ArgumentException aex => (
                StatusCodes.Status400BadRequest,
                "VAL_INVALID_ARGUMENT",
                GetStatusTitle(StatusCodes.Status400BadRequest, "Invalid argument"),
                aex.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "INTERNAL_SERVER_ERROR",
                GetStatusTitle(StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
                "An internal server error occurred. Please try again later.")
        };

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "[ERROR {ErrorCode}] {Title}: {Detail}. Trace: {TraceId}", errorCode, title,
                detail, traceId);
        }
        else
        {
            _logger.LogWarning("[WARN {ErrorCode}] {Title}: {Detail}. Trace: {TraceId}", errorCode, title, detail,
                traceId);
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var errors = exception is RequestValidationException requestValidationException
            ? requestValidationException.Errors
            : null;
        var problemDetails = ApiValidationProblemFactory.CreateProblemDetails(
            statusCode,
            errorCode,
            title,
            detail,
            traceId,
            errors);

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }

    private static string GetStatusTitle(int statusCode, string fallback) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status423Locked => "Account locked",
        StatusCodes.Status429TooManyRequests => "Too many requests",
        _ => fallback
    };
}

public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app) =>
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
}