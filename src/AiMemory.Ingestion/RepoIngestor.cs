using AiMemory.Connectors;
using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>
/// Scans a repository's in-repo knowledge artifacts and runs them through the
/// ingestion pipeline. Shared by the manual endpoint and the scheduled sync so both
/// use one code path.
/// </summary>
public sealed class RepoIngestor
{
    private readonly RepoKnowledgeScanner _scanner;
    private readonly IngestionOrchestrator _orchestrator;

    public RepoIngestor(RepoKnowledgeScanner scanner, IngestionOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(orchestrator);
        _scanner = scanner;
        _orchestrator = orchestrator;
    }

    public Task<IngestionResult> IngestAsync(string project, string repoPath, SourceKind source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        if (!Directory.Exists(repoPath))
        {
            // Surface a misconfigured/unmounted path as a failure instead of a silent
            // "synced, 0 chunks" (the scheduled job counts this as a failed repo).
            throw new DirectoryNotFoundException($"Repository path not found: {repoPath}");
        }

        var records = _scanner.Scan(repoPath, project, source);
        return _orchestrator.IngestAsync(ToAsyncEnumerable(records), ct);
    }

    private static async IAsyncEnumerable<MemoryRecord> ToAsyncEnumerable(IEnumerable<MemoryRecord> records)
    {
        foreach (var record in records)
        {
            yield return record;
        }

        await Task.CompletedTask;
    }
}
