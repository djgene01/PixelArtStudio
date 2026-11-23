using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

/// <summary>The non-pixel state of a document: palette, colour cycles, layer properties and frame durations.</summary>
public sealed class DocumentMeta
{
    private Palette _palette = new();
    private List<ColorCycle> _cycles = [];
    private List<(string Name, bool Visible, double Opacity)> _layers = [];
    private List<int> _durations = [];

    public static DocumentMeta Capture(SpriteDocument doc) => new()
    {
        _palette = doc.Palette.Clone(),
        _cycles = doc.Cycles.Select(c => c.Clone()).ToList(),
        _layers = doc.Layers.Select(l => (l.Name, l.Visible, l.Opacity)).ToList(),
        _durations = doc.Frames.Select(f => f.DurationMs).ToList(),
    };

    /// <summary>Restores the captured state. Layers and frames that no longer exist are skipped.</summary>
    public void Apply(SpriteDocument doc)
    {
        doc.Palette = _palette.Clone();
        doc.Cycles.Clear();
        doc.Cycles.AddRange(_cycles.Select(c => c.Clone()));
        for (int i = 0; i < Math.Min(_layers.Count, doc.Layers.Count); i++)
        {
            var layer = doc.Layers[i];
            (layer.Name, layer.Visible, layer.Opacity) = _layers[i];
        }
        for (int i = 0; i < Math.Min(_durations.Count, doc.Frames.Count); i++)
            doc.Frames[i].DurationMs = _durations[i];
    }

    internal long SizeBytes => 256 + _palette.Colors.Count * sizeof(uint) + _cycles.Count * 64 + _layers.Count * 64 + _durations.Count * sizeof(int);
}

/// <summary>An undo step for changes that touch no pixels, so no cels are copied.</summary>
/// <param name="mergeKey">Consecutive edits with the same non-null key collapse into one entry.</param>
public sealed class MetaEdit(string name, DocumentMeta before, DocumentMeta after, string? mergeKey = null) : IMergeableUndoEntry
{
    private DocumentMeta _after = after;

    public string Name { get; } = name;
    public string? MergeKey { get; } = mergeKey;
    public long SizeBytes => before.SizeBytes + _after.SizeBytes;

    public void Undo(SpriteDocument doc) => before.Apply(doc);

    public void Redo(SpriteDocument doc) => _after.Apply(doc);

    public bool TryMerge(IUndoEntry next)
    {
        if (MergeKey == null || next is not MetaEdit edit || edit.MergeKey != MergeKey) return false;
        _after = edit._after;
        return true;
    }
}
