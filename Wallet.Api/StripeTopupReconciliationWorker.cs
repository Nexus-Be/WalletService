namespace Wallet.Api;

using MediatR;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;
using Wallet.Infrastructure.Payments;

internal sealed class StripeTopupReconciliationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<StripeTopupReconciliationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!StripeSettings.Validate(configuration))
            {
                logger.LogInformation("Stripe is not configured; skipping Stripe topup reconciliation worker.");
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Stripe configuration is incomplete; skipping Stripe topup reconciliation worker.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ReconcilePendingTopupsCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Stripe topup reconciliation failed"); }
        }
    }
}
