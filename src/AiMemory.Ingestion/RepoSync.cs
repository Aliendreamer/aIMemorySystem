using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>A repository the scheduled sync keeps ingested.</summary>
public sealed class SyncedRepo
{
    public string Project { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public SourceKind Source { get; set; } = SourceKind.GitHub;

    /// <summary>Optional remote to refresh the local working copy from before scanning.</summary>
    public string? RemoteUrl { get; set; }
}

/// <summary>Scheduled-sync configuration (bound from the <c>RepoSync</c> config section).</summary>
public sealed class RepoSyncOptions
{
    public int IntervalMinutes { get; set; } = 30;
    public List<SyncedRepo> Repos { get; set; } = [];
}

/// <summary>Ensures a repo's local working copy is current, returning the path to scan.</summary>
public interface IRepoUpdater
{
    Task<string> UpdateAsync(SyncedRepo repo, CancellationToken ct = default);
}

/// <summary>Default updater: scans the configured local path as-is (no pull).</summary>
public sealed class NoOpRepoUpdater : IRepoUpdater
{
    public Task<string> UpdateAsync(SyncedRepo repo, CancellationToken ct = default) =>
        Task.FromResult(repo.Path);
}

/// <summary>Outcome of one scheduled-sync pass.</summary>
public sealed record RepoSyncResult(int ReposSynced, int ReposFailed, int ChunksStored);

/// <summary>
/// One pass of the scheduled sync: update then ingest each configured repo. A single
/// repo's failure is isolated and counted — it never aborts the rest of the pass.
/// </summary>
public sealed class RepoSyncJob
{
    private readonly IRepoUpdater _updater;
    private readonly RepoIngestor _ingestor;

    public RepoSyncJob(IRepoUpdater updater, RepoIngestor ingestor)
    {
        ArgumentNullException.ThrowIfNull(updater);
        ArgumentNullException.ThrowIfNull(ingestor);
        _updater = updater;
        _ingestor = ingestor;
    }

    public async Task<RepoSyncResult> RunAsync(IReadOnlyList<SyncedRepo> repos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repos);
        var synced = 0;
        var failed = 0;
        var chunks = 0;

        foreach (var repo in repos)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = await _updater.UpdateAsync(repo, ct);
                var result = await _ingestor.IngestAsync(repo.Project, path, repo.Source, ct);
                chunks += result.ChunksStored;
                synced++;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                failed++; // isolate a bad repo; keep syncing the rest
            }
        }

        return new RepoSyncResult(synced, failed, chunks);
    }
}
