using AiMemory.Connectors;
using AiMemory.Core;

namespace AiMemory.Tests;

public sealed class RepoKnowledgeScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, "repotest-" + Guid.NewGuid().ToString("N"));

    public RepoKnowledgeScannerTests()
    {
        Write(".serena/memories/decisions.md", "serena memory");
        Write("CLAUDE.md", "agent instructions");
        Write(".github/copilot-instructions.md", "copilot");
        Write("openspec/changes/x/proposal.md", "why");
        Write("openspec/changes/x/design.md", "how");
        Write("openspec/changes/x/tasks.md", "tasks");
        Write("openspec/changes/x/specs/cap/spec.md", "delta spec");
        Write("openspec/specs/cap/spec.md", "main spec");
        Write("docs/adr/0001-choice.md", "decision record");
        Write("README.md", "readme");
        Write("docs/guide.md", "a doc");
        Write("tools/claude.md", "nested, lower-case agent instructions");
        // Must be skipped:
        Write("src/Program.cs", "code, not markdown");
        Write("bin/leftover.md", "in an excluded dir");
        Write("openspec/changes/x/.openspec.yaml", "config, not an artifact");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string rel, string content)
    {
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private Dictionary<string, MemoryRecord> ScanByPath()
    {
        var scanner = new RepoKnowledgeScanner();
        return scanner.Scan(_root, "Payments", SourceKind.GitHub)
            .ToDictionary(r => r.SourceId, r => r);
    }

    [Theory]
    [InlineData(".serena/memories/decisions.md", DocKind.SerenaMemory)]
    [InlineData("CLAUDE.md", DocKind.AgentInstructions)]
    [InlineData(".github/copilot-instructions.md", DocKind.AgentInstructions)]
    [InlineData("openspec/changes/x/proposal.md", DocKind.OpenSpecProposal)]
    [InlineData("openspec/changes/x/design.md", DocKind.OpenSpecDesign)]
    [InlineData("openspec/changes/x/tasks.md", DocKind.OpenSpecTasks)]
    [InlineData("openspec/changes/x/specs/cap/spec.md", DocKind.OpenSpecSpec)]
    [InlineData("openspec/specs/cap/spec.md", DocKind.OpenSpecSpec)]
    [InlineData("docs/adr/0001-choice.md", DocKind.Adr)]
    [InlineData("README.md", DocKind.Readme)]
    [InlineData("docs/guide.md", DocKind.Doc)]
    [InlineData("tools/claude.md", DocKind.AgentInstructions)]   // nested + lower-case
    public void Scan_ClassifiesArtifacts(string rel, DocKind expected)
    {
        var byPath = ScanByPath();

        Assert.True(byPath.ContainsKey(rel), $"expected {rel} to be ingested");
        var record = byPath[rel];
        Assert.Equal(ItemType.RepoKnowledge, record.ItemType);
        Assert.Equal(expected, record.DocKind);
        Assert.Equal("Payments", record.Project);
        Assert.Equal(SourceKind.GitHub, record.Source);
        Assert.False(string.IsNullOrEmpty(record.Text));
    }

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData("bin/leftover.md")]
    [InlineData("openspec/changes/x/.openspec.yaml")]
    public void Scan_SkipsNonArtifacts(string rel)
    {
        Assert.False(ScanByPath().ContainsKey(rel), $"expected {rel} to be skipped");
    }
}
