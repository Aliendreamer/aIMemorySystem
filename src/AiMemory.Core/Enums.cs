namespace AiMemory.Core;

/// <summary>The source system a record was ingested from.</summary>
public enum SourceKind
{
    AzureDevOps,
    GitHub,
}

/// <summary>
/// The kind of item a record represents. In-repo documents (ADRs, READMEs,
/// generic docs) are always <see cref="RepoKnowledge"/> distinguished by
/// <see cref="DocKind"/>, so there is one canonical encoding per concept.
/// </summary>
public enum ItemType
{
    WorkItem,
    Issue,
    PullRequest,
    Commit,
    Comment,
    RepoKnowledge,
    Attachment,
}

/// <summary>
/// Sub-classification for <see cref="ItemType.RepoKnowledge"/> records.
/// New values can be added without changing the storage schema.
/// </summary>
public enum DocKind
{
    None,
    SerenaMemory,
    AgentInstructions,
    OpenSpecProposal,
    OpenSpecDesign,
    OpenSpecSpec,
    OpenSpecTasks,
    Adr,
    Readme,
    Doc,
}

/// <summary>Normalized, source-agnostic lifecycle state vocabulary.</summary>
public enum RecordState
{
    Unknown,
    Open,
    InProgress,
    Resolved,
    Closed,
    Declined,
    Removed,
}

/// <summary>The classification of an extracted decision record.</summary>
public enum DecisionType
{
    Declined,
    Constraint,
    Chosen,
}
