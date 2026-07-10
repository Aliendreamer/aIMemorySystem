using AiMemory.Core;
using AiMemory.Query;

namespace AiMemory.Tests;

public class QueryServiceTests
{
    private sealed class FakeEmbedder : IEmbedder
    {
        public Task<EmbeddingVector> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new EmbeddingVector([0.1f, 0.2f, 0.3f]));
    }

    private sealed class FakeVectorStore(IReadOnlyList<RetrievedChunk> hits) : IVectorStore
    {
        public RetrievalFilter? LastFilter { get; private set; }

        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
            EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default)
        {
            LastFilter = filter;
            return Task.FromResult(hits);
        }
    }

    private sealed class FakeChatModel(string summary) : IChatModel
    {
        public int Calls { get; private set; }

        public Task<string> CompleteJsonAsync(string s, string u, string schema, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(summary);
        }
    }

    // Returns nothing for a decision-type-filtered query, but hits for a project-only
    // (fallback) query — to exercise the recall fallback path.
    private sealed class TypedMissStore(IReadOnlyList<RetrievedChunk> fallbackHits) : IVectorStore
    {
        public int Searches { get; private set; }

        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
            EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default)
        {
            Searches++;
            var result = filter.DecisionTypes is null ? fallbackHits : [];
            return Task.FromResult(result);
        }
    }

    private static RetrievedChunk Hit(string sourceId, string title) => new(
        new MemoryRecord
        {
            Id = sourceId,
            Project = "Payments",
            Source = SourceKind.GitHub,
            SourceId = sourceId,
            ItemType = ItemType.Issue,
            Title = title,
            Text = "evidence text",
        },
        Score: 0.9f);

    [Fact]
    public async Task Answer_Declined_ScopesFilterToProjectAndDeclinedDecisions()
    {
        var store = new FakeVectorStore([Hit("i-1", "Declined thing")]);
        var service = new QueryService(new FakeEmbedder(), store, new FakeChatModel("summary"));

        await service.AnswerAsync("Payments", QuestionKind.Declined);

        Assert.Equal("Payments", store.LastFilter!.Project);
        Assert.NotNull(store.LastFilter.DecisionTypes);
        Assert.Contains(DecisionType.Declined, store.LastFilter.DecisionTypes!);
    }

    [Fact]
    public async Task Answer_Limitations_FiltersByConstraint()
    {
        var store = new FakeVectorStore([Hit("adr-1", "A limitation")]);
        var service = new QueryService(new FakeEmbedder(), store, new FakeChatModel("summary"));

        await service.AnswerAsync("Payments", QuestionKind.Limitations);

        Assert.Contains(DecisionType.Constraint, store.LastFilter!.DecisionTypes!);
    }

    [Fact]
    public async Task Answer_WithHits_ReturnsSummaryAndCitations()
    {
        var store = new FakeVectorStore([Hit("i-1", "First"), Hit("i-2", "Second"), Hit("i-1", "First again")]);
        var service = new QueryService(new FakeEmbedder(), store, new FakeChatModel("Two things were declined."));

        var answer = await service.AnswerAsync("Payments", QuestionKind.Declined);

        Assert.True(answer.HasEvidence);
        Assert.Equal("Two things were declined.", answer.Summary);
        // Citations are de-duplicated by source id (i-1 appeared twice).
        Assert.Equal(2, answer.Citations.Count);
        Assert.Contains(answer.Citations, c => c.SourceId == "i-1");
        Assert.Contains(answer.Citations, c => c.SourceId == "i-2");
    }

    [Fact]
    public async Task Answer_NoHits_ReturnsNoEvidence_WithoutCallingModel()
    {
        var store = new FakeVectorStore([]);
        var model = new FakeChatModel("should not be used");
        var service = new QueryService(new FakeEmbedder(), store, model);

        var answer = await service.AnswerAsync("Payments", QuestionKind.Declined);

        Assert.False(answer.HasEvidence);
        Assert.Empty(answer.Citations);
        Assert.Contains("No supporting evidence", answer.Summary, StringComparison.Ordinal);
        Assert.Equal(0, model.Calls); // no fabrication
    }

    [Fact]
    public async Task Answer_NoTypedHits_FallsBackToProjectScopedSearch()
    {
        var store = new TypedMissStore([Hit("i-9", "A real limitation")]);
        var service = new QueryService(new FakeEmbedder(), store, new FakeChatModel("summary"));

        var answer = await service.AnswerAsync("Payments", QuestionKind.Limitations);

        Assert.True(answer.HasEvidence);         // fallback surfaced evidence
        Assert.Equal(2, store.Searches);         // primary (empty) + project-only fallback
        Assert.Single(answer.Citations);
    }

    [Fact]
    public async Task Answer_StoreReturnsNull_TreatedAsNoEvidence()
    {
        var service = new QueryService(new FakeEmbedder(), new FakeVectorStore(null!), new FakeChatModel("x"));

        var answer = await service.AnswerAsync("Payments", QuestionKind.Declined);

        Assert.False(answer.HasEvidence);
    }
}
