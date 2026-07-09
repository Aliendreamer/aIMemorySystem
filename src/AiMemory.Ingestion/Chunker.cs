using AiMemory.Core;

namespace AiMemory.Ingestion;

/// <summary>
/// Splits a record's text into overlapping character windows. Each chunk is a
/// <see cref="MemoryRecord"/> that keeps its parent's <see cref="MemoryRecord.SourceId"/>
/// (the parent link) and gets a derived, unique <see cref="MemoryRecord.Id"/>.
/// <para>
/// v1 sizing is by UTF-16 char count — a placeholder. Real embedders count tokens,
/// so once the embedding model is chosen this should become token-aware (and prefer
/// sentence/paragraph boundaries). Window boundaries are snapped off surrogate pairs
/// so astral characters are never split.
/// </para>
/// </summary>
public sealed class Chunker : IChunker
{
    private readonly int _maxChars;
    private readonly int _overlap;

    public Chunker(int maxChars = 1000, int overlap = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, maxChars);
        _maxChars = maxChars;
        _overlap = overlap;
    }

    public IEnumerable<MemoryRecord> Chunk(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var text = record.Text;
        if (text.Length <= _maxChars)
        {
            yield return record;
            yield break;
        }

        var step = _maxChars - _overlap;
        var index = 0;
        // `cursor` always advances by `step` so the loop cannot stall; `from` is a
        // local snap that never begins a chunk on a lone low surrogate.
        for (var cursor = 0; cursor < text.Length; cursor += step)
        {
            var from = cursor;
            if (from > 0 && char.IsLowSurrogate(text[from]))
            {
                from--;
            }

            var end = Math.Min(from + _maxChars, text.Length);
            // Never end a chunk between a surrogate pair.
            if (end < text.Length && char.IsLowSurrogate(text[end]))
            {
                end--;
            }

            yield return record with
            {
                Id = $"{record.Id}#c{index}",
                Text = text[from..end],
            };
            index++;
            if (end >= text.Length)
            {
                yield break;
            }
        }
    }
}
