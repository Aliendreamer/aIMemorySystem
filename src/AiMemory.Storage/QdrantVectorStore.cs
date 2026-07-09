using System.Security.Cryptography;
using System.Text;
using AiMemory.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AiMemory.Storage;

/// <summary>
/// <see cref="IVectorStore"/> backed by Qdrant. Records are stored as points whose
/// payload carries the filterable fields (project, item_type, doc_kind, state,
/// decision_type) plus enough to reconstruct a <see cref="MemoryRecord"/> for citations.
/// <para>
/// Invariant: <c>vectorSize</c> must match the dimensionality of the configured
/// <see cref="IEmbedder"/> (a mismatch surfaces only as an upsert error against a live Qdrant).
/// </para>
/// </summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private const string KeyRecordId = "record_id";
    private const string KeyProject = "project";
    private const string KeySource = "source";
    private const string KeySourceId = "source_id";
    private const string KeyItemType = "item_type";
    private const string KeyDocKind = "doc_kind";
    private const string KeyState = "state";
    private const string KeyText = "text";
    private const string KeyTitle = "title";
    private const string KeyUrl = "url";
    private const string KeyDecisionType = "decision_type";
    private const string KeyDecisionRationale = "decision_rationale";

    private readonly QdrantClient _client;
    private readonly string _collection;
    private readonly ulong _vectorSize;

    public QdrantVectorStore(QdrantClient client, string collectionName = "aimemory", int vectorSize = 1024)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vectorSize);
        _client = client;
        _collection = collectionName;
        _vectorSize = (ulong)vectorSize;
    }

    // Check-then-create: fine for a single-instance v1. Concurrent startup of two
    // instances could race; the loser's CreateCollectionAsync would throw "already exists".
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (!await _client.CollectionExistsAsync(_collection, ct))
        {
            await _client.CreateCollectionAsync(
                _collection,
                new VectorParams { Size = _vectorSize, Distance = Distance.Cosine },
                cancellationToken: ct);
        }
    }

    public async Task UpsertAsync(IReadOnlyCollection<EmbeddedRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        var points = records.Select(ToPoint).ToList();
        await _client.UpsertAsync(_collection, points, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        EmbeddingVector query, RetrievalFilter filter, int limit, CancellationToken ct = default)
    {
        var results = await _client.SearchAsync(
            _collection,
            query.Dense.ToArray(),
            filter: BuildFilter(filter),
            limit: (ulong)limit,
            payloadSelector: true,
            cancellationToken: ct);

        return results.Select(p => new RetrievedChunk(ToRecord(p.Payload), p.Score)).ToArray();
    }

    internal static PointStruct ToPoint(EmbeddedRecord embedded)
    {
        var r = embedded.Record;
        var point = new PointStruct
        {
            Id = ToPointId(r.Id),
            Vectors = embedded.Vector.Dense.ToArray(),
        };

        point.Payload.Add(KeyRecordId, r.Id);
        point.Payload.Add(KeyProject, r.Project);
        point.Payload.Add(KeySource, r.Source.ToString());
        point.Payload.Add(KeySourceId, r.SourceId);
        point.Payload.Add(KeyItemType, r.ItemType.ToString());
        point.Payload.Add(KeyDocKind, r.DocKind.ToString());
        point.Payload.Add(KeyState, r.State.ToString());
        point.Payload.Add(KeyText, r.Text);
        if (r.Title is not null)
        {
            point.Payload.Add(KeyTitle, r.Title);
        }

        if (r.Url is not null)
        {
            point.Payload.Add(KeyUrl, r.Url);
        }

        if (r.Decision is not null)
        {
            point.Payload.Add(KeyDecisionType, r.Decision.Type.ToString());
            point.Payload.Add(KeyDecisionRationale, r.Decision.Rationale);
        }

        return point;
    }

    // Qdrant point ids must be a UUID or unsigned int, so derive a deterministic GUID
    // from the record id (kept verbatim in the payload as record_id).
    internal static Guid ToPointId(string recordId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(recordId), hash);
        return new Guid(hash[..16]);
    }

    internal static MemoryRecord ToRecord(IReadOnlyDictionary<string, Value> payload)
    {
        string Str(string key) => payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;
        string? Opt(string key) => payload.ContainsKey(key) ? Str(key) : null;

        // Only reconstruct a decision when the type parses to a known value — an
        // unrecognized decision_type is dropped rather than silently mislabeled.
        var decision = payload.ContainsKey(KeyDecisionType) && TryParseEnum<DecisionType>(Str(KeyDecisionType), out var decisionType)
            ? new DecisionInfo { Type = decisionType, Rationale = Str(KeyDecisionRationale) }
            : null;

        return new MemoryRecord
        {
            Id = Str(KeyRecordId),
            Project = Str(KeyProject),
            Source = ParseEnum(Str(KeySource), SourceKind.GitHub),
            SourceId = Str(KeySourceId),
            Url = Opt(KeyUrl),
            ItemType = ParseEnum(Str(KeyItemType), ItemType.RepoKnowledge),
            DocKind = ParseEnum(Str(KeyDocKind), DocKind.None),
            Title = Opt(KeyTitle),
            Text = Str(KeyText),
            State = ParseEnum(Str(KeyState), RecordState.Unknown),
            Decision = decision,
        };
    }

    internal static Filter BuildFilter(RetrievalFilter filter)
    {
        var qdrant = new Filter();
        qdrant.Must.Add(MatchKeyword(KeyProject, filter.Project));
        AddAny(qdrant, KeyItemType, filter.ItemTypes);
        AddAny(qdrant, KeyState, filter.States);
        AddAny(qdrant, KeyDecisionType, filter.DecisionTypes);
        AddAny(qdrant, KeyDocKind, filter.DocKinds);
        return qdrant;
    }

    private static void AddAny<T>(Filter filter, string field, IReadOnlyList<T>? values) where T : struct, Enum
    {
        if (values is { Count: > 0 })
        {
            filter.Must.Add(MatchAny(field, values.Select(v => v.ToString()!)));
        }
    }

    private static Condition MatchKeyword(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };

    private static Condition MatchAny(string field, IEnumerable<string> values)
    {
        var keywords = new RepeatedStrings();
        keywords.Strings.Add(values);
        return new Condition { Field = new FieldCondition { Key = field, Match = new Match { Keywords = keywords } } };
    }

    private static bool TryParseEnum<TEnum>(string value, out TEnum result) where TEnum : struct, Enum =>
        Enum.TryParse(value, out result) && Enum.IsDefined(result);

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct, Enum =>
        TryParseEnum<TEnum>(value, out var parsed) ? parsed : fallback;
}
