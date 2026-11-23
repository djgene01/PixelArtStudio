using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using static PixelArtStudio.Mcp.Tools.ToolHelpers;

namespace PixelArtStudio.Mcp.Tools;

// Parameter names are the argument names clients see, hence snake_case.
[McpServerToolType]
public sealed class DocumentTools(IStudioSession session)
{
    public const int MaxNewFrames = 256;

    [McpServerTool(Name = "get_state", ReadOnly = true)]
    [Description("Returns the open sprite: size, name, file path, unsaved flag, active frame and layer, layers (bottom first), frame durations, palette (index = slot) and colour cycles.")]
    public Task<string> GetState() => session.RunAsync(e =>
    {
        var doc = e.Document;
        return Json(new
        {
            Name = e.DocumentName,
            Path = e.FilePath,
            Dirty = e.IsDirty,
            doc.Width,
            doc.Height,
            ActiveFrame = e.ActiveFrame,
            ActiveLayer = e.ActiveLayer,
            Layers = doc.Layers.Select((l, i) => new { Index = i, l.Name, l.Visible, l.Opacity }),
            Frames = doc.Frames.Select((f, i) => new { Index = i, f.DurationMs }),
            Palette = doc.Palette.Colors.Select(ColorUtil.ToHex),
            Cycles = doc.Cycles.Select((c, i) => new { Index = i, c.Name, c.First, c.Last, c.StepMs, c.Reverse, c.Enabled }),
        });
    });

    [McpServerTool(Name = "new_sprite", Destructive = true)]
    [Description("Replaces the open sprite with a blank one (one layer, transparent). Fails if there are unsaved changes unless discard_changes is true.")]
    public Task<string> NewSprite(
        [Description("Width in pixels, 1-4096.")] int width,
        [Description("Height in pixels, 1-4096.")] int height,
        [Description("Number of blank frames, 1-256.")] int frames = 1,
        [Description("Drop unsaved changes in the open sprite.")] bool discard_changes = false)
    {
        CheckSize(width, height);
        if (frames < 1 || frames > MaxNewFrames) throw new McpException($"frames must be 1 to {MaxNewFrames} (got {frames}).");
        CheckBudget(width, height, 1, frames);

        return session.RunAsync(e =>
        {
            RefuseIfDirty(e, discard_changes);
            var doc = SpriteDocument.Create(width, height);
            for (int i = 1; i < frames; i++) doc.AddFrame(i - 1);
            e.ReplaceDocument(doc, null, "Untitled");
            return $"New {width}x{height} sprite with {frames} frame(s) and 1 layer.";
        });
    }

