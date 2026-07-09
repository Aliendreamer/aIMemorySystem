using AiMemory.Core;
using AiMemory.Ingestion;

namespace AiMemory.Tests;

public class ChunkerTests
{
    private static MemoryRecord Record(string text) => new()
    {
        Id = "doc-1",
        Project = "Payments",
        Source = SourceKind.GitHub,
        SourceId = "src-1",
        ItemType = ItemType.RepoKnowledge,
        Text = text,
    };

    [Fact]
    public void Chunk_ShortText_ReturnsSingleUnchanged()
    {
        var chunker = new Chunker(maxChars: 1000, overlap: 100);
        var record = Record("short enough to fit in one chunk");

        var chunks = chunker.Chunk(record).ToList();

        Assert.Single(chunks);
        Assert.Same(record, chunks[0]);
    }

    [Fact]
    public void Chunk_LongText_SplitsAndPreservesParentLink()
    {
        var chunker = new Chunker(maxChars: 1000, overlap: 100);
        var record = Record(new string('a', 2500));

        var chunks = chunker.Chunk(record).ToList();

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.Equal("src-1", c.SourceId));   // parent link preserved
        Assert.All(chunks, c => Assert.Equal("Payments", c.Project));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 1000));
        Assert.All(chunks, c => Assert.StartsWith("doc-1#c", c.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void Chunk_InvalidOverlap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Chunker(maxChars: 100, overlap: 100));
    }

    [Fact]
    public void Chunk_DoesNotSplitSurrogatePairs()
    {
        var chunker = new Chunker(maxChars: 10, overlap: 2);
        // "😀" (U+1F600) is a surrogate pair straddling index 9-10 → the naive
        // window [0,10) would cut it in half.
        var record = Record(new string('a', 9) + "\U0001F600" + new string('b', 20));

        var chunks = chunker.Chunk(record).ToList();

        Assert.All(chunks, c =>
        {
            Assert.False(char.IsHighSurrogate(c.Text[^1]), "chunk ends with a lone high surrogate");
            Assert.False(char.IsLowSurrogate(c.Text[0]), "chunk starts with a lone low surrogate");
        });
        // The emoji survives intact somewhere in the output.
        Assert.Contains(chunks, c => c.Text.Contains("\U0001F600", StringComparison.Ordinal));
    }
}
