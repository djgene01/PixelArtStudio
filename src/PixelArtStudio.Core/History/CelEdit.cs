using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

/// <summary>
/// Before/after copies of the part of one cel a stroke changed. Only the bounding rectangle of the changed pixels
/// is stored, so <paramref name="after"/> may be the live cel.
/// </summary>
public sealed class CelEdit : IUndoEntry
{
    private readonly PixelBuffer? _before;
    private readonly PixelBuffer? _after;
    private readonly int _celWidth, _celHeight;

    public CelEdit(string name, int frameIndex, int layerIndex, PixelBuffer before, PixelBuffer after)
    {
        Name = name;
        FrameIndex = frameIndex;
        LayerIndex = layerIndex;
        (_celWidth, _celHeight) = (after.Width, after.Height);
        if (before.DiffBounds(after) is not { } bounds) return;
        Bounds = bounds;
        _before = Selection.Extract(before, bounds);
        _after = Selection.Extract(after, bounds);
    }

    public string Name { get; }
    public int FrameIndex { get; }
    public int LayerIndex { get; }

    /// <summary>The changed rectangle of the cel; empty when nothing changed.</summary>
    public RectI Bounds { get; }

    /// <summary>True when before and after were equal; such an entry should not be pushed.</summary>
    public bool IsEmpty => _before == null;

    public long SizeBytes => 2L * Bounds.Width * Bounds.Height * sizeof(uint) + 64;

    public void Undo(SpriteDocument doc) => Apply(doc, _before);

    public void Redo(SpriteDocument doc) => Apply(doc, _after);

    private void Apply(SpriteDocument doc, PixelBuffer? pixels)
    {
        // Skip if the layout changed under the entry (it should not, but never write out of range).
        if (pixels == null || FrameIndex >= doc.Frames.Count || LayerIndex >= doc.Layers.Count) return;
        var cel = doc.GetCel(FrameIndex, LayerIndex);
        if (cel.Width != _celWidth || cel.Height != _celHeight) return;
        Selection.Paste(cel, pixels, Bounds.X, Bounds.Y, skipTransparent: false);
    }
}
