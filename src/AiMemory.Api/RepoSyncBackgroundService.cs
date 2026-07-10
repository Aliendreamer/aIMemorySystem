using AiMemory.Ingestion;
using Microsoft.Extensions.Options;

namespace AiMemory.Api;

/// <summary>
/// Scheduled sync: runs the ingest for all configured repos on startup and then on a
/// fixed interval. Disabled when no repos are configured. Doubles as the reconcile
/// pass that event-driven (webhook) sync will rely on later.
/// </summary>
public sealed class RepoSyncBackgroundService(
    RepoSyncJob job,
    IOptions<RepoSyncOptions> options,
    ILogger<RepoSyncBackgroundService> logger) : BackgroundService
{
    private readonly RepoSyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Repos.Count == 0)
        {
            logger.LogInformation("RepoSync: no repos configured; scheduled sync disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await job.RunAsync(_options.Repos, stoppingToken);
                logger.LogInformation(
                    "RepoSync: {Synced} synced, {Failed} failed, {Chunks} chunks stored.",
                    result.ReposSynced, result.ReposFailed, result.ChunksStored);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RepoSync pass failed; will retry next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
