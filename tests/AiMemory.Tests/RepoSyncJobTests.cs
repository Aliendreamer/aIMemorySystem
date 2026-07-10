using AiMemory.Connectors;
using AiMemory.Core;
using AiMemory.Ingestion;

namespace AiMemory.Tests;

public sealed class RepoSyncJobTests : IDisposable
{
    private readonly List<string> _dirs = [];

    private string MakeRepo(string name, params (string Rel, string Content)[] files)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, $"{name}-{Guid.NewGuid():N}");
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        _dirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _dirs.Where(Directory.Exists))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class NullExtractor : IDecisionExtractor
    {
        public Task<DecisionInfo?> ExtractAsync(MemoryRecord record, CancellationToken ct = default) =>
            Task.FromResult<DecisionInfo?>(null);
    }

    private sealed class FixedEmbedder : IEmbedder
    {
        public Task<EmbeddingVector> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new EmbeddingVector([0.1f]));
    }

    private sealed class CountingStore : IVectorStore
    {
        public int Count { get; private set; }

        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default)
        {
            Count += records.Count;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingUpdater : IRepoUpdater
    {
        public Task<string> UpdateAsync(SyncedRepo repo, CancellationToken ct = default) =>
            repo.Project == "Bad"
                ? throw new InvalidOperationException("clone failed")
                : Task.FromResult(repo.Path);
    }

    private static RepoSyncJob BuildJob(IVectorStore store, IRepoUpdater? updater = null)
    {
        var orchestrator = new IngestionOrchestrator(new Chunker(), new NullExtractor(), new FixedEmbedder(), store);
        var ingestor = new RepoIngestor(new RepoKnowledgeScanner(), orchestrator);
        return new RepoSyncJob(updater ?? new NoOpRepoUpdater(), ingestor);
    }

    [Fact]
    public async Task Run_IngestsEachConfiguredRepo()
    {
        var store = new CountingStore();
        var repoA = MakeRepo("a", ("CLAUDE.md", "x"), ("README.md", "y"));
        var repoB = MakeRepo("b", ("README.md", "z"));
        var job = BuildJob(store);

        var result = await job.RunAsync([
            new SyncedRepo { Project = "A", Path = repoA, Source = SourceKind.GitHub },
            new SyncedRepo { Project = "B", Path = repoB, Source = SourceKind.AzureDevOps },
        ]);

        Assert.Equal(2, result.ReposSynced);
        Assert.Equal(0, result.ReposFailed);
        Assert.Equal(3, result.ChunksStored);   // A: CLAUDE.md + README.md, B: README.md
        Assert.Equal(3, store.Count);
    }

    [Fact]
    public async Task Run_IsolatesAFailingRepo_ContinuesTheRest()
    {
        var store = new CountingStore();
        var good = MakeRepo("good", ("CLAUDE.md", "x"));
        var job = BuildJob(store, new ThrowingUpdater());

        var result = await job.RunAsync([
            new SyncedRepo { Project = "Bad", Path = "/does/not/matter", Source = SourceKind.GitHub },
            new SyncedRepo { Project = "Good", Path = good, Source = SourceKind.GitHub },
        ]);

        Assert.Equal(1, result.ReposSynced);
        Assert.Equal(1, result.ReposFailed);
        Assert.Equal(1, result.ChunksStored);
    }

    [Fact]
    public async Task Run_NonexistentPath_CountsAsFailedNotSilentlySynced()
    {
        var store = new CountingStore();
        var job = BuildJob(store); // NoOp updater returns the path as-is

        var result = await job.RunAsync([
            new SyncedRepo { Project = "Missing", Path = "/no/such/repo/path", Source = SourceKind.GitHub },
        ]);

        Assert.Equal(0, result.ReposSynced);
        Assert.Equal(1, result.ReposFailed);   // not a silent "synced, 0 chunks"
        Assert.Equal(0, result.ChunksStored);
    }
}
