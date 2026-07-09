using AiMemory.Connectors;
using AiMemory.Core;
using AiMemory.Ingestion;
using AiMemory.Query;

namespace AiMemory.Api;

/// <summary>Request to ingest the in-repo knowledge artifacts of a locally-cloned repository.</summary>
public sealed record IngestRepoRequest(string Project, string RepoPath, SourceKind Source);

public static class AiMemoryEndpoints
{
    public static void MapAiMemory(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects/{project}/declined", (string project, IQueryService query, CancellationToken ct) =>
            query.AnswerAsync(project, QuestionKind.Declined, ct));

        app.MapGet("/projects/{project}/limitations", (string project, IQueryService query, CancellationToken ct) =>
            query.AnswerAsync(project, QuestionKind.Limitations, ct));

        app.MapPost("/ingest/repo", IngestRepoAsync);
    }

    // Manual/triggered sync (v1): scan a locally-cloned repo's knowledge artifacts and
    // run them through the ingestion pipeline. Static + explicit deps so it is testable.
    public static Task<IngestionResult> IngestRepoAsync(
        IngestRepoRequest request,
        RepoKnowledgeScanner scanner,
        IngestionOrchestrator orchestrator,
        CancellationToken ct)
    {
        var records = scanner.Scan(request.RepoPath, request.Project, request.Source);
        return orchestrator.IngestAsync(ToAsyncEnumerable(records), ct);
    }

    private static async IAsyncEnumerable<MemoryRecord> ToAsyncEnumerable(IEnumerable<MemoryRecord> records)
    {
        foreach (var record in records)
        {
            yield return record;
        }

        await Task.CompletedTask;
    }
}
