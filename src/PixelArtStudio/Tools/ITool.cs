using System.Windows.Input;
using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tools;

public enum ToolKind { Pencil, Eraser, Fill, Eyedropper, Line, Rectangle, Ellipse, Select }

public enum PointerButton { Left, Right }

public readonly record struct ToolInput(int X, int Y, PointerButton Button, ModifierKeys Modifiers)
{
    public bool Shift => Modifiers.HasFlag(ModifierKeys.Shift);
}

/// <summary>Pixels lifted out of a cel by the select tool (or pasted), not yet merged back.</summary>
public sealed class FloatingSelection(PixelBuffer pixels, int x, int y, int frameIndex, int layerIndex, PixelBuffer celBefore, string name)
{
    public PixelBuffer Pixels { get; } = pixels;
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int FrameIndex { get; } = frameIndex;
    public int LayerIndex { get; } = layerIndex;

    /// <summary>The cel as it was before lifting, for the undo entry made on commit.</summary>
    public PixelBuffer CelBefore { get; } = celBefore;

    public string Name { get; } = name;
    public RectI Bounds => new(X, Y, Pixels.Width, Pixels.Height);
}

/// <summary>What a tool can see and change. Implemented by the main view model.</summary>
public interface IToolContext
{
    SpriteDocument Document { get; }
    PixelBuffer ActiveCel { get; }
    PaintOptions PaintOptions { get; }
    bool FillContiguous { get; }
    bool ShapeFilled { get; }
    RectI? Selection { get; set; }
    FloatingSelection? Floating { get; }

    uint ColorFor(PointerButton button);
    void PickColor(uint color, PointerButton button);
    uint SampleComposite(int x, int y);

    /// <summary>Shows <paramref name="preview"/> in place of the active cel, or clears the preview when null.</summary>
    void SetPreview(PixelBuffer? preview);

    /// <summary>The active cel changed inside <paramref name="region"/> (null = anywhere).</summary>
    void Invalidate(RectI? region);

    /// <summary>Records an undo entry if the active cel differs from <paramref name="before"/>.</summary>
    void Commit(string name, PixelBuffer before);

    void LiftSelection();
    void MoveFloating(int dx, int dy);
    void CommitFloating();
}

public interface ITool
{
    ToolKind Kind { get; }
    string Name { get; }

    /// <summary>Whether the tool changes the active cel (and so needs a visible layer).</summary>
    bool Edits { get; }

    void OnDown(IToolContext ctx, ToolInput input);
    void OnMove(IToolContext ctx, ToolInput input);
    void OnUp(IToolContext ctx, ToolInput input);

    /// <summary>Ends any operation in progress: finished strokes are kept, unfinished previews are dropped.</summary>
    void End(IToolContext ctx);
}
