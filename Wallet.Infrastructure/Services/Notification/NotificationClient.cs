namespace Wallet.Infrastructure.Services.Notification;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wallet.Application.Interfaces;

public sealed class NotificationClient : INotificationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<NotificationClient> _logger;
    private readonly string? _serviceKey;

    public NotificationClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<NotificationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var configuredKey = Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_KEY")
            ?? configuration["NOTIFICATION_SERVICE_KEY"]
            ?? configuration["Services:Notification:ServiceKey"];

        _serviceKey = !string.IsNullOrWhiteSpace(configuredKey) &&
                      !configuredKey.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) &&
                      !configuredKey.Contains("SET_VIA_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
            ? configuredKey.Trim()
            : null;
    }

    public Task<NotificationDispatchResult> SendWithdrawalEventAsync(
        WithdrawalNotificationEvent payload,
        CancellationToken cancellationToken = default) =>
        SendAsync("api/v1/notifications/withdrawal-events", payload, cancellationToken);

    public Task<NotificationDispatchResult> SendAuctionDepositEventAsync(
        AuctionDepositNotificationEvent payload,
        CancellationToken cancellationToken = default) =>
        SendAsync("api/v1/notifications/auction-deposits", payload, cancellationToken);

    private async Task<NotificationDispatchResult> SendAsync<TPayload>(
        string route,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };

            if (!string.IsNullOrEmpty(_serviceKey))
            {
                request.Headers.TryAddWithoutValidation("X-Internal-Service-Key", _serviceKey);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var notificationId = TryExtractNotificationId(body);
                return new NotificationDispatchResult(true, (int)response.StatusCode, notificationId);
            }

            var truncated = body.Length > 500 ? body[..500] : body;
            _logger.LogWarning(
                "[REST Notification] Dispatch to '{Route}' failed with HTTP {StatusCode}: {Body}",
                route,
                (int)response.StatusCode,
                truncated);

            return new NotificationDispatchResult(false, (int)response.StatusCode, Error: truncated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Notification] Connection error dispatching to '{Route}'.", route);
            return new NotificationDispatchResult(false, Error: ex.Message);
        }
    }

    private static string? TryExtractNotificationId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryGetId(doc.RootElement, out var id))
            {
                return id;
            }

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                TryGetId(data, out var dataId))
            {
                return dataId;
            }
        }
        catch (JsonException)
        {
            // Non-JSON success responses are valid.
        }

        return null;
    }

    private static bool TryGetId(JsonElement obj, out string? id)
    {
        foreach (var propertyName in new[] { "notificationId", "id" })
        {
            if (obj.TryGetProperty(propertyName, out var element))
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    id = element.GetString();
                    return !string.IsNullOrWhiteSpace(id);
                }

                if (element.ValueKind == JsonValueKind.Number)
                {
                    id = element.GetRawText();
                    return true;
                }
            }
        }

        id = null;
        return false;
    }
}
