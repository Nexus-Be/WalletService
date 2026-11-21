namespace Wallet.Api;

using Wallet.Application.Common.Ledger;

/// <summary>Posts waiting shadow ledger events as journal entries, in order. A failure never touches the money: the row is kept and retried.</summary>
internal sealed class JournalPostingWorker(IServiceScopeFactory scopes, ILogger<JournalPostingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var accountsReady = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var poster = scope.ServiceProvider.GetRequiredService<JournalPoster>();
                if (!accountsReady)
                {
                    await poster.EnsureAccountsAsync(stoppingToken);
                    accountsReady = true;
                }

                var posted = await poster.PostPendingAsync(200, stoppingToken);
                if (posted > 0) logger.LogInformation("Posted {Count} ledger events to the journal.", posted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Journal posting pass failed; it will be tried again."); }
        }
    }
}
