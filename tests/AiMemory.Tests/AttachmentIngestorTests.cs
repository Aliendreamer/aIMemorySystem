using System.Text;
using AiMemory.Core;
using AiMemory.Ingestion;
using AiMemory.Storage;

namespace AiMemory.Tests;

public sealed class AttachmentIngestorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, "atttest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryRecord Parent() => new()
    {
        Id = "issue-7",
        Project = "Payments",
        Source = SourceKind.GitHub,
        SourceId = "https://github.com/org/repo/issues/7",
        ItemType = ItemType.Issue,
        State = RecordState.Open,
    };

    [Fact]
    public async Task StoreAndLink_StoresBinaryAndLinksWithoutExtraction()
    {
        var blob = new FileSystemBlobStore(_root);
        var ingestor = new AttachmentIngestor(blob);
        var bytes = Encoding.UTF8.GetBytes("PDF-BYTES");
        var parent = Parent();

        var record = await ingestor.StoreAndLinkAsync(
            parent, "design.pdf", "application/pdf", new MemoryStream(bytes),
            sourceUrl: "https://example/design.pdf");

        Assert.Equal(ItemType.Attachment, record.ItemType);
        Assert.Equal("issue-7", record.Links.AttachmentOf);
        Assert.Equal("application/pdf", record.Attachment!.Mime);
        Assert.Equal(bytes.Length, record.Attachment.Size);
        Assert.Equal(string.Empty, record.Text);   // no extraction in v1

        await using var read = await blob.OpenAsync(record.Attachment.VolumePath);
        using var ms = new MemoryStream();
        await read.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());          // binary actually persisted
    }

    [Fact]
    public async Task StoreAndLink_DistinctFileNames_DoNotCollide()
    {
        var blob = new FileSystemBlobStore(_root);
        var ingestor = new AttachmentIngestor(blob);
        var parent = Parent();

        // These two names would sanitize to the same segment ("a_b.pdf"); the key
        // must still keep them apart so neither overwrites the other.
        var a = await ingestor.StoreAndLinkAsync(parent, "a/b.pdf", "application/pdf",
            new MemoryStream(Encoding.UTF8.GetBytes("AAA")));
        var b = await ingestor.StoreAndLinkAsync(parent, "a_b.pdf", "application/pdf",
            new MemoryStream(Encoding.UTF8.GetBytes("BBB")));

        Assert.NotEqual(a.Attachment!.VolumePath, b.Attachment!.VolumePath);

        await using var readA = await blob.OpenAsync(a.Attachment.VolumePath);
        using var msA = new MemoryStream();
        await readA.CopyToAsync(msA);
        Assert.Equal("AAA", Encoding.UTF8.GetString(msA.ToArray())); // A's bytes not clobbered
    }
}
