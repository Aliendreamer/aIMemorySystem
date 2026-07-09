namespace AiMemory.Core;

/// <summary>
/// Maps source-specific item states onto the normalized <see cref="RecordState"/>
/// vocabulary so records from different systems are queryable uniformly.
/// </summary>
public static class StateNormalizer
{
    /// <summary>
    /// Normalizes a source item's state. GitHub splits lifecycle across two fields —
    /// <paramref name="rawState"/> (<c>OPEN</c>/<c>CLOSED</c>, plus <c>MERGED</c> for PRs)
    /// and an issue's close reason (<c>COMPLETED</c>/<c>NOT_PLANNED</c>/<c>DUPLICATE</c>/…) —
    /// so <paramref name="rawReason"/> carries the reason where the source provides one.
    /// GraphQL enum values arrive upper-cased and are folded here.
    /// </summary>
    public static RecordState Normalize(SourceKind source, string? rawState, string? rawReason = null)
    {
        var state = rawState?.Trim().ToLowerInvariant();
        var reason = rawReason?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(state) && string.IsNullOrEmpty(reason))
        {
            return RecordState.Unknown;
        }

        return source switch
        {
            SourceKind.GitHub => NormalizeGitHub(state, reason),
            SourceKind.AzureDevOps => NormalizeAzureDevOps(state),
            _ => RecordState.Unknown,
        };
    }

    private static RecordState NormalizeGitHub(string? state, string? reason)
    {
        // "Declined" semantics live in the close reason, not the state.
        if (reason is "not_planned")
        {
            return RecordState.Declined;
        }

        return state switch
        {
            "open" or "reopened" => RecordState.Open,
            "closed" or "merged" or "completed" => RecordState.Closed,
            _ => reason is "reopened" ? RecordState.Open : RecordState.Unknown,
        };
    }

    private static RecordState NormalizeAzureDevOps(string? state) => state switch
    {
        "new" or "to do" or "proposed" or "approved" => RecordState.Open,
        "active" or "in progress" or "doing" or "committed" => RecordState.InProgress,
        "resolved" => RecordState.Resolved,
        "closed" or "done" => RecordState.Closed,
        "removed" => RecordState.Removed,
        _ => RecordState.Unknown,
    };

    /// <summary>Whether a state represents work that was declined or removed.</summary>
    public static bool IsDeclined(RecordState state) =>
        state is RecordState.Declined or RecordState.Removed;
}
