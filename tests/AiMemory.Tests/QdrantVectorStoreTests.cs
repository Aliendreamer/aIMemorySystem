using AiMemory.Core;
using AiMemory.Storage;

namespace AiMemory.Tests;

public class QdrantVectorStoreTests
{
    private static MemoryRecord Sample() => new()
    {
        Id = "Payments:openspec/changes/x/design.md#c0",
        Project = "Payments",
        Source = SourceKind.GitHub,
        SourceId = "openspec/changes/x/design.md",
        Url = "https://example/design.md",
        ItemType = ItemType.RepoKnowledge,
        DocKind = DocKind.OpenSpecDesign,
        Title = "design.md",
        Text = "we declined X because...",
        State = RecordState.Closed,
        Decision = new DecisionInfo { Type = DecisionType.Declined, Rationale = "coupling" },
    };

    [Fact]
    public void ToPointId_IsDeterministicAndDistinct()
    {
        Assert.Equal(QdrantVectorStore.ToPointId("abc"), QdrantVectorStore.ToPointId("abc"));
        Assert.NotEqual(QdrantVectorStore.ToPointId("abc"), QdrantVectorStore.ToPointId("abd"));
    }

    [Fact]
    public void PayloadRoundTrip_PreservesCoreFields()
    {
        var record = Sample();
        var point = QdrantVectorStore.ToPoint(new EmbeddedRecord(record, new EmbeddingVector([0.1f, 0.2f])));

        var restored = QdrantVectorStore.ToRecord(point.Payload);

        Assert.Equal(record.Id, restored.Id);
        Assert.Equal(record.Project, restored.Project);
        Assert.Equal(record.Source, restored.Source);
        Assert.Equal(record.SourceId, restored.SourceId);
        Assert.Equal(record.Url, restored.Url);
        Assert.Equal(record.ItemType, restored.ItemType);
        Assert.Equal(record.DocKind, restored.DocKind);
        Assert.Equal(record.Title, restored.Title);
        Assert.Equal(record.Text, restored.Text);
        Assert.Equal(record.State, restored.State);
        Assert.Equal(DecisionType.Declined, restored.Decision!.Type);
        Assert.Equal("coupling", restored.Decision.Rationale);   // rationale persisted
    }

    [Fact]
    public void ToRecord_UnknownDecisionType_DropsDecisionNotMislabels()
    {
        var point = QdrantVectorStore.ToPoint(new EmbeddedRecord(Sample(), new EmbeddingVector([0.1f])));
        point.Payload["decision_type"] = "garbage"; // corrupt / renamed enum value

        var restored = QdrantVectorStore.ToRecord(point.Payload);

        Assert.Null(restored.Decision); // not silently mapped to Chosen
    }

    [Fact]
    public void ToRecord_MissingOptionalFields_AreNull()
    {
        var minimal = new MemoryRecord
        {
            Id = "i-1",
            Project = "P",
            Source = SourceKind.AzureDevOps,
            SourceId = "i-1",
            ItemType = ItemType.WorkItem,
            Text = "plain",
        };
        var point = QdrantVectorStore.ToPoint(new EmbeddedRecord(minimal, new EmbeddingVector([0.1f])));

        var restored = QdrantVectorStore.ToRecord(point.Payload);

        Assert.Null(restored.Title);
        Assert.Null(restored.Url);
        Assert.Null(restored.Decision);
    }

    [Fact]
    public void BuildFilter_ProjectOnly_HasSingleKeywordCondition()
    {
        var filter = QdrantVectorStore.BuildFilter(new RetrievalFilter { Project = "Payments" });

        var condition = Assert.Single(filter.Must);
        Assert.Equal("project", condition.Field.Key);
        Assert.Equal("Payments", condition.Field.Match.Keyword);
    }

    [Fact]
    public void BuildFilter_WithLists_AddsMatchAnyConditions()
    {
        var filter = QdrantVectorStore.BuildFilter(new RetrievalFilter
        {
            Project = "Payments",
            ItemTypes = [ItemType.Issue],
            DecisionTypes = [DecisionType.Declined],
        });

        Assert.Equal(3, filter.Must.Count);
        var itemType = Assert.Single(filter.Must, c => c.Field.Key == "item_type");
        Assert.Contains("Issue", itemType.Field.Match.Keywords.Strings);
        var decision = Assert.Single(filter.Must, c => c.Field.Key == "decision_type");
        Assert.Contains("Declined", decision.Field.Match.Keywords.Strings);
    }
}
