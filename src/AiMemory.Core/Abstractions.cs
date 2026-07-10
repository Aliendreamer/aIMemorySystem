namespace AiMemory.Core;

/// <summary>A dense embedding with an optional sparse component for hybrid search.</summary>
public sealed record EmbeddingVector(
    IReadOnlyList<float> Dense,
    IReadOnlyDictionary<uint, float>? Sparse = null);

/// <summary>A record paired with its embedding, ready to persist.</summary>
public sealed record EmbeddedRecord(MemoryRecord Record, EmbeddingVector Vector);

/// <summary>A record returned from retrieval, with its similarity score.</summary>
public sealed record RetrievedChunk(MemoryRecord Record, float Score);

/// <summary>Payload filter constraints applied during retrieval.</summary>
public sealed record RetrievalFilter
{
    public required string Project { get; init; }
    public IReadOnlyList<ItemType>? ItemTypes { get; init; }
    public IReadOnlyList<RecordState>? States { get; init; }
    public IReadOnlyList<DecisionType>? DecisionTypes { get; init; }
    public IReadOnlyList<DocKind>? DocKinds { get; init; }
}

/// <summary>Fetches items from a source system and normalizes them to <see cref="MemoryRecord"/>.</summary>
public interface IConnector
{
    SourceKind Source { get; }

    IAsyncEnumerable<MemoryRecord> FetchAsync(string project, CancellationToken ct = default);
}

/// <summary>Splits a record's text into embeddable chunks that keep their parent link.</summary>
public interface IChunker
{
    IEnumerable<MemoryRecord> Chunk(MemoryRecord record);
}

/// <summary>Extracts a structured decision (or none) from a record's text.</summary>
public interface IDecisionExtractor
{
    Task<DecisionInfo?> ExtractAsync(MemoryRecord record, CancellationToken ct = default);
}

/// <summary>Produces embeddings using a self-hosted model.</summary>
public interface IEmbedder
{
    Task<EmbeddingVector> EmbedAsync(string text, CancellationToken ct = default);
}

/// <summary>Persists and retrieves embedded records.</summary>
public interface IVectorStore
{
    Task EnsureInitializedAsync(CancellationToken ct = default);

    Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default);

    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        EmbeddingVector query,
        RetrievalFilter filter,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the stored content hash for each given record id that already exists, for
    /// change-detection. Default: an empty map (no index — every record treated as changed).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetExistingHashesAsync(
        IReadOnlyCollection<string> recordIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

/// <summary>Result of persisting a blob: the path to retrieve it by and its byte length.</summary>
public sealed record BlobInfo(string VolumePath, long Size);

/// <summary>Stores attachment binaries on a self-hostable volume.</summary>
public interface IBlobStore
{
    /// <summary>Saves content and returns its retrieval path and byte length.</summary>
    Task<BlobInfo> SaveAsync(string relativePath, Stream content, CancellationToken ct = default);

    Task<Stream> OpenAsync(string volumePath, CancellationToken ct = default);
}

/// <summary>A self-hosted chat model invoked in constrained-JSON mode.</summary>
public interface IChatModel
{
    /// <summary>
    /// Completes a prompt constrained to <paramref name="jsonSchema"/>, returning raw JSON.
    /// Implementations run response-only (no chain-of-thought).
    /// </summary>
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string jsonSchema,
        CancellationToken ct = default);

    /// <summary>Completes a prompt as free-form text (used for answer summarization).</summary>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
