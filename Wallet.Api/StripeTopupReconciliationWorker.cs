namespace Wallet.Api;

using MediatR;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Infrastructure.Payments;

internal sealed class StripeTopupReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeTopupReconciliationWorker> _logger;
    private readonly TimeSpan _pollInterval;

    public StripeTopupReconciliationWorker(
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<StripeTopupReconciliationWorker> logger)
        : this(scopes, configuration, logger, TimeSpan.FromMinutes(1))
    {
    }

    internal StripeTopupReconciliationWorker(
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<StripeTopupReconciliationWorker> logger,
        TimeSpan pollInterval)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
        _pollInterval = pollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!StripeSettings.Validate(_configuration))
            {
                _logger.LogInformation("Stripe is not configured; skipping Stripe topup reconciliation worker.");
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Stripe configuration is incomplete; skipping Stripe topup reconciliation worker.");
            return;
        }

        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ReconcilePendingTopupsCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { _logger.LogError(error, "Stripe topup reconciliation failed"); }
        }
    }
}
