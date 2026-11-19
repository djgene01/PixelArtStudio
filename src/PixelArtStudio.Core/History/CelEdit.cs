using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

/// <summary>Before/after copies of the one cel a stroke touched.</summary>
public sealed class CelEdit(string name, int frameIndex, int layerIndex, PixelBuffer before, PixelBuffer after) : IUndoEntry
{
    public string Name { get; } = name;
    public int FrameIndex { get; } = frameIndex;
    public int LayerIndex { get; } = layerIndex;

    public void Undo(SpriteDocument doc) => doc.GetCel(FrameIndex, LayerIndex).CopyFrom(before);

    public void Redo(SpriteDocument doc) => doc.GetCel(FrameIndex, LayerIndex).CopyFrom(after);
}
