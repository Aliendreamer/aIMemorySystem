using AiMemory.Api;
using AiMemory.Connectors;
using AiMemory.Core;
using AiMemory.Ingestion;

namespace AiMemory.Tests;

public sealed class AiMemoryEndpointsTests : IDisposable
{
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
        public List<EmbeddedRecord> Upserted { get; } = [];

        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default)
        {
            Upserted.AddRange(records);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private readonly string _repo =
        Path.Combine(AppContext.BaseDirectory, "endpointrepo-" + Guid.NewGuid().ToString("N"));

    public AiMemoryEndpointsTests()
    {
        Directory.CreateDirectory(_repo);
        File.WriteAllText(Path.Combine(_repo, "CLAUDE.md"), "agent instructions");
        File.WriteAllText(Path.Combine(_repo, "README.md"), "readme");
        File.WriteAllText(Path.Combine(_repo, "code.cs"), "not an artifact");
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo))
        {
            Directory.Delete(_repo, recursive: true);
        }
    }

    [Fact]
    public async Task IngestRepo_ScansArtifactsAndRunsThePipeline()
    {
        var store = new CountingStore();
        var orchestrator = new IngestionOrchestrator(new Chunker(), new NullExtractor(), new FixedEmbedder(), store);
        var ingestor = new RepoIngestor(new RepoKnowledgeScanner(), orchestrator);
        var request = new IngestRepoRequest("Payments", _repo, SourceKind.GitHub);

        var result = await AiMemoryEndpoints.IngestRepoAsync(request, ingestor, CancellationToken.None);

        Assert.Equal(2, result.ChunksStored);        // CLAUDE.md + README.md; code.cs skipped
        Assert.Equal(0, result.RecordsFailed);
        Assert.Equal(2, store.Upserted.Count);
        Assert.All(store.Upserted, e => Assert.Equal(ItemType.RepoKnowledge, e.Record.ItemType));
    }
}
