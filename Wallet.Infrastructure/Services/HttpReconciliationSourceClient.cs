namespace Wallet.Infrastructure.Services;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Wallet.Application.Common.Ledger;

/// <summary>Reads order and deposit data from Commerce and Auction over their internal reconciliation routes. Read only.</summary>
public sealed class HttpReconciliationSourceClient : IReconciliationSourceClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _factory;
    private readonly string _commerceUrl;
    private readonly string _auctionUrl;
    private readonly string _key;

    public HttpReconciliationSourceClient(IHttpClientFactory factory, IConfiguration configuration)
    {
        _factory = factory;
        _commerceUrl = (configuration["Reconciliation:CommerceBaseUrl"] ?? configuration["COMMERCE_SERVICE_URL"] ?? "http://commerce-service:8003").TrimEnd('/');
        _auctionUrl = (configuration["Reconciliation:AuctionBaseUrl"] ?? configuration["AUCTION_SERVICE_URL"] ?? "http://auction-service:8005").TrimEnd('/');
        _key = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? configuration["INTERNAL_SERVICE_SECRET"]
            ?? configuration["InternalSecurity:ServiceKey"]
            ?? string.Empty;
    }

    private sealed record OrderPage(List<OrderRecon> Items, Guid? NextCursor);

    private sealed record DepositPage(List<DepositRecon> Items, Guid? NextCursor);

    public async Task<IReadOnlyList<OrderRecon>> GetOrdersAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken)
    {
        var all = new List<OrderRecon>();
        Guid? cursor = null;
        do
        {
            var url = $"{_commerceUrl}/api/v1/internal/reconciliation/orders?limit=200"
                + (updatedFrom.HasValue ? $"&updatedFrom={Uri.EscapeDataString(updatedFrom.Value.ToString("O"))}" : string.Empty)
                + (cursor.HasValue ? $"&cursor={cursor}" : string.Empty);
            var page = await GetAsync<OrderPage>(url, cancellationToken);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor.HasValue);
        return all;
    }

    public async Task<IReadOnlyList<DepositRecon>> GetDepositsAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken)
    {
        var all = new List<DepositRecon>();
        Guid? cursor = null;
        do
        {
            var url = $"{_auctionUrl}/api/v1/internal/reconciliation/deposits?limit=200"
                + (updatedFrom.HasValue ? $"&updatedFrom={Uri.EscapeDataString(updatedFrom.Value.ToString("O"))}" : string.Empty)
                + (cursor.HasValue ? $"&cursor={cursor}" : string.Empty);
            var page = await GetAsync<DepositPage>(url, cancellationToken);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor.HasValue);
        return all;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Internal-Service-Key", _key);
        var client = _factory.CreateClient("reconciliation-source");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{new Uri(url).Host} answered {(int)response.StatusCode}.");
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
            ?? throw new HttpRequestException($"{new Uri(url).Host} returned an empty body.");
    }
}
