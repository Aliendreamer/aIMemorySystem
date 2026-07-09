using AiMemory.Core;

namespace AiMemory.Tests;

public class StateNormalizerTests
{
    [Theory]
    [InlineData("open", null, RecordState.Open)]
    [InlineData("OPEN", null, RecordState.Open)]                     // GraphQL upper-case
    [InlineData("Reopened", null, RecordState.Open)]
    [InlineData("closed", null, RecordState.Closed)]
    [InlineData("MERGED", null, RecordState.Closed)]                 // merged PR (was Unknown)
    [InlineData("closed", "completed", RecordState.Closed)]
    [InlineData("closed", "duplicate", RecordState.Closed)]
    [InlineData("closed", "not_planned", RecordState.Declined)]      // declined lives in reason
    [InlineData("CLOSED", "NOT_PLANNED", RecordState.Declined)]      // both upper-case
    public void Normalize_GitHub_UsesStateAndReason(string raw, string? reason, RecordState expected)
    {
        Assert.Equal(expected, StateNormalizer.Normalize(SourceKind.GitHub, raw, reason));
    }

    [Theory]
    [InlineData("New", RecordState.Open)]
    [InlineData("Active", RecordState.InProgress)]
    [InlineData("In Progress", RecordState.InProgress)]
    [InlineData("Resolved", RecordState.Resolved)]
    [InlineData("Closed", RecordState.Closed)]
    [InlineData("Removed", RecordState.Removed)]
    public void Normalize_AzureDevOps_MapsKnownStates(string raw, RecordState expected)
    {
        Assert.Equal(expected, StateNormalizer.Normalize(SourceKind.AzureDevOps, raw));
    }

    [Theory]
    [InlineData(SourceKind.GitHub, null)]
    [InlineData(SourceKind.GitHub, "")]
    [InlineData(SourceKind.GitHub, "   ")]
    [InlineData(SourceKind.GitHub, "something-odd")]
    [InlineData(SourceKind.AzureDevOps, "mystery-state")]
    public void Normalize_UnknownOrEmpty_ReturnsUnknown(SourceKind source, string? raw)
    {
        Assert.Equal(RecordState.Unknown, StateNormalizer.Normalize(source, raw));
    }

    [Fact]
    public void Normalize_IsCaseAndWhitespaceInsensitive()
    {
        Assert.Equal(RecordState.Removed, StateNormalizer.Normalize(SourceKind.AzureDevOps, "  ReMoVeD  "));
    }

    [Theory]
    [InlineData(RecordState.Declined, true)]
    [InlineData(RecordState.Removed, true)]
    [InlineData(RecordState.Closed, false)]
    [InlineData(RecordState.Open, false)]
    public void IsDeclined_GroupsDeclinedAndRemoved(RecordState state, bool expected)
    {
        Assert.Equal(expected, StateNormalizer.IsDeclined(state));
    }
}
