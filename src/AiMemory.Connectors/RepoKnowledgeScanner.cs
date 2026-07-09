using AiMemory.Core;

namespace AiMemory.Connectors;

/// <summary>
/// Scans a cloned repository for in-repo knowledge artifacts — Serena memories,
/// agent-instruction files, OpenSpec proposals/design/specs/tasks, ADRs, READMEs
/// and docs — and emits them as <see cref="ItemType.RepoKnowledge"/> records
/// tagged with the matching <see cref="DocKind"/>. New kinds slot in via
/// <see cref="Classify"/> without changing the record shape.
/// </summary>
public sealed class RepoKnowledgeScanner
{
    private static readonly string[] DefaultExcludedDirs = [".git", "bin", "obj", "node_modules"];

    private readonly HashSet<string> _excludedDirs;

    public RepoKnowledgeScanner(IEnumerable<string>? excludedDirs = null) =>
        _excludedDirs = new HashSet<string>(excludedDirs ?? DefaultExcludedDirs, StringComparer.Ordinal);

    public IEnumerable<MemoryRecord> Scan(string repoRoot, string project, SourceKind source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        var root = Path.GetFullPath(repoRoot);

        foreach (var full in EnumerateCandidateFiles(root))
        {
            var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
            var kind = Classify(rel);
            if (kind is null)
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // skip a single unreadable file rather than aborting the whole scan
            }

            yield return new MemoryRecord
            {
                Id = $"{project}:{rel}",
                Project = project,
                Source = source,
                SourceId = rel,
                ItemType = ItemType.RepoKnowledge,
                DocKind = kind.Value,
                Title = Path.GetFileName(rel),
                Text = text,
            };
        }
    }

    // Manual walk that prunes excluded directories during traversal (so huge trees
    // like .git / node_modules are never enumerated) and skips directory symlinks to
    // avoid cycles. Unreadable directories are skipped rather than aborting the scan.
    private IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subdirs;
            string[] files;
            try
            {
                subdirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                if (!_excludedDirs.Contains(Path.GetFileName(sub)) && !IsSymlink(sub))
                {
                    stack.Push(sub);
                }
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // if we can't tell, don't recurse into it
        }
    }

    /// <summary>Classifies a repo-relative path, or returns null if it is not a knowledge artifact.</summary>
    internal static DocKind? Classify(string rel)
    {
        var lower = rel.ToLowerInvariant();
        var name = Path.GetFileName(lower);

        var isMarkdown = lower.EndsWith(".md", StringComparison.Ordinal);
        var isCursorRule = lower.StartsWith(".cursor/rules/", StringComparison.Ordinal);
        if (!isMarkdown && !isCursorRule)
        {
            return null; // every artifact kind we ingest today is markdown or a cursor rule
        }

        if (lower.StartsWith(".serena/memories/", StringComparison.Ordinal))
        {
            return DocKind.SerenaMemory;
        }

        if (name is "claude.md" or "agents.md" or "gemini.md" ||
            lower == ".github/copilot-instructions.md" ||
            isCursorRule)
        {
            return DocKind.AgentInstructions;
        }

        if (lower.StartsWith("openspec/", StringComparison.Ordinal))
        {
            return ClassifyOpenSpec(lower, name);
        }

        if (lower.Contains("/adr/", StringComparison.Ordinal) ||
            lower.StartsWith("adr/", StringComparison.Ordinal) ||
            lower.Contains("/decisions/", StringComparison.Ordinal))
        {
            return DocKind.Adr;
        }

        if (name.StartsWith("readme", StringComparison.Ordinal))
        {
            return DocKind.Readme;
        }

        if (lower.StartsWith("docs/", StringComparison.Ordinal))
        {
            return DocKind.Doc;
        }

        return null;
    }

    private static DocKind? ClassifyOpenSpec(string lower, string name)
    {
        if (lower.StartsWith("openspec/specs/", StringComparison.Ordinal))
        {
            return DocKind.OpenSpecSpec;
        }

        if (lower.StartsWith("openspec/changes/", StringComparison.Ordinal))
        {
            return name switch
            {
                "proposal.md" => DocKind.OpenSpecProposal,
                "design.md" => DocKind.OpenSpecDesign,
                "tasks.md" => DocKind.OpenSpecTasks,
                _ when lower.Contains("/specs/", StringComparison.Ordinal) => DocKind.OpenSpecSpec,
                _ => null,
            };
        }

        return null;
    }
}
