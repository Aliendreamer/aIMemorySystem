using System.Text.Json;
using System.Text.Json.Serialization;
using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>
/// Extracts a structured <see cref="DecisionInfo"/> from a record's text using a
/// self-hosted chat model constrained to <see cref="Schema"/>. The model's JSON is
/// defensively validated here — malformed output, an unknown decision type, or a
/// missing rationale are dropped (never persisted) rather than trusted.
/// </summary>
public sealed class DecisionExtractor : IDecisionExtractor
{
    internal const string Schema = """
        {
          "type": "object",
          "properties": {
            "hasDecision": { "type": "boolean" },
            "type": { "type": "string", "enum": ["declined", "constraint", "chosen"] },
            "rationale": { "type": "string" },
            "alternativesRejected": { "type": "array", "items": { "type": "string" } },
            "declines": { "type": "array", "items": { "type": "string" } },
            "supersedes": { "type": "array", "items": { "type": "string" } },
            "causedBy": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["hasDecision"]
        }
        """;

    private const string SystemPrompt =
        "You extract a single decision from a software project artifact. " +
        "If the text records work that was declined, a technical constraint/limitation, or a chosen approach with rationale, " +
        "return it. If there is no decision, set hasDecision to false. " +
        "Respond with JSON only, matching the schema. Do not explain.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IChatModel _chatModel;

    public DecisionExtractor(IChatModel chatModel)
    {
        ArgumentNullException.ThrowIfNull(chatModel);
        _chatModel = chatModel;
    }

    public async Task<DecisionInfo?> ExtractAsync(MemoryRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Text))
        {
            return null; // nothing to extract; don't spend a model call
        }

        var json = await _chatModel.CompleteJsonAsync(SystemPrompt, BuildUserPrompt(record), Schema, ct);
        return Parse(json);
    }

    private static string BuildUserPrompt(MemoryRecord record) =>
        $"Item type: {record.ItemType}\nTitle: {record.Title}\nText:\n{record.Text}";

    private static DecisionInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null; // null/empty generation → drop
        }

        ExtractionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExtractionDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // malformed output → drop
        }

        if (dto is null || !dto.HasDecision)
        {
            return null;
        }

        var type = ParseType(dto.Type);
        if (type is null || string.IsNullOrWhiteSpace(dto.Rationale))
        {
            return null; // unknown decision type or missing rationale → drop
        }

        return new DecisionInfo
        {
            Type = type.Value,
            Rationale = dto.Rationale.Trim(),
            AlternativesRejected = Clean(dto.AlternativesRejected),
            Declines = Clean(dto.Declines),
            Supersedes = Clean(dto.Supersedes),
            CausedBy = Clean(dto.CausedBy),
        };
    }

    // Accept only the three exact schema literals — not the numeric or comma-combined
    // forms that Enum.TryParse would otherwise admit (e.g. "1", "declined,chosen").
    private static DecisionType? ParseType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "declined" => DecisionType.Declined,
        "constraint" => DecisionType.Constraint,
        "chosen" => DecisionType.Chosen,
        _ => null,
    };

    private static string[] Clean(IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();

    private sealed class ExtractionDto
    {
        [JsonPropertyName("hasDecision")] public bool HasDecision { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("rationale")] public string? Rationale { get; init; }
        [JsonPropertyName("alternativesRejected")] public string[]? AlternativesRejected { get; init; }
        [JsonPropertyName("declines")] public string[]? Declines { get; init; }
        [JsonPropertyName("supersedes")] public string[]? Supersedes { get; init; }
        [JsonPropertyName("causedBy")] public string[]? CausedBy { get; init; }
    }
}
