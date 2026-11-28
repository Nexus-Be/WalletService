namespace Wallet.Api;

using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;

/// <summary>Every 30 seconds sends again the withdrawal notifications that were queued with the money change but not yet confirmed.</summary>
internal sealed class WithdrawalNotificationRetryWorker(IServiceScopeFactory scopes, ILogger<WithdrawalNotificationRetryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var client = scope.ServiceProvider.GetService<INotificationClient>();
                if (client is null)
                {
                    continue;
                }

                var context = scope.ServiceProvider.GetRequiredService<IWalletDbContext>();
                var handled = await WithdrawalNotificationRetry.RetryPendingAsync(context, client, DateTimeOffset.UtcNow, 20, stoppingToken, logger);
                if (handled > 0)
                {
                    logger.LogInformation("Notification retry handled {Count} event(s)", handled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Withdrawal notification retry failed"); }
        }
    }
}
