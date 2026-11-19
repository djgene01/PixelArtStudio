using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

/// <summary>Whole-document copies for structural changes (frames, layers, resize).</summary>
/// <param name="mergeKey">Consecutive snapshots with the same non-null key collapse into one entry.</param>
public sealed class DocumentSnapshot(string name, SpriteDocument before, SpriteDocument after, string? mergeKey = null)
    : IMergeableUndoEntry
{
    private SpriteDocument _after = after;

    public string Name { get; } = name;
    public string? MergeKey { get; } = mergeKey;

    public void Undo(SpriteDocument doc) => doc.CopyStateFrom(before);

    public void Redo(SpriteDocument doc) => doc.CopyStateFrom(_after);

    public bool TryMerge(IUndoEntry next)
    {
        if (MergeKey == null || next is not DocumentSnapshot snapshot || snapshot.MergeKey != MergeKey) return false;
        _after = snapshot._after;
        return true;
    }
}
