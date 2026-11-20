using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using static PixelArtStudio.Mcp.Tools.ToolHelpers;

namespace PixelArtStudio.Mcp.Tools;

[McpServerToolType]
public sealed class ViewTools(IStudioSession session)
{
    public const int MaxScale = 16;
    public const int TargetFrameSize = 512;
    public const int TargetSheetSize = 1024;
    public const int MaxImageSize = 2048;
    public const int DefaultSheetColumns = 8;

    [McpServerTool(Name = "view_frame", ReadOnly = true)]
    [Description("Returns a PNG of one frame (all visible layers, or one layer) scaled up with nearest-neighbour so you can check your work.")]
    public async Task<IEnumerable<ContentBlock>> ViewFrame(
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Layer index to view one layer alone. Default: all visible layers.")] int? layer = null,
        [Description("Integer upscale 1-16. Default: about 512 px on the long side.")] int? scale = null,
        [Description("Colour drawn behind transparent pixels, e.g. #FFFFFF. Default: keep transparency.")] string? background = null)
    {
        uint? back = background == null ? null : Color(background, "background");
        var (pixels, label) = await session.RunAsync(e =>
        {
            var doc = e.Document;
            int f = Frame(e, frame);
            if (layer is { } requested)
            {
                int l = Layer(e, requested);
                return (doc.GetCel(f, l).Clone(), $"Frame {f}, {LayerLabel(e, l)}");
            }
            return (Compositor.Flatten(doc, f), $"Frame {f} of {doc.Frames.Count}, all visible layers");
        });

        int longSide = Math.Max(pixels.Width, pixels.Height);
        int factor = ChooseScale(scale, longSide, TargetFrameSize);
        byte[] png = await Task.Run(() => EncodePng(Compositor.Scale(back is { } b ? OnBackground(pixels, b) : pixels, factor)));
        return
        [
            new TextContentBlock { Text = $"{label}: {pixels.Width}x{pixels.Height} px shown at {factor}x." },
            ImageContentBlock.FromBytes(png, "image/png"),
        ];
    }

    [McpServerTool(Name = "view_animation", ReadOnly = true)]
    [Description("Returns every frame side by side in one PNG (left to right, then top to bottom), plus the frame durations, so you can check an animation.")]
    public async Task<IEnumerable<ContentBlock>> ViewAnimation(
        [Description("Frames per row. Default: up to 8.")] int? columns = null,
        [Description("Integer upscale 1-16. Default: about 1024 px on the long side (never over 2048).")] int? scale = null,
        [Description("Colour drawn behind transparent pixels, e.g. #FFFFFF. Default: keep transparency.")] string? background = null)
    {
        if (columns is < 1) throw new McpException($"columns must be at least 1 (got {columns}).");
        uint? back = background == null ? null : Color(background, "background");
        var (frames, durations) = await session.RunAsync(e =>
        {
            var doc = e.Document;
            var flat = Enumerable.Range(0, doc.Frames.Count).Select(f => Compositor.Flatten(doc, f)).ToList();
            return (flat, doc.Frames.Select(f => f.DurationMs).ToList());
        });

        int cols = Math.Min(columns ?? DefaultSheetColumns, frames.Count);
        int rows = (frames.Count + cols - 1) / cols;
        int sheetW = frames[0].Width * cols, sheetH = frames[0].Height * rows;
        int longSide = Math.Max(sheetW, sheetH);
        if (longSide > MaxImageSize)
            throw new McpException($"The sheet would be {sheetW}x{sheetH} px even at 1x (limit {MaxImageSize}). " +
                                   "Use fewer columns, or view_frame for single frames.");
        int factor = ChooseScale(scale, longSide, TargetSheetSize);

        byte[] png = await Task.Run(() =>
        {
            var tiles = frames.Select(p => Compositor.Scale(back is { } b ? OnBackground(p, b) : p, factor)).ToList();
            return EncodePng(SpriteSheet.Build(tiles, cols, out _));
        });
        string timing = string.Join(", ", durations.Select((d, i) => $"{i}: {d} ms"));
        return
        [
            new TextContentBlock
            {
                Text = $"{frames.Count} frame(s) of {frames[0].Width}x{frames[0].Height} px, {cols} per row, shown at {factor}x. " +
                       $"Durations: {timing}. Total {durations.Sum()} ms.",
            },
            ImageContentBlock.FromBytes(png, "image/png"),
        ];
    }

    /// <summary>The requested scale, or one that brings the long side near <paramref name="target"/>; never above <see cref="MaxImageSize"/>.</summary>
    private static int ChooseScale(int? requested, int longSide, int target)
    {
        if (requested is < 1 or > MaxScale) throw new McpException($"scale must be 1 to {MaxScale} (got {requested}).");
        int factor = requested ?? Math.Clamp(target / longSide, 1, MaxScale);
        return Math.Max(1, Math.Min(factor, MaxImageSize / longSide));
    }
}
