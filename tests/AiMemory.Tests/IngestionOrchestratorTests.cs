using AiMemory.Core;
using AiMemory.Ingestion;

namespace AiMemory.Tests;

public class IngestionOrchestratorTests
{
    private sealed class FakeExtractor(Func<MemoryRecord, DecisionInfo?> map) : IDecisionExtractor
    {
        public Task<DecisionInfo?> ExtractAsync(MemoryRecord record, CancellationToken ct = default) =>
            Task.FromResult(map(record));
    }

    private sealed class FakeEmbedder : IEmbedder
    {
        public Task<EmbeddingVector> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new EmbeddingVector([0.5f]));
    }

    private sealed class FakeStore : IVectorStore
    {
        public bool Initialized { get; private set; }
        public List<EmbeddedRecord> Upserted { get; } = [];

        public Task EnsureInitializedAsync(CancellationToken ct = default)
        {
            Initialized = true;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default)
        {
            Upserted.AddRange(records);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingStore : IVectorStore
    {
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default) =>
            throw new InvalidOperationException("store down");

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class PrefilledStore(IReadOnlyDictionary<string, string> hashes) : IVectorStore
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

        public Task<IReadOnlyDictionary<string, string>> GetExistingHashesAsync(IReadOnlyCollection<string> recordIds, CancellationToken ct = default) =>
            Task.FromResult(hashes);
    }

    private sealed class CountingExtractor : IDecisionExtractor
    {
        public int Calls { get; private set; }

        public Task<DecisionInfo?> ExtractAsync(MemoryRecord record, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<DecisionInfo?>(null);
        }
    }

    private static MemoryRecord Record(string id, string text) => new()
    {
        Id = id,
        Project = "Payments",
        Source = SourceKind.GitHub,
        SourceId = id,
        ItemType = ItemType.Issue,
        Title = id,
        Text = text,
    };

    private static async IAsyncEnumerable<MemoryRecord> Stream(params MemoryRecord[] records)
    {
        foreach (var record in records)
        {
            yield return record;
            await Task.Yield();
        }
    }

    private static IngestionOrchestrator Build(FakeStore store, Func<MemoryRecord, DecisionInfo?>? extract = null) =>
        new(new Chunker(), new FakeExtractor(extract ?? (_ => null)), new FakeEmbedder(), store);

    [Fact]
    public async Task Ingest_InitializesStoreAndUpsertsAllChunks()
    {
        var store = new FakeStore();
        var orchestrator = Build(store);

        var result = await orchestrator.IngestAsync(Stream(Record("a", "short a"), Record("b", "short b")));

        Assert.True(store.Initialized);
        Assert.Equal(2, result.ChunksStored);       // short text → one chunk each
        Assert.Equal(0, result.RecordsFailed);
        Assert.Equal(2, store.Upserted.Count);
    }

    [Fact]
    public async Task Ingest_AttachesExtractedDecisionToStoredChunks()
    {
        var store = new FakeStore();
        var decision = new DecisionInfo { Type = DecisionType.Declined, Rationale = "because" };
        var orchestrator = Build(store, r => r.Id == "decl" ? decision : null);

        await orchestrator.IngestAsync(Stream(Record("decl", "we declined X"), Record("plain", "routine")));

        var declChunk = Assert.Single(store.Upserted, e => e.Record.SourceId == "decl");
        Assert.Equal(DecisionType.Declined, declChunk.Record.Decision!.Type);
        var plainChunk = Assert.Single(store.Upserted, e => e.Record.SourceId == "plain");
        Assert.Null(plainChunk.Record.Decision);
    }

    [Fact]
    public async Task Ingest_IsolatesRecordFailure_StoresNothingPartialForIt()
    {
        var store = new FakeStore();
        var orchestrator = Build(store, r => r.Id == "bad"
            ? throw new InvalidOperationException("extract boom")
            : null);

        var result = await orchestrator.IngestAsync(Stream(Record("ok1", "fine"), Record("bad", "boom"), Record("ok2", "fine")));

        Assert.Equal(1, result.RecordsFailed);
        Assert.Equal(2, result.ChunksStored);
        Assert.DoesNotContain(store.Upserted, e => e.Record.SourceId == "bad");
    }

    [Fact]
    public async Task Ingest_EmptyStream_StoresNothing()
    {
        var store = new FakeStore();

        var result = await Build(store).IngestAsync(Stream());

        Assert.Equal(0, result.ChunksStored);
        Assert.Empty(store.Upserted);
    }

    [Fact]
    public async Task Ingest_UnchangedChunk_IsSkipped_NoExtractionNoUpsert()
    {
        var record = Record("r1", "hello");   // short text → single chunk with Id "r1"
        var store = new PrefilledStore(new Dictionary<string, string>
        {
            ["r1"] = Hashing.ContentHash("hello"),   // already stored with the same content
        });
        var extractor = new CountingExtractor();
        var orchestrator = new IngestionOrchestrator(new Chunker(), extractor, new FakeEmbedder(), store);

        var result = await orchestrator.IngestAsync(Stream(record));

        Assert.Equal(0, result.ChunksStored);
        Assert.Equal(1, result.ChunksSkipped);
        Assert.Empty(store.Upserted);       // nothing re-embedded/re-upserted
        Assert.Equal(0, extractor.Calls);   // extraction skipped for the unchanged record
    }

    [Fact]
    public async Task Ingest_StoreFlushFailure_DropsBatchWithoutAborting()
    {
        var orchestrator = new IngestionOrchestrator(
            new Chunker(), new FakeExtractor(_ => null), new FakeEmbedder(), new ThrowingStore());

        var result = await orchestrator.IngestAsync(Stream(Record("a", "x"), Record("b", "y")));

        Assert.Equal(0, result.ChunksStored);
        Assert.Equal(2, result.ChunksDropped);   // both chunks failed to persist, run completed
        Assert.Equal(0, result.RecordsFailed);
    }

    [Fact]
    public async Task Ingest_NonCallerCancellation_IsIsolatedNotFatal()
    {
        var store = new FakeStore();
        // An embedder/extractor timeout surfaces as OperationCanceledException while the
        // caller's token is still live — it must be counted, not abort the run.
        var orchestrator = Build(store, r => r.Id == "timeout"
            ? throw new OperationCanceledException()
            : null);

        var result = await orchestrator.IngestAsync(Stream(Record("ok", "x"), Record("timeout", "y")));

        Assert.Equal(1, result.RecordsFailed);
        Assert.Equal(1, result.ChunksStored);
    }
}
