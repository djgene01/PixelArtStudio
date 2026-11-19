using PixelArtStudio.Core.Drawing;

namespace PixelArtStudio.Tools;

public sealed class FillTool : ITool
{
    public ToolKind Kind => ToolKind.Fill;
    public string Name => "Fill";
    public bool Edits => true;

    public void OnDown(IToolContext ctx, ToolInput input)
    {
        var before = ctx.ActiveCel.Clone();
        if (!Raster.FloodFill(ctx.ActiveCel, input.X, input.Y, ctx.ColorFor(input.Button), ctx.FillContiguous, ctx.PaintOptions))
            return;
        ctx.Invalidate(null);
        ctx.Commit(Name, before);
    }

    public void OnMove(IToolContext ctx, ToolInput input) { }
    public void OnUp(IToolContext ctx, ToolInput input) { }
    public void End(IToolContext ctx) { }
}

public sealed class EyedropperTool : ITool
{
    public ToolKind Kind => ToolKind.Eyedropper;
    public string Name => "Eyedropper";
    public bool Edits => false;

    public void OnDown(IToolContext ctx, ToolInput input) => Pick(ctx, input);
    public void OnMove(IToolContext ctx, ToolInput input) => Pick(ctx, input);
    public void OnUp(IToolContext ctx, ToolInput input) { }
    public void End(IToolContext ctx) { }

    private static void Pick(IToolContext ctx, ToolInput input)
    {
        if (ctx.ActiveCel.Contains(input.X, input.Y))
            ctx.PickColor(ctx.SampleComposite(input.X, input.Y), input.Button);
    }
}
