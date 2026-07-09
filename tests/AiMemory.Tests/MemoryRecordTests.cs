using AiMemory.Core;

namespace AiMemory.Tests;

public class MemoryRecordTests
{
    private static MemoryRecord NewRecord() => new()
    {
        Id = "id-1",
        Project = "Payments",
        Source = SourceKind.GitHub,
        SourceId = "https://github.com/org/repo/issues/7",
        ItemType = ItemType.Issue,
    };

    [Fact]
    public void Defaults_AreEmptyNotNull()
    {
        var r = NewRecord();

        Assert.Equal(DocKind.None, r.DocKind);
        Assert.Equal(RecordState.Unknown, r.State);
        Assert.Equal(string.Empty, r.Text);
        Assert.NotNull(r.Links);
        Assert.Empty(r.Links.WorkItems);
        Assert.Empty(r.Links.PullRequests);
        Assert.Empty(r.Links.Commits);
        Assert.Null(r.Links.AttachmentOf);
        Assert.Null(r.Decision);
        Assert.Null(r.Attachment);
    }

    [Fact]
    public void DecisionInfo_CapturesTypedEdges()
    {
        var r = NewRecord() with
        {
            Decision = new DecisionInfo
            {
                Type = DecisionType.Declined,
                Rationale = "Rejected: adds unacceptable coupling.",
                AlternativesRejected = ["inline the call", "shared mutable cache"],
                Supersedes = ["adr-3"],
            },
        };

        Assert.NotNull(r.Decision);
        Assert.Equal(DecisionType.Declined, r.Decision!.Type);
        Assert.Contains("adr-3", r.Decision.Supersedes);
        Assert.Equal(2, r.Decision.AlternativesRejected.Count);
    }

    [Fact]
    public void Attachment_IsLinkedToParentViaAttachmentOf()
    {
        var r = NewRecord() with
        {
            ItemType = ItemType.Attachment,
            Links = new RecordLinks { AttachmentOf = "issue-7" },
            Attachment = new AttachmentInfo
            {
                Mime = "application/pdf",
                Size = 2048,
                SourceUrl = "https://example/attach.pdf",
                VolumePath = "Payments/issue-7/attach.pdf",
            },
        };

        Assert.Equal("issue-7", r.Links.AttachmentOf);
        Assert.Equal("application/pdf", r.Attachment!.Mime);
        Assert.Equal("Payments/issue-7/attach.pdf", r.Attachment.VolumePath);
    }

    [Fact]
    public void RepoKnowledge_CarriesDocKind()
    {
        var r = NewRecord() with
        {
            ItemType = ItemType.RepoKnowledge,
            DocKind = DocKind.OpenSpecDesign,
        };

        Assert.Equal(ItemType.RepoKnowledge, r.ItemType);
        Assert.Equal(DocKind.OpenSpecDesign, r.DocKind);
    }
}
