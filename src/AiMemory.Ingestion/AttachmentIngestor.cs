using System.Security.Cryptography;
using System.Text;
using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>
/// v1 attachment handling: stream the binary to the blob store and emit an
/// <see cref="ItemType.Attachment"/> record linked to its parent via
/// <see cref="RecordLinks.AttachmentOf"/>. No text extraction, OCR, or transcription.
/// </summary>
public sealed class AttachmentIngestor
{
    private readonly IBlobStore _blobStore;

    public AttachmentIngestor(IBlobStore blobStore)
    {
        ArgumentNullException.ThrowIfNull(blobStore);
        _blobStore = blobStore;
    }

    public async Task<MemoryRecord> StoreAndLinkAsync(
        MemoryRecord parent,
        string fileName,
        string mime,
        Stream content,
        string? sourceUrl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mime);
        ArgumentNullException.ThrowIfNull(content);

        // The store streams and reports the byte count, so there is no need to
        // buffer the whole attachment in memory.
        var blob = await _blobStore.SaveAsync(BuildKey(parent, fileName), content, ct);

        return new MemoryRecord
        {
            Id = $"{parent.Id}#att:{fileName}",
            Project = parent.Project,
            Source = parent.Source,
            SourceId = sourceUrl ?? $"{parent.SourceId}#att:{fileName}",
            Url = sourceUrl,
            ItemType = ItemType.Attachment,
            Title = fileName,
            Text = string.Empty,
            State = parent.State,
            Links = new RecordLinks { AttachmentOf = parent.Id },
            Attachment = new AttachmentInfo
            {
                Mime = mime,
                Size = blob.Size,
                SourceUrl = sourceUrl,
                VolumePath = blob.VolumePath,
            },
        };
    }

    // Builds a collision-resistant blob key: one safe segment per part plus a hash
    // of the raw file name, so distinct names can never map to the same path.
    // NOTE: this is filesystem-oriented; if a non-filesystem IBlobStore backend
    // (e.g. S3/MinIO with different key rules) is added, lift this into a shared
    // key scheme. Two attachments sharing a parent AND file name still collide —
    // that identity concern belongs to the ingestion orchestrator (task 6.3).
    private static string BuildKey(MemoryRecord parent, string fileName) =>
        $"{SafeSegment(parent.Project)}/{SafeSegment(parent.Id)}/{ShortHash(fileName)}-{SafeSegment(fileName)}";

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c)
            .ToArray();
        var segment = new string(chars).Trim('.');
        return segment.Length == 0 ? "_" : segment;
    }

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
