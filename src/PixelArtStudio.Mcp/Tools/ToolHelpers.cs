using System.Text.Json;
using ModelContextProtocol;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Mcp.Tools;

/// <summary>Argument validation and formatting shared by the tool classes.</summary>
internal static class ToolHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Resolves an optional frame index (default: the active frame) and checks its range.</summary>
    public static int Frame(IStudioEditor editor, int? frame, string argument = "frame") =>
        CheckIndex(frame ?? editor.ActiveFrame, editor.Document.Frames.Count, argument);

    /// <summary>Resolves an optional layer index (default: the active layer) and checks its range.</summary>
    public static int Layer(IStudioEditor editor, int? layer, string argument = "layer") =>
        CheckIndex(layer ?? editor.ActiveLayer, editor.Document.Layers.Count, argument);

    public static int CheckIndex(int index, int count, string argument)
    {
        if ((uint)index >= (uint)count)
            throw new McpException($"{argument} {index} is out of range (0-{count - 1}).");
        return index;
    }

    /// <summary>Parses <c>#RRGGBB</c>, <c>#RRGGBBAA</c> or <c>transparent</c>.</summary>
    public static uint Color(string? text, string argument = "color")
    {
        if (string.Equals(text?.Trim(), "transparent", StringComparison.OrdinalIgnoreCase)) return ColorUtil.Transparent;
        if (ColorUtil.TryParseHex(text, out uint color)) return color;
        throw new McpException(text == null
            ? $"{argument} is required (#RRGGBB, #RRGGBBAA or transparent)."
            : $"{argument} '{text}' is not valid; use #RRGGBB, #RRGGBBAA or transparent.");
    }

    public static void CheckSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > SpriteDocument.MaxSize || height > SpriteDocument.MaxSize)
            throw new McpException($"Canvas size must be 1 to {SpriteDocument.MaxSize} per side (got {width}x{height}).");
    }

    public static void CheckAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new McpException($"'{path}' is not an absolute path. Pass a full path such as C:\\Sprites\\hero.pxs.");
    }

    public static void RefuseIfDirty(IStudioEditor editor, bool discardChanges)
    {
        if (editor.IsDirty && !discardChanges)
            throw new McpException($"'{editor.DocumentName}' has unsaved changes. Call save first, or pass discard_changes=true to drop them.");
    }

    public static string LayerLabel(IStudioEditor editor, int layer) => $"layer {layer} ({editor.Document.Layers[layer].Name})";

    public static byte[] EncodePng(PixelBuffer pixels)
    {
        using var stream = new MemoryStream();
        ImageExporter.WritePng(pixels, stream);
        return stream.ToArray();
    }

    /// <summary>Copies <paramref name="pixels"/> over a solid background so transparent areas are visible.</summary>
    public static PixelBuffer OnBackground(PixelBuffer pixels, uint background)
    {
        var result = new PixelBuffer(pixels.Width, pixels.Height);
        result.Clear(background);
        Core.Drawing.Compositor.BlendOnto(result, pixels);
        return result;
    }

    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
