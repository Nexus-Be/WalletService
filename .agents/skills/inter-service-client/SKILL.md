---
name: inter-service-client
description: Add or modify outbound typed REST clients in WalletService (port 8008). Use when WalletService dispatches balance alerts or transaction receipts to NotificationService without using message brokers.
---

# Outbound REST Client in WalletService

> Scope: WalletService repository only. Never reference parent workspace paths, root documentation, or sibling service directories.
> Architectural Note: WalletService uses classic CQRS via MediatR (frozen architecture). Outbound clients are injected directly into MediatR Command Handlers, not into feature service facades.

## Local Facts

- Service: WalletService
- Local HTTP Port: 8008
- Primary Downstream Target: NotificationService (Port 8007)
- Target Use Case: Dispatching email notifications for deposits, withdrawals, and balance alerts
- Interface Location: `Wallet.Application/Interfaces/`
- Implementation Location: `Wallet.Infrastructure/Services/{Target}/`
- DI Registration: `Wallet.Infrastructure/DependencyInjection.cs`
- Architecture Constraint: Inject `I{Target}Client` directly into `{UseCase}CommandHandler.cs`. Never recreate `I{Feature}Service` facades.

## Invariants

1. **100% RESTful HTTP/JSON:** No RabbitMQ, MassTransit, Saga, or Outbox. All cross-service calls use typed `HttpClient`.
2. **Strict Project Isolation:** Never reference external assemblies or query other microservice databases directly.
3. **No Internal API Key:** Inter-service routes use standard public endpoints. Do not send `X-Internal-Service-Key`.
4. **Timeout & Non-Blocking Resilience:** Outbound notifications must never block financial ledger operations. Ledger updates and DB transactions must commit; failure to send an email must be caught and logged as a warning, not rolled back.
5. **Timeout:** Enforce `client.Timeout = TimeSpan.FromSeconds(10)`.

## Implementation Steps

### 1. Application Contract
Define `Wallet.Application/Interfaces/IEmailNotificationClient.cs`:
```csharp
namespace Wallet.Application.Interfaces;

public interface IEmailNotificationClient
{
    Task<bool> SendTransactionReceiptEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
```

### 2. Infrastructure Typed Client
Implement `Wallet.Infrastructure/Services/Notification/EmailNotificationClient.cs`:
```csharp
namespace Wallet.Infrastructure.Services.Notification;

public sealed class EmailNotificationClient : IEmailNotificationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmailNotificationClient> _logger;

    public EmailNotificationClient(HttpClient httpClient, ILogger<EmailNotificationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> SendTransactionReceiptEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { toEmail, subject, htmlBody };
            var response = await _httpClient.PostAsJsonAsync("api/v1/notifications/email/send", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Notification] Receipt dispatch failed: {Status} {Error}", response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Notification] Connection error while sending receipt.");
            return false;
        }
    }
}
```

### 3. Dependency Injection Registration
In `Wallet.Infrastructure/DependencyInjection.cs`:
```csharp
var notificationBaseUrl = Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_URL")
    ?? configuration["NOTIFICATION_SERVICE_URL"]
    ?? configuration["Services:Notification:BaseUrl"]
    ?? "http://localhost:8007/";

services.AddHttpClient<IEmailNotificationClient, EmailNotificationClient>(client =>
{
    client.BaseAddress = new Uri(notificationBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

### 4. Injection into MediatR Command Handler
Inject `IEmailNotificationClient` directly into the appropriate Command Handler (e.g., `TopupVnPayCommandHandler.cs`):
```csharp
internal sealed class TopupVnPayCommandHandler : IRequestHandler<TopupVnPayCommand, TopupResult>
{
    private readonly IWalletDbContext _context;
    private readonly IEmailNotificationClient _emailClient;
    // ...
}
```
