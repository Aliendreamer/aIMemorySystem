using AiMemory.Core;

namespace AiMemory.Storage;

/// <summary>
/// Stores attachment binaries on a local filesystem volume. The returned volume
/// path is a root-relative key so records stay portable across mount points.
/// </summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private readonly string _root;

    public FileSystemBlobStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<BlobInfo> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (rel, full) = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return new BlobInfo(rel, fs.Length);
    }

    public Task<Stream> OpenAsync(string volumePath, CancellationToken ct = default)
    {
        var (_, full) = Resolve(volumePath);
        Stream stream = File.OpenRead(full);
        return Task.FromResult(stream);
    }

    private (string Rel, string Full) Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var segment in rel.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new ArgumentException($"Path '{relativePath}' has an empty or relative segment.", nameof(relativePath));
            }
        }

        var full = Path.GetFullPath(Path.Combine(_root, rel));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Path '{relativePath}' escapes the blob root.", nameof(relativePath));
        }

        return (rel, full);
    }
}
