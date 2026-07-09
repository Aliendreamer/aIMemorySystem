namespace AiMemory.Core;

/// <summary>Native relationships preserved from the source system.</summary>
public sealed record RecordLinks
{
    public IReadOnlyList<string> WorkItems { get; init; } = [];
    public IReadOnlyList<string> PullRequests { get; init; } = [];
    public IReadOnlyList<string> Commits { get; init; } = [];

    /// <summary>For attachment records: the parent item's identifier.</summary>
    public string? AttachmentOf { get; init; }
}

/// <summary>
/// A decision extracted from a record, with its rationale and typed edges.
/// Edges are stored flat so relationships survive without a graph database.
/// </summary>
public sealed record DecisionInfo
{
    public required DecisionType Type { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<string> AlternativesRejected { get; init; } = [];
    public IReadOnlyList<string> Declines { get; init; } = [];
    public IReadOnlyList<string> Supersedes { get; init; } = [];
    public IReadOnlyList<string> CausedBy { get; init; } = [];
}

/// <summary>Metadata for a stored attachment binary (v1 stores and links only).</summary>
public sealed record AttachmentInfo
{
    public required string Mime { get; init; }
    public long Size { get; init; }
    public string? SourceUrl { get; init; }
    public required string VolumePath { get; init; }
}

/// <summary>
/// The single canonical record every ingested item is normalized into.
/// One embeddable chunk maps to one <see cref="MemoryRecord"/> (chunks share
/// the parent's <see cref="SourceId"/>).
/// </summary>
public sealed record MemoryRecord
{
    public required string Id { get; init; }
    public required string Project { get; init; }
    public required SourceKind Source { get; init; }
    public required string SourceId { get; init; }
    public string? Url { get; init; }
    public required ItemType ItemType { get; init; }
    public DocKind DocKind { get; init; } = DocKind.None;
    public string? Title { get; init; }
    public string Text { get; init; } = "";
    public RecordState State { get; init; } = RecordState.Unknown;
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Author { get; init; }
    public RecordLinks Links { get; init; } = new();
    public DecisionInfo? Decision { get; init; }
    public AttachmentInfo? Attachment { get; init; }
}
