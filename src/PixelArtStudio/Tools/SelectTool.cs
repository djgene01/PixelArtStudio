using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tools;

/// <summary>Rectangular marquee. Dragging inside the selection lifts and moves its pixels.</summary>
public sealed class SelectTool : ITool
{
    private enum Mode { None, Marquee, Move }

    private Mode _mode;
    private int _startX, _startY, _lastX, _lastY;
    private bool _moved;

    public ToolKind Kind => ToolKind.Select;
    public string Name => "Select";
    public bool Edits => false;

    public void OnDown(IToolContext ctx, ToolInput input)
    {
        (_startX, _startY, _lastX, _lastY) = (input.X, input.Y, input.X, input.Y);
        _moved = false;

        if (input.Button == PointerButton.Left && ctx.Selection is { } selection && selection.Contains(input.X, input.Y))
        {
            if (ctx.Floating == null) ctx.LiftSelection();
            _mode = Mode.Move;
            return;
        }

        ctx.CommitFloating();
        _mode = Mode.Marquee;
        ctx.Selection = null;
    }

    public void OnMove(IToolContext ctx, ToolInput input)
    {
        if (input.X == _lastX && input.Y == _lastY) return;
        _moved = true;
        switch (_mode)
        {
            case Mode.Move:
                ctx.MoveFloating(input.X - _lastX, input.Y - _lastY);
                break;
            case Mode.Marquee:
                var (x, y) = input.Shift ? Raster.ConstrainSquare(_startX, _startY, input.X, input.Y) : (input.X, input.Y);
                var rect = RectI.FromPoints(_startX, _startY, x, y).Intersect(ctx.ActiveCel.Bounds);
                ctx.Selection = rect.IsEmpty ? null : rect;
                break;
        }
        (_lastX, _lastY) = (input.X, input.Y);
    }

    public void OnUp(IToolContext ctx, ToolInput input)
    {
        OnMove(ctx, input);
        // A click without dragging just clears the selection.
        if (_mode == Mode.Marquee && !_moved) ctx.Selection = null;
        _mode = Mode.None;
    }

    public void End(IToolContext ctx) => _mode = Mode.None;
}
