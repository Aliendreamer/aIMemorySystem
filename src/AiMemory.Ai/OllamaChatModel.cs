using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiMemory.Core;

namespace AiMemory.Ai;

/// <summary>
/// Self-hosted chat model backed by Ollama's <c>/api/chat</c>. Runs response-only
/// (<c>think:false</c>) and, for extraction, constrains output to the JSON schema
/// via Ollama's structured-output <c>format</c> field. The <see cref="HttpClient"/>
/// is injected (its <c>BaseAddress</c> points at the Ollama endpoint) and owned by the caller.
/// </summary>
public sealed class OllamaChatModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaChatModel(HttpClient http, string model)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _http = http;
        _model = model;
    }

    public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct = default) =>
        PostChatAsync(BuildRequest(systemPrompt, userPrompt, ParseSchema(jsonSchema)), ct);

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        PostChatAsync(BuildRequest(systemPrompt, userPrompt, format: null), ct);

    private Dictionary<string, object?> BuildRequest(string systemPrompt, string userPrompt, object? format)
    {
        var request = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["stream"] = false,
            ["think"] = false,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        if (format is not null)
        {
            request["format"] = format;
        }

        return request;
    }

    // Ollama accepts a JSON schema object (structured outputs) or the string "json".
    // Fall back to "json" if the schema is not valid JSON.
    private static object ParseSchema(string jsonSchema)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(jsonSchema);
        }
        catch (JsonException)
        {
            return "json";
        }
    }

    private async Task<string> PostChatAsync(object request, CancellationToken ct)
    {
        // Transport failures (non-2xx / timeout) throw by design — an unreachable or
        // erroring model server is an infra problem for the caller/orchestrator to handle
        // (retry/skip), distinct from a malformed generation which callers validate.
        using var response = await _http.PostAsJsonAsync("api/chat", request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct).ConfigureAwait(false);
        return body?.Message?.Content ?? string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}
