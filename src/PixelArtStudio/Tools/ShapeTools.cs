using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tools;

/// <summary>
/// Drag-to-draw shape: previewed while dragging, committed on release. Shift constrains. Each preview update
/// only restores and redraws the area the shape covered, so it stays fast on big canvases.
/// </summary>
public abstract class ShapeTool : ITool
{
    private bool _active;
    private int _x0, _y0, _x1, _y1;
    private uint _color;
    private PixelBuffer? _preview;
    private RectI? _lastBounds;

    public abstract ToolKind Kind { get; }
    public abstract string Name { get; }
    public bool Edits => true;

    protected abstract void Draw(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, IToolContext ctx);

    protected virtual (int X, int Y) Constrain(int x0, int y0, int x1, int y1) => Raster.ConstrainSquare(x0, y0, x1, y1);

    public void OnDown(IToolContext ctx, ToolInput input)
    {
        _active = true;
        (_x0, _y0, _x1, _y1) = (input.X, input.Y, input.X, input.Y);
        _color = ctx.ColorFor(input.Button);

        var cel = ctx.ActiveCel;
        if (_preview == null || _preview.Width != cel.Width || _preview.Height != cel.Height)
            _preview = cel.Clone();
        else
            _preview.CopyFrom(cel);
        _lastBounds = null;
        UpdatePreview(ctx);
    }

    public void OnMove(IToolContext ctx, ToolInput input)
    {
        if (!_active) return;
        var end = input.Shift ? Constrain(_x0, _y0, input.X, input.Y) : (input.X, input.Y);
        if (end == (_x1, _y1)) return;
        (_x1, _y1) = end;
        UpdatePreview(ctx);
    }

    public void OnUp(IToolContext ctx, ToolInput input)
    {
        if (!_active) return;
        OnMove(ctx, input);
        _active = false;
        var before = ctx.ActiveCel.Clone();
        Draw(ctx.ActiveCel, _x0, _y0, _x1, _y1, _color, ctx);
        ctx.SetPreview(null, _lastBounds);
        ctx.Commit(Name, before);
    }

    public void End(IToolContext ctx)
    {
        if (!_active) return;
        _active = false;
        ctx.SetPreview(null, _lastBounds);
    }

    private void UpdatePreview(IToolContext ctx)
    {
        var cel = ctx.ActiveCel;
        if (_preview == null) return;
        if (_lastBounds is { } old) _preview.CopyFrom(cel, old);

        Draw(_preview, _x0, _y0, _x1, _y1, _color, ctx);
        var options = ctx.PaintOptions;
        var bounds = DirtyRegion.WithMirrors(RectI.FromPoints(_x0, _y0, _x1, _y1).Inflate(options.BrushSize), options, cel.Width, cel.Height);
        var dirty = _lastBounds is { } previous ? previous.Union(bounds) : bounds;
        _lastBounds = bounds;
        ctx.SetPreview(_preview, dirty);
    }
}

public sealed class LineTool : ShapeTool
{
    public override ToolKind Kind => ToolKind.Line;
    public override string Name => "Line";

    protected override void Draw(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, IToolContext ctx) =>
        Raster.Line(target, x0, y0, x1, y1, color, ctx.PaintOptions);

    protected override (int X, int Y) Constrain(int x0, int y0, int x1, int y1) => Raster.ConstrainLine(x0, y0, x1, y1);
}

public sealed class RectangleTool : ShapeTool
{
    public override ToolKind Kind => ToolKind.Rectangle;
    public override string Name => "Rectangle";

    protected override void Draw(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, IToolContext ctx) =>
        Raster.Rectangle(target, x0, y0, x1, y1, color, ctx.ShapeFilled, ctx.PaintOptions);
}

public sealed class EllipseTool : ShapeTool
{
    public override ToolKind Kind => ToolKind.Ellipse;
    public override string Name => "Ellipse";

    protected override void Draw(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, IToolContext ctx) =>
        Raster.Ellipse(target, x0, y0, x1, y1, color, ctx.ShapeFilled, ctx.PaintOptions);
}
