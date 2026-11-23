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
    [Description("Returns a PNG of one frame (all visible layers, or one layer) scaled up with nearest-neighbour so you can check your work. " +
                 "For big sprites pass a region (x, y, width, height) to see detail; a view over 2048 px is shrunk to fit.")]
    public async Task<IEnumerable<ContentBlock>> ViewFrame(
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Layer index to view one layer alone. Default: all visible layers.")] int? layer = null,
        [Description("Integer upscale 1-16. Default: about 512 px on the long side.")] int? scale = null,
        [Description("Colour drawn behind transparent pixels, e.g. #FFFFFF. Default: keep transparency.")] string? background = null,
        [Description("Show colour cycling as it looks this many milliseconds into playback. Default: the stored colours.")] int? cycle_time_ms = null,
        [Description("Region left column. Default 0.")] int? x = null,
        [Description("Region top row. Default 0.")] int? y = null,
        [Description("Region width. Default: to the right edge.")] int? width = null,
        [Description("Region height. Default: to the bottom edge.")] int? height = null)
    {
        uint? back = background == null ? null : Color(background, "background");
        if (scale is < 1 or > MaxScale) throw new McpException($"scale must be 1 to {MaxScale} (got {scale}).");
        if (cycle_time_ms is < 0) throw new McpException($"cycle_time_ms must be 0 or more (got {cycle_time_ms}).");
        bool hasRegion = x != null || y != null || width != null || height != null;

        var (pixels, label, region) = await session.RunAsync(e =>
        {
            var doc = e.Document;
            int f = Frame(e, frame);
            var cycle = cycle_time_ms is { } t ? ColorCycleMap.Build(doc.Palette, doc.Cycles, t) : null;
            var rect = doc.NewCel().Bounds;
            if (hasRegion)
            {
                int rx = x ?? 0, ry = y ?? 0;
                if (rx < 0 || ry < 0 || rx >= doc.Width || ry >= doc.Height)
                    throw new McpException($"({rx}, {ry}) is outside the {doc.Width}x{doc.Height} canvas.");
                int rw = Math.Min(width ?? doc.Width - rx, doc.Width - rx), rh = Math.Min(height ?? doc.Height - ry, doc.Height - ry);
                if (rw < 1 || rh < 1) throw new McpException("width and height must be at least 1.");
                rect = new RectI(rx, ry, rw, rh);
            }

            string what;
            PixelBuffer result;
            if (layer is { } requested)
            {
                int l = Layer(e, requested);
                what = $"Frame {f}, {LayerLabel(e, l)}";
                result = Selection.Extract(doc.GetCel(f, l), rect);
                if (cycle != null)
                    for (int i = 0; i < result.Pixels.Length; i++) result.Pixels[i] = cycle.Map(result.Pixels[i]);
            }
            else
            {
                what = $"Frame {f} of {doc.Frames.Count}, all visible layers";
                if (!hasRegion && Math.Max(doc.Width, doc.Height) > MaxImageSize)
                {
                    result = Compositor.FlattenScaled(doc, f, MaxImageSize, cycle);
                }
                else
                {
                    var full = new PixelBuffer(doc.Width, doc.Height);
                    Compositor.FlattenInto(full, doc, f, rect, cycle: cycle);
                    result = hasRegion ? Selection.Extract(full, rect) : full;
                }
            }
            if (cycle_time_ms is { } ms) what += $", colour cycling at {ms} ms";
            return (result, what, rect);
        });

        int shrink = 1;
        if (Math.Max(pixels.Width, pixels.Height) > MaxImageSize)
        {
            shrink = (Math.Max(pixels.Width, pixels.Height) + MaxImageSize - 1) / MaxImageSize;
            pixels = Shrink(pixels, shrink);
        }
        else if (pixels.Width < region.Width || pixels.Height < region.Height)
        {
            shrink = (Math.Max(region.Width, region.Height) + MaxImageSize - 1) / MaxImageSize;
        }

        int longSide = Math.Max(pixels.Width, pixels.Height);
        int factor = shrink > 1 ? 1 : ChooseScale(scale, longSide, TargetFrameSize);
        byte[] png = await Task.Run(() => EncodePng(Compositor.Scale(back is { } b ? OnBackground(pixels, b) : pixels, factor)));
        string where = hasRegion ? $" region ({region.X}, {region.Y}) {region.Width}x{region.Height} px" : $" {region.Width}x{region.Height} px";
        string shown = shrink > 1
            ? $"shrunk to 1/{shrink} ({pixels.Width}x{pixels.Height}) to fit {MaxImageSize} px; pass x, y, width and height to see detail"
            : $"shown at {factor}x";
        return
        [
            new TextContentBlock { Text = $"{label}:{where} {shown}." },
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

    /// <summary>Nearest-neighbour shrink by a whole factor.</summary>
    private static PixelBuffer Shrink(PixelBuffer source, int factor)
    {
        int w = (source.Width + factor - 1) / factor, h = (source.Height + factor - 1) / factor;
        var result = new PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            result.Pixels[y * w + x] = source.Pixels[y * factor * source.Width + x * factor];
        return result;
    }

    /// <summary>The requested scale, or one that brings the long side near <paramref name="target"/>; never above <see cref="MaxImageSize"/>.</summary>
    private static int ChooseScale(int? requested, int longSide, int target)
    {
        if (requested is < 1 or > MaxScale) throw new McpException($"scale must be 1 to {MaxScale} (got {requested}).");
        int factor = requested ?? Math.Clamp(target / longSide, 1, MaxScale);
        return Math.Max(1, Math.Min(factor, MaxImageSize / longSide));
    }
}
