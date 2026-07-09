using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiMemory.Core;

namespace AiMemory.Ai;

/// <summary>
/// Self-hosted embedder backed by Ollama's <c>/api/embed</c> (e.g. serving BGE-M3).
/// Returns the dense vector; Ollama does not expose sparse vectors, so hybrid search
/// would need a different server. The <see cref="HttpClient"/> is injected and caller-owned.
/// </summary>
public sealed class OllamaEmbedder : IEmbedder
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaEmbedder(HttpClient http, string model)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _http = http;
        _model = model;
    }

    public async Task<EmbeddingVector> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var request = new { model = _model, input = text };

        using var response = await _http.PostAsJsonAsync("api/embed", request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(ct).ConfigureAwait(false);

        // Guard both the outer array and a null inner array so Dense is never null.
        var dense = body?.Embeddings is { Length: > 0 } embeddings && embeddings[0] is { } first
            ? first
            : [];
        return new EmbeddingVector(dense);
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embeddings")] public float[][]? Embeddings { get; init; }
    }
}
