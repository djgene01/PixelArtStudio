using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PixelArtStudio.Core.Model;
using static PixelArtStudio.Mcp.Tools.ToolHelpers;

namespace PixelArtStudio.Mcp.Tools;

[McpServerToolType]
public sealed class CanvasTools(IStudioSession session)
{
    public const int MaxPaletteColors = 256;

    [McpServerTool(Name = "resize_canvas", Destructive = true)]
    [Description("Crops or pads every frame and layer to a new canvas size. Content stays at the anchor; new areas are transparent.")]
    public Task<string> ResizeCanvas(
        [Description("New width, 1-1024.")] int width,
        [Description("New height, 1-1024.")] int height,
        [Description("top_left, top, top_right, left, center, right, bottom_left, bottom or bottom_right.")] string anchor = "center")
    {
        CheckSize(width, height);
        var where = anchor.Trim().ToLowerInvariant() switch
        {
            "top_left" => Anchor.TopLeft,
            "top" => Anchor.Top,
            "top_right" => Anchor.TopRight,
            "left" => Anchor.Left,
            "center" or "centre" => Anchor.Center,
            "right" => Anchor.Right,
            "bottom_left" => Anchor.BottomLeft,
            "bottom" => Anchor.Bottom,
            "bottom_right" => Anchor.BottomRight,
            _ => throw new McpException($"anchor '{anchor}' is not valid; use top_left, top, top_right, left, center, right, bottom_left, bottom or bottom_right."),
        };

        return session.RunAsync(e =>
        {
            var (oldW, oldH) = (e.Document.Width, e.Document.Height);
            e.EditDocument("Resize canvas", doc => doc.Resize(width, height, where));
            return $"Resized the canvas from {oldW}x{oldH} to {width}x{height}.";
        });
    }

    [McpServerTool(Name = "flip")]
    [Description("Mirrors the whole sprite (every frame and layer).")]
    public Task<string> Flip(
        [Description("horizontal (left-right) or vertical (top-bottom).")] string axis)
    {
        bool horizontal = axis.Trim().ToLowerInvariant() switch
        {
            "horizontal" or "x" => true,
            "vertical" or "y" => false,
            _ => throw new McpException($"axis '{axis}' is not valid; use horizontal or vertical."),
        };
        return session.RunAsync(e =>
        {
            e.EditDocument(horizontal ? "Flip horizontal" : "Flip vertical", doc =>
            {
                if (horizontal) doc.FlipHorizontal();
                else doc.FlipVertical();
            });
            return $"Flipped the sprite {(horizontal ? "horizontally" : "vertically")}.";
        });
    }

    [McpServerTool(Name = "rotate")]
    [Description("Rotates the whole sprite (every frame and layer) by 90 degrees. Width and height swap.")]
    public Task<string> Rotate(
        [Description("true: clockwise; false: counter-clockwise.")] bool clockwise = true) =>
        session.RunAsync(e =>
        {
            e.EditDocument(clockwise ? "Rotate right" : "Rotate left", doc => doc.Rotate90(clockwise));
            return $"Rotated {(clockwise ? "clockwise" : "counter-clockwise")}; the canvas is {e.Document.Width}x{e.Document.Height}.";
        });

    [McpServerTool(Name = "set_palette")]
    [Description("Replaces the palette shown in the app with these colours (opaque, in order). The palette is a convenience for the user; drawing accepts any colour.")]
    public Task<string> SetPalette(
        [Description("Colours as #RRGGBB, 1-256 of them.")] string[] colors)
    {
        if (colors == null || colors.Length == 0 || colors.Length > MaxPaletteColors)
            throw new McpException($"colors must have 1 to {MaxPaletteColors} entries.");
        var palette = new Palette();
        for (int i = 0; i < colors.Length; i++)
        {
            uint c = Color(colors[i], $"colors[{i}]") | 0xFF000000;
            if (!palette.Colors.Contains(c)) palette.Colors.Add(c);
        }
        return session.RunAsync(e =>
        {
            e.EditDocument("Set palette", doc => doc.Palette = palette.Clone());
            return $"The palette has {palette.Colors.Count} colour(s).";
        });
    }
}
