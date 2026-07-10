using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>
/// Outcome of an ingestion run. <see cref="RecordsFailed"/> counts records that failed
/// during processing; <see cref="ChunksDropped"/> counts embedded chunks that failed to
/// persist; <see cref="ChunksSkipped"/> counts unchanged chunks that were not re-embedded.
/// </summary>
public sealed record IngestionResult(int ChunksStored, int RecordsFailed, int ChunksDropped, int ChunksSkipped);

/// <summary>
/// Drives the per-record pipeline: extract a decision from the whole record, attach it,
/// chunk, embed each chunk, and upsert in batches. A single record's failure (e.g. a
/// transient model/transport error) is isolated and counted — it never aborts the run.
/// </summary>
public sealed class IngestionOrchestrator
{
    private const int BatchSize = 64;

    private readonly IChunker _chunker;
    private readonly IDecisionExtractor _extractor;
    private readonly IEmbedder _embedder;
    private readonly IVectorStore _store;

    public IngestionOrchestrator(IChunker chunker, IDecisionExtractor extractor, IEmbedder embedder, IVectorStore store)
    {
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(store);
        _chunker = chunker;
        _extractor = extractor;
        _embedder = embedder;
        _store = store;
    }

    public async Task<IngestionResult> IngestAsync(IAsyncEnumerable<MemoryRecord> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        await _store.EnsureInitializedAsync(ct);

        var batch = new List<EmbeddedRecord>(BatchSize);
        var stored = 0;
        var recordsFailed = 0;
        var dropped = 0;
        var skipped = 0;

        await foreach (var record in records.WithCancellation(ct))
        {
            List<EmbeddedRecord> processed;
            int recordSkipped;
            try
            {
                // Build the record's changed chunks fully before touching the shared batch,
                // so a mid-record failure stores nothing partial for that record.
                (processed, recordSkipped) = await ProcessRecordAsync(record, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                recordsFailed++; // isolate a single bad record; keep ingesting the rest
                continue;
            }

            skipped += recordSkipped;
            batch.AddRange(processed);
            while (batch.Count >= BatchSize)
            {
                (stored, dropped) = await FlushAsync(TakeBatch(batch), stored, dropped, ct);
            }
        }

        if (batch.Count > 0)
        {
            (stored, dropped) = await FlushAsync(batch, stored, dropped, ct);
        }

        return new IngestionResult(stored, recordsFailed, dropped, skipped);
    }

    // Removes and returns up to BatchSize items from the front, so a single high-chunk
    // record never produces an oversized upsert.
    private static List<EmbeddedRecord> TakeBatch(List<EmbeddedRecord> batch)
    {
        var size = Math.Min(BatchSize, batch.Count);
        var slice = batch.GetRange(0, size);
        batch.RemoveRange(0, size);
        return slice;
    }

    // Flushes a slice, isolating store failures: a transient upsert error drops that slice
    // (counted) rather than aborting the whole run.
    private async Task<(int Stored, int Dropped)> FlushAsync(
        List<EmbeddedRecord> slice, int stored, int dropped, CancellationToken ct)
    {
        try
        {
            await _store.UpsertAsync(slice, ct);
            return (stored + slice.Count, dropped);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return (stored, dropped + slice.Count);
        }
    }

    private async Task<(List<EmbeddedRecord> Processed, int Skipped)> ProcessRecordAsync(MemoryRecord record, CancellationToken ct)
    {
        // Chunk first (cheap, no model call) and skip chunks whose text is unchanged since
        // last ingest. If the whole record is unchanged, skip the extraction call too.
        var chunks = _chunker.Chunk(record).ToList();
        var existing = await _store.GetExistingHashesAsync(chunks.Select(c => c.Id).ToArray(), ct);

        var changed = chunks
            .Where(c => !existing.TryGetValue(c.Id, out var hash) || hash != Hashing.ContentHash(c.Text))
            .ToList();
        var skipped = chunks.Count - changed.Count;

        if (changed.Count == 0)
        {
            return (new List<EmbeddedRecord>(), skipped);
        }

        // Extract on the whole record (a decision spans the document) and propagate to the
        // changed chunks, so declined/constraint records stay filterable after chunking.
        var decision = await _extractor.ExtractAsync(record, ct);

        var processed = new List<EmbeddedRecord>();
        foreach (var chunk in changed)
        {
            var enriched = decision is null ? chunk : chunk with { Decision = decision };
            var vector = await _embedder.EmbedAsync(EmbedText(enriched), ct);
            processed.Add(new EmbeddedRecord(enriched, vector));
        }

        return (processed, skipped);
    }

    private static string EmbedText(MemoryRecord record) =>
        string.IsNullOrEmpty(record.Title) ? record.Text : $"{record.Title}\n{record.Text}";
}
