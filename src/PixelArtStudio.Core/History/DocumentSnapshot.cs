using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

/// <summary>Whole-document copies for structural changes (frames, layers, resize).</summary>
/// <param name="mergeKey">Consecutive snapshots with the same non-null key collapse into one entry.</param>
public sealed class DocumentSnapshot(string name, SpriteDocument before, SpriteDocument after, string? mergeKey = null)
    : IMergeableUndoEntry
{
    private SpriteDocument _after = after;
    private long _sizeBytes = MeasureBytes(before, after);

    public string Name { get; } = name;
    public string? MergeKey { get; } = mergeKey;
    public long SizeBytes => _sizeBytes;

    /// <summary>
    /// A snapshot from a copy taken before the change and the live document after it. Cels that did not change
    /// (same content at the same frame and layer) share <paramref name="before"/>'s buffers instead of being copied
    /// again; this is safe because undo and redo restore deep copies.
    /// </summary>
    public static DocumentSnapshot Capture(string name, SpriteDocument before, SpriteDocument live, string? mergeKey = null)
    {
        var after = live.ShallowCopy();
        for (int f = 0; f < live.Frames.Count; f++)
        {
            var source = live.Frames[f];
            var frame = new Frame { DurationMs = source.DurationMs };
            for (int l = 0; l < source.Cels.Count; l++)
            {
                var cel = source.Cels[l];
                var old = f < before.Frames.Count && l < before.Frames[f].Cels.Count ? before.Frames[f].Cels[l] : null;
                frame.Cels.Add(old != null && old.ContentEquals(cel) ? old : cel.Clone());
            }
            after.Frames.Add(frame);
        }
        return new DocumentSnapshot(name, before, after, mergeKey);
    }

    public void Undo(SpriteDocument doc) => doc.CopyStateFrom(before);

    public void Redo(SpriteDocument doc) => doc.CopyStateFrom(_after);

    public bool TryMerge(IUndoEntry next)
    {
        if (MergeKey == null || next is not DocumentSnapshot snapshot || snapshot.MergeKey != MergeKey) return false;
        _after = snapshot._after;
        _sizeBytes = MeasureBytes(before, _after);
        return true;
    }

    /// <summary>Bytes of the distinct pixel buffers held by both documents.</summary>
    private static long MeasureBytes(SpriteDocument a, SpriteDocument b)
    {
        var seen = new HashSet<PixelBuffer>(ReferenceEqualityComparer.Instance);
        long bytes = 1024;
        foreach (var cel in a.Frames.Concat(b.Frames).SelectMany(f => f.Cels))
            if (seen.Add(cel)) bytes += (long)cel.Pixels.Length * sizeof(uint);
        return bytes;
    }
}