    [McpServerTool(Name = "open_file", Destructive = true)]
    [Description("Opens a .pxs project, or a PNG/BMP/GIF/WebP image (animated files open as frames). Fails if there are unsaved changes unless discard_changes is true.")]
    public async Task<string> OpenFile(
        [Description("Absolute path of the file.")] string path,
        [Description("Drop unsaved changes in the open sprite.")] bool discard_changes = false)
    {
        CheckAbsolute(path);
        if (!File.Exists(path)) throw new McpException($"File not found: {path}");

        bool isProject = string.Equals(Path.GetExtension(path), ProjectFile.Extension, StringComparison.OrdinalIgnoreCase);
        SpriteDocument doc;
        try
        {
            doc = await Task.Run(() => isProject ? ProjectFile.Load(path) : ImageImporter.LoadDocument(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new McpException($"Could not open {path}: {ex.Message}", ex);
        }

        return await session.RunAsync(e =>
        {
            RefuseIfDirty(e, discard_changes);
            e.ReplaceDocument(doc, isProject ? path : null, Path.GetFileNameWithoutExtension(path));
            return $"Opened {path}: {doc.Width}x{doc.Height}, {doc.Frames.Count} frame(s), {doc.Layers.Count} layer(s)." +
                   (isProject ? "" : " It is an unsaved sprite until you save it as .pxs.");
        });
    }

    [McpServerTool(Name = "save")]
    [Description("Saves the sprite as a .pxs project (keeps layers and frames). Use export for PNG/GIF and other image formats.")]
    public Task<string> Save(
        [Description("Absolute .pxs path. Optional when the sprite already has a file.")] string? path = null) =>
        session.RunAsync(e =>
        {
            string target = path ?? e.FilePath ?? throw new McpException("The sprite has never been saved; pass an absolute .pxs path.");
            CheckAbsolute(target);
            if (!string.Equals(Path.GetExtension(target), ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new McpException($"save writes .pxs projects only (got '{target}'). Use export for image formats.");
            try
            {
                e.Save(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new McpException($"Could not save {target}: {ex.Message}", ex);
            }
            return $"Saved {target}";
        });

    [McpServerTool(Name = "export")]
    [Description("Exports the flattened sprite (visible layers) to image files and returns the files written.")]
    public async Task<string> Export(
        [Description("Absolute output path. For png_sequence, files are named <name>_0001.png and so on next to it.")] string path,
        [Description("One of: png, bmp (one frame), png_sequence, sprite_sheet, gif, apng, webp (animated).")] string format,
        [Description("Integer nearest-neighbour upscale, 1-16.")] int scale = 1,
        [Description("Frame for png/bmp. Default: the active frame.")] int? frame = null,
        [Description("First frame of the range for multi-frame formats. Default: 0.")] int? first_frame = null,
        [Description("Last frame of the range (inclusive). Default: the last frame.")] int? last_frame = null,
        [Description("sprite_sheet columns; 0 means one row.")] int columns = 0,
        [Description("Animated formats: loop forever (true) or play once.")] bool loop = true,
        [Description("sprite_sheet: also write a .json file with frame rectangles and durations.")] bool write_json = false,
        [Description("Multi-frame formats: render colour cycling into the output (an image per cycle step, over one full loop).")] bool bake_cycles = false)
    {
        CheckAbsolute(path);
        var exportFormat = format.Trim().ToLowerInvariant() switch
        {
            "png" => ExportFormat.Png,
            "bmp" => ExportFormat.Bmp,
            "png_sequence" => ExportFormat.PngSequence,
            "sprite_sheet" => ExportFormat.SpriteSheet,
            "gif" => ExportFormat.Gif,
            "apng" => ExportFormat.Apng,
            "webp" => ExportFormat.WebP,
            _ => throw new McpException($"format '{format}' is not valid; use png, bmp, png_sequence, sprite_sheet, gif, apng or webp."),
        };
        if (scale < 1 || scale > ImageExporter.MaxScale) throw new McpException($"scale must be 1 to {ImageExporter.MaxScale} (got {scale}).");
        if (columns < 0) throw new McpException($"columns must be 0 or more (got {columns}).");
        string? dir = Path.GetDirectoryName(path);
        if (dir == null || !Directory.Exists(dir)) throw new McpException($"The folder '{dir}' does not exist.");

        var (doc, options) = await session.RunAsync(e =>
        {
            int count = e.Document.Frames.Count;
            int first = CheckIndex(first_frame ?? 0, count, "first_frame");
            int last = CheckIndex(last_frame ?? count - 1, count, "last_frame");
            if (last < first) throw new McpException($"last_frame {last} is before first_frame {first}.");
            var opts = new ExportOptions
            {
                Format = exportFormat,
                Scale = scale,
                CurrentFrame = Frame(e, frame),
                FirstFrame = first,
                LastFrame = last,
                SheetColumns = columns,
                Loop = loop,
                WriteJson = write_json,
                BakeCycles = bake_cycles,
            };
            return (e.Document.Clone(), opts);
        });

        try
        {
            var (files, truncated) = await Task.Run(() =>
            {
                var written = ImageExporter.Export(doc, path, options, out bool cut);
                return (written, cut);
            });
            string note = truncated
                ? $"\nThe colour cycling loop was longer than {Core.Drawing.CycleTimeline.MaxLoopMs / 1000} s or {Core.Drawing.CycleTimeline.MaxSegments} images, so it was cut short and will not loop seamlessly."
                : "";
            return "Exported:\n" + string.Join("\n", files) + note;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new McpException($"Export failed: {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "set_active")]
    [Description("Selects the frame and/or layer shown and edited in the app, so the user sees what you are working on.")]
    public Task<string> SetActive(
        [Description("Frame index.")] int? frame = null,
        [Description("Layer index (0 is the bottom).")] int? layer = null) =>
        session.RunAsync(e =>
        {
            int f = Frame(e, frame), l = Layer(e, layer);
            e.ActiveFrame = f;
            e.ActiveLayer = l;
            return $"Active: frame {f}, {LayerLabel(e, l)}.";
        });

    [McpServerTool(Name = "undo")]
    [Description("Undoes the last step (yours or the user's). Each tool call that changes the sprite is one step.")]
    public Task<string> Undo() => session.RunAsync(e => e.Undo() is { } name ? $"Undid: {name}" : "Nothing to undo.");

    [McpServerTool(Name = "redo")]
    [Description("Redoes the last undone step.")]
    public Task<string> Redo() => session.RunAsync(e => e.Redo() is { } name ? $"Redid: {name}" : "Nothing to redo.");
}
