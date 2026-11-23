using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tools;

/// <summary>Freehand drawing. Mouse samples are joined with lines so fast strokes have no gaps.</summary>
public class PencilTool : ITool
{
    private PixelBuffer? _before;
    private int _lastX, _lastY;
    private uint _color;

    public virtual ToolKind Kind => ToolKind.Pencil;
    public virtual string Name => "Pencil";
    public bool Edits => true;

    protected virtual uint ColorFor(IToolContext ctx, PointerButton button) => ctx.ColorFor(button);

    public void OnDown(IToolContext ctx, ToolInput input)
    {
        _before = ctx.ActiveCel.Clone();
        _color = ColorFor(ctx, input.Button);
        (_lastX, _lastY) = (input.X, input.Y);
        Raster.Plot(ctx.ActiveCel, input.X, input.Y, _color, ctx.PaintOptions);
        ctx.Invalidate(DirtyRect(ctx, input.X, input.Y, input.X, input.Y));
    }

    public void OnMove(IToolContext ctx, ToolInput input)
    {
        if (_before == null || (input.X == _lastX && input.Y == _lastY)) return;
        Raster.Line(ctx.ActiveCel, _lastX, _lastY, input.X, input.Y, _color, ctx.PaintOptions);
        ctx.Invalidate(DirtyRect(ctx, _lastX, _lastY, input.X, input.Y));
        (_lastX, _lastY) = (input.X, input.Y);
    }

    public void OnUp(IToolContext ctx, ToolInput input) => End(ctx);

    public void End(IToolContext ctx)
    {
        if (_before == null) return;
        ctx.Commit(Name, _before);
        _before = null;
    }

    private static RectI DirtyRect(IToolContext ctx, int x0, int y0, int x1, int y1)
    {
        var options = ctx.PaintOptions;
        var cel = ctx.ActiveCel;
        return DirtyRegion.WithMirrors(RectI.FromPoints(x0, y0, x1, y1).Inflate(options.BrushSize), options, cel.Width, cel.Height);
    }
}

public sealed class EraserTool : PencilTool
{
    public override ToolKind Kind => ToolKind.Eraser;
    public override string Name => "Eraser";
    protected override uint ColorFor(IToolContext ctx, PointerButton button) => ColorUtil.Transparent;
}
