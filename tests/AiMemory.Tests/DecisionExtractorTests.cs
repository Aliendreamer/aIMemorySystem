using AiMemory.Core;
using AiMemory.Ingestion;

namespace AiMemory.Tests;

public class DecisionExtractorTests
{
    private sealed class FakeChatModel(string response, bool expectCall = true) : IChatModel
    {
        public int Calls { get; private set; }

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct = default)
        {
            Calls++;
            Assert.True(expectCall, "model should not have been called");
            return Task.FromResult(response);
        }
    }

    private static MemoryRecord Record(string text, ItemType type = ItemType.WorkItem) => new()
    {
        Id = "wi-1",
        Project = "Payments",
        Source = SourceKind.AzureDevOps,
        SourceId = "wi-1",
        ItemType = type,
        Text = text,
    };

    [Fact]
    public async Task Extract_DeclinedItem_ReturnsDeclinedRecordWithEdges()
    {
        var model = new FakeChatModel("""
            {"hasDecision":true,"type":"declined","rationale":"Rejected: adds unacceptable coupling.",
             "alternativesRejected":["inline the call"],"supersedes":["adr-3"]}
            """);
        var extractor = new DecisionExtractor(model);

        var decision = await extractor.ExtractAsync(Record("We will not do X because..."));

        Assert.NotNull(decision);
        Assert.Equal(DecisionType.Declined, decision!.Type);
        Assert.Equal("Rejected: adds unacceptable coupling.", decision.Rationale);
        Assert.Contains("inline the call", decision.AlternativesRejected);
        Assert.Contains("adr-3", decision.Supersedes);
    }

    [Fact]
    public async Task Extract_AdrLimitation_ReturnsConstraint()
    {
        var model = new FakeChatModel("""
            {"hasDecision":true,"type":"constraint","rationale":"Throughput capped by the single-writer store."}
            """);
        var extractor = new DecisionExtractor(model);

        var decision = await extractor.ExtractAsync(Record("Known limitation: ...", ItemType.RepoKnowledge));

        Assert.NotNull(decision);
        Assert.Equal(DecisionType.Constraint, decision!.Type);
    }

    [Fact]
    public async Task Extract_RoutineText_ReturnsNull()
    {
        var model = new FakeChatModel("""{"hasDecision":false}""");
        var extractor = new DecisionExtractor(model);

        Assert.Null(await extractor.ExtractAsync(Record("Bumped the version number.")));
    }

    [Fact]
    public async Task Extract_EmptyText_ReturnsNullWithoutCallingModel()
    {
        var model = new FakeChatModel("unused", expectCall: false);
        var extractor = new DecisionExtractor(model);

        Assert.Null(await extractor.ExtractAsync(Record("   ")));
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{"hasDecision":true,"type":"bogus","rationale":"x"}""")]           // unknown type
    [InlineData("""{"hasDecision":true,"type":"1","rationale":"x"}""")]               // numeric type
    [InlineData("""{"hasDecision":true,"type":"declined,chosen","rationale":"x"}""")] // comma-combined
    [InlineData("""{"hasDecision":true,"type":"declined"}""")]                         // missing rationale
    [InlineData("""{"hasDecision":true,"type":"declined","rationale":"   "}""")]       // blank rationale
    public async Task Extract_InvalidOutput_IsDroppedNotStored(string response)
    {
        var extractor = new DecisionExtractor(new FakeChatModel(response));

        Assert.Null(await extractor.ExtractAsync(Record("Some decision text.")));
    }

    [Fact]
    public async Task Extract_NullResponse_IsDroppedNotThrown()
    {
        var extractor = new DecisionExtractor(new FakeChatModel(null!));

        Assert.Null(await extractor.ExtractAsync(Record("Some decision text.")));
    }
}
