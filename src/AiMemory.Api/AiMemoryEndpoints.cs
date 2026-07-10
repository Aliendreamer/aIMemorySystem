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

    // Manual/triggered sync: the on-demand escape hatch over the same RepoIngestor
    // the scheduled BackgroundService uses.
    public static Task<IngestionResult> IngestRepoAsync(
        IngestRepoRequest request,
        RepoIngestor ingestor,
        CancellationToken ct) =>
        ingestor.IngestAsync(request.Project, request.RepoPath, request.Source, ct);
}
