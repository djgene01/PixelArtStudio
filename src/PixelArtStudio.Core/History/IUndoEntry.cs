using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

public interface IUndoEntry
{
    string Name { get; }

    /// <summary>Approximate memory held by the entry, for the undo stack's byte budget.</summary>
    long SizeBytes { get; }

    void Undo(SpriteDocument doc);
    void Redo(SpriteDocument doc);
}

/// <summary>An entry that can absorb the next one, e.g. consecutive opacity slider changes.</summary>
public interface IMergeableUndoEntry : IUndoEntry
{
    bool TryMerge(IUndoEntry next);
}
