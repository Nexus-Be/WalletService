namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Wallet.Api.Filters;
using Wallet.Api.Swagger;
using Wallet.Application.Interfaces;
using Wallet.Infrastructure.Services.Notification;
using Xunit;

public sealed class ServiceKeyHygieneTests
{
    private static OpenApiOperation ApplyFilter(string relativePath)
    {
        var operation = new OpenApiOperation();
        var context = new OperationFilterContext(
            new ApiDescription { RelativePath = relativePath },
            null!,
            null!,
            typeof(ServiceKeyHygieneTests).GetMethod(nameof(ApplyFilter), BindingFlags.NonPublic | BindingFlags.Static)!);
        new InternalOperationFilter().Apply(operation, context);
        return operation;
    }

    [Fact]
    public void InternalOperationFilter_AddsHeaderWithoutEmbeddingSecret_AndSkipsPublicRoutes()
    {
        var internalOperation = ApplyFilter("api/v1/internal/wallets/orders/1/payment");
        var parameter = Assert.Single(internalOperation.Parameters);
        Assert.Equal("X-Internal-Service-Key", parameter.Name);
        Assert.True(parameter.Required);
        Assert.Null(parameter.Schema.Default);

        var publicOperation = ApplyFilter("api/v1/wallets/me");
        Assert.True(publicOperation.Parameters is null || publicOperation.Parameters.Count == 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<OPTIONAL_INTERNAL_SERVICE_KEY>")]
    [InlineData("<REPLACE_WITH_NOTIFICATION_KEY>")]
    public async Task NotificationClient_OmitsHeader_WhenKeyUnconfiguredOrPlaceholder(string? configured)
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8007/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Notification:ServiceKey"] = configured
        }).Build();
        var client = new NotificationClient(http, config, NullLogger<NotificationClient>.Instance);

        await client.SendWithdrawalEventAsync(new WithdrawalNotificationEvent(
            "withdrawal:1:Approved", Guid.NewGuid(), Guid.NewGuid(), "Approved",
            DateTimeOffset.UtcNow, null, null, null));

        Assert.False(handler.HeaderSent);
    }

    [Fact]
    public async Task NotificationClient_SendsHeader_WhenRealKeyConfigured()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8007/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Notification:ServiceKey"] = "real-key"
        }).Build();
        var client = new NotificationClient(http, config, NullLogger<NotificationClient>.Instance);

        await client.SendWithdrawalEventAsync(new WithdrawalNotificationEvent(
            "withdrawal:1:Approved", Guid.NewGuid(), Guid.NewGuid(), "Approved",
            DateTimeOffset.UtcNow, null, null, null));

        Assert.True(handler.HeaderSent);
    }

    [Theory]
    [InlineData("<OPTIONAL_INTERNAL_SERVICE_KEY>")]
    [InlineData("<ANY_PLACEHOLDER>")]
    public void RequireInternalServiceKey_TreatsPlaceholderAsUnconfigured(string placeholder)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InternalSecurity:ServiceKey"] = placeholder
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers["X-Internal-Service-Key"] = placeholder;
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        new RequireInternalServiceKeyAttribute().OnAuthorization(context);

        Assert.Equal(500, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public bool HeaderSent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HeaderSent = request.Headers.Contains("X-Internal-Service-Key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
