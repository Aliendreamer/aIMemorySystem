using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>
/// Outcome of an ingestion run. <see cref="RecordsFailed"/> counts records that failed
/// during processing; <see cref="ChunksDropped"/> counts embedded chunks that failed to persist.
/// </summary>
public sealed record IngestionResult(int ChunksStored, int RecordsFailed, int ChunksDropped);

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

        await foreach (var record in records.WithCancellation(ct))
        {
            List<EmbeddedRecord> processed;
            try
            {
                // Build the record's chunks fully before touching the shared batch, so a
                // mid-record failure stores nothing partial for that record.
                processed = await ProcessRecordAsync(record, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                recordsFailed++; // isolate a single bad record; keep ingesting the rest
                continue;
            }

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

        return new IngestionResult(stored, recordsFailed, dropped);
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

    private async Task<List<EmbeddedRecord>> ProcessRecordAsync(MemoryRecord record, CancellationToken ct)
    {
        // Extract on the whole record (a decision spans the document) then propagate to
        // every chunk, so declined/constraint records stay filterable after chunking.
        var decision = await _extractor.ExtractAsync(record, ct);
        var enriched = decision is null ? record : record with { Decision = decision };

        var processed = new List<EmbeddedRecord>();
        foreach (var chunk in _chunker.Chunk(enriched))
        {
            var vector = await _embedder.EmbedAsync(EmbedText(chunk), ct);
            processed.Add(new EmbeddedRecord(chunk, vector));
        }

        return processed;
    }

    private static string EmbedText(MemoryRecord record) =>
        string.IsNullOrEmpty(record.Title) ? record.Text : $"{record.Title}\n{record.Text}";
}
