namespace Wallet.Api.Common;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

public static class ApiValidationProblemFactory
{
    public const string GenericDetail = "One or more validation errors occurred.";
    public const string UnreadableBodyDetail = "The request body is not valid.";

    public static IActionResult Create(ActionContext context)
    {
        var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        var errors = CollectErrors(context.ModelState);
        var messages = errors.Values.SelectMany(v => v).ToList();
        var detail = messages.Any(m => m == UnreadableBodyDetail)
            ? UnreadableBodyDetail
            : (messages.Count > 0 ? string.Join(" ", messages) : GenericDetail);

        var problem = CreateProblemDetails(
            StatusCodes.Status400BadRequest,
            "VAL_INVALID_INPUT",
            "One or more validation errors occurred.",
            detail,
            traceId,
            errors.Count > 0 ? errors : null);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static ApiProblemDetails CreateProblemDetails(
        int status,
        string errorCode,
        string title,
        string detail,
        string? traceId,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(
            GetRfcType(status),
            title,
            status,
            errorCode,
            detail,
            errors,
            traceId,
            DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

    private static string GetRfcType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.3",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status423Locked => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status429TooManyRequests => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
    };

    private static Dictionary<string, string[]> CollectErrors(ModelStateDictionary modelState)
    {
        var dict = new Dictionary<string, string[]>();

        foreach (var entry in modelState)
        {
            if (entry.Value is null || entry.Value.Errors.Count == 0)
            {
                continue;
            }

            var errs = entry.Value.Errors
                .Select(error =>
                {
                    var msg = string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? $"{entry.Key} is invalid."
                        : error.ErrorMessage.Trim();
                    return LeaksInternals(msg) ? UnreadableBodyDetail : msg;
                })
                .Distinct()
                .ToArray();

            if (errs.Length > 0)
            {
                dict[entry.Key] = errs;
            }
        }

        return dict;
    }

    private static bool LeaksInternals(string message) =>
        message.Contains("LineNumber", StringComparison.Ordinal) ||
        message.Contains("BytePositionInLine", StringComparison.Ordinal) ||
        message.Contains("Path: $", StringComparison.Ordinal) ||
        message.Contains("could not be converted to", StringComparison.OrdinalIgnoreCase);
}

public sealed record ApiProblemDetails(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("errorCode")]
    string ErrorCode,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("errors")] IReadOnlyDictionary<string, string[]>? Errors,
    [property: JsonPropertyName("traceId")]
    string? TraceId,
    [property: JsonPropertyName("timestamp")]
    string Timestamp);