namespace Wallet.Api;

using MediatR;
using Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;

internal sealed class StripeTopupReconciliationWorker(IServiceScopeFactory scopes, ILogger<StripeTopupReconciliationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
