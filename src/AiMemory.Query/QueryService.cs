using System.Text;
using AiMemory.Core;

namespace AiMemory.Query;

/// <summary>The two per-project questions v1 answers.</summary>
public enum QuestionKind
{
    Declined,
    Limitations,
}

/// <summary>A source the answer is grounded in.</summary>
public sealed record Citation(string SourceId, string? Title, string? Url);

/// <summary>An answer with its grounding. <see cref="HasEvidence"/> is false when nothing was retrieved.</summary>
public sealed record QueryAnswer(string Summary, IReadOnlyList<Citation> Citations, bool HasEvidence);

/// <summary>
/// Answers a per-project question by embedding it, retrieving matching records with
/// a payload filter, and summarizing them with a self-hosted model — grounded in
/// citations. When nothing is retrieved it says so rather than fabricating an answer.
/// </summary>
public sealed class QueryService
{
    private const int RetrievalLimit = 12;
    private const int MaxEvidenceCharsPerChunk = 2000;

    private const string SystemPrompt =
        "Answer the question using ONLY the provided project evidence. " +
        "Cite what the evidence supports and do not invent facts. Be concise.";

    private readonly IEmbedder _embedder;
    private readonly IVectorStore _store;
    private readonly IChatModel _chatModel;

    public QueryService(IEmbedder embedder, IVectorStore store, IChatModel chatModel)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(chatModel);
        _embedder = embedder;
        _store = store;
        _chatModel = chatModel;
    }

    public async Task<QueryAnswer> AnswerAsync(string project, QuestionKind kind, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var question = QuestionText(kind);
        var queryVector = await _embedder.EmbedAsync(question, ct);

        // Nullable local: defend against an adapter that violates the non-null contract.
        IReadOnlyList<RetrievedChunk>? hits =
            await _store.SearchAsync(queryVector, BuildFilter(project, kind), RetrievalLimit, ct);

        if (hits is null || hits.Count == 0)
        {
            return new QueryAnswer(
                $"No supporting evidence found for \"{question}\" in project {project}.",
                [],
                HasEvidence: false);
        }

        string? summary = await _chatModel.CompleteAsync(SystemPrompt, BuildUserPrompt(question, hits), ct);
        return new QueryAnswer(summary ?? string.Empty, BuildCitations(hits), HasEvidence: true);
    }

    private static string QuestionText(QuestionKind kind) => kind switch
    {
        QuestionKind.Declined => "What was declined and why?",
        QuestionKind.Limitations => "What are the technical limitations?",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // NOTE (v2 recall): filtering by DecisionType alone misses records whose decision
    // extraction never ran. A semantic-only fallback (or an "unclassified" pass) would
    // widen recall for the declined/limitations questions.
    private static RetrievalFilter BuildFilter(string project, QuestionKind kind) => kind switch
    {
        QuestionKind.Declined => new RetrievalFilter
        {
            Project = project,
            DecisionTypes = [DecisionType.Declined],
        },
        QuestionKind.Limitations => new RetrievalFilter
        {
            Project = project,
            DecisionTypes = [DecisionType.Constraint],
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // NOTE (v2 security): evidence text is untrusted source content; a body that says
    // "ignore previous instructions" is a prompt-injection vector. Harden by delimiting
    // or channeling evidence as data the model cannot treat as instructions.
    private static string BuildUserPrompt(string question, IReadOnlyList<RetrievedChunk> hits)
    {
        var sb = new StringBuilder();
        sb.Append("Question: ").AppendLine(question);
        sb.AppendLine("Evidence:");
        foreach (var hit in hits)
        {
            var text = hit.Record.Text;
            if (text.Length > MaxEvidenceCharsPerChunk)
            {
                text = string.Concat(text.AsSpan(0, MaxEvidenceCharsPerChunk), "…");
            }

            sb.Append("- [").Append(hit.Record.SourceId).Append("] ").AppendLine(text);
        }

        return sb.ToString();
    }

    private static Citation[] BuildCitations(IReadOnlyList<RetrievedChunk> hits) =>
        hits
            .Select(h => new Citation(h.Record.SourceId, h.Record.Title, h.Record.Url))
            .DistinctBy(c => c.SourceId)
            .ToArray();
}
