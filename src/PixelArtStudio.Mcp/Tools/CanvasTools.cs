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
        [Description("New width, 1-4096.")] int width,
        [Description("New height, 1-4096.")] int height,
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
            CheckBudget(width, height, e.Document.Layers.Count, e.Document.Frames.Count);
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
    [Description("Replaces the palette shown in the app with these colours (opaque, in order; duplicates are dropped). " +
                 "Drawing accepts any colour, but colour cycles refer to palette positions (slots): cycles are clamped " +
                 "to the new palette and removed if fewer than 2 of their slots remain.")]
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
            int cyclesBefore = e.Document.Cycles.Count;
            e.EditMeta("Set palette", doc =>
            {
                doc.Palette = palette.Clone();
                doc.NormalizeCycles();
            });
            int dropped = cyclesBefore - e.Document.Cycles.Count;
            return $"The palette has {palette.Colors.Count} colour(s)." +
                   (dropped > 0 ? $" {dropped} colour cycle(s) no longer fit and were removed." : "");
        });
    }

    [McpServerTool(Name = "add_color_cycle")]
    [Description("Adds colour cycling (the classic palette-swap animation): every step_ms the colours of palette slots " +
                 "first..last rotate by one place. Pixels painted with exactly one of those palette colours (opaque) " +
                 "appear to move, e.g. water, fire or conveyor belts, without extra frames. It is shown live in the app " +
                 "and can be baked into exports (export bake_cycles). Use get_state for palette slot indices.")]
    public Task<string> AddColorCycle(
        [Description("First palette slot (0-based, inclusive).")] int first,
        [Description("Last palette slot (inclusive); the range needs at least 2 slots.")] int last,
        [Description("Milliseconds per one-slot step, 20-10000 (rounded to 10).")] int step_ms = ColorCycle.DefaultStepMs,
        [Description("Rotate towards lower slots instead of higher ones.")] bool reverse = false,
        [Description("Name shown in the app. Default: Cycle N.")] string? name = null)
    {
        CheckStep(step_ms);
        return session.RunAsync(e =>
        {
            var doc = e.Document;
            int count = doc.Palette.Colors.Count;
            CheckIndex(first, count, "first");
            CheckIndex(last, count, "last");
            if (last <= first) throw new McpException($"last ({last}) must be greater than first ({first}); a cycle needs at least 2 slots.");
            if (doc.Cycles.Count >= SpriteDocument.MaxCycles) throw new McpException($"A sprite can have at most {SpriteDocument.MaxCycles} colour cycles.");

            var cycle = new ColorCycle
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"Cycle {doc.Cycles.Count + 1}" : name.Trim(),
                First = first,
                Last = last,
                StepMs = step_ms,
                Reverse = reverse,
            };
            e.EditMeta("Add colour cycle", d => d.Cycles.Add(cycle.Clone()));
            var colors = doc.Palette.Colors.Skip(first).Take(last - first + 1).ToList();
            string warning = colors.Distinct().Count() < colors.Count ? " Warning: the range has duplicate colours; the first slot of each wins." : "";
            return $"Added colour cycle {doc.Cycles.Count - 1} '{cycle.Name}': slots {first}-{last} " +
                   $"({string.Join(", ", colors.Select(ColorUtil.ToHex))}), {cycle.StepMs} ms per step{(reverse ? ", reversed" : "")}.{warning}";
        });
    }

    [McpServerTool(Name = "remove_color_cycle", Destructive = true)]
    [Description("Removes a colour cycle.")]
    public Task<string> RemoveColorCycle(
        [Description("Cycle index (see get_state).")] int index) =>
        session.RunAsync(e =>
        {
            CheckIndex(index, e.Document.Cycles.Count, "index");
            string cycleName = e.Document.Cycles[index].Name;
            e.EditMeta("Remove colour cycle", d => d.Cycles.RemoveAt(index));
            return $"Removed colour cycle {index} '{cycleName}'. {e.Document.Cycles.Count} cycle(s) left.";
        });

    [McpServerTool(Name = "set_color_cycle")]
    [Description("Changes a colour cycle's speed, direction or enabled state. Omitted values are left unchanged.")]
    public Task<string> SetColorCycle(
        [Description("Cycle index (see get_state).")] int index,
        [Description("Milliseconds per one-slot step, 20-10000.")] int? step_ms = null,
        [Description("Rotate towards lower slots.")] bool? reverse = null,
        [Description("Turn the cycle on or off.")] bool? enabled = null)
    {
        if (step_ms is { } step) CheckStep(step);
        if (step_ms == null && reverse == null && enabled == null) throw new McpException("Pass at least one of step_ms, reverse or enabled.");
        return session.RunAsync(e =>
        {
            CheckIndex(index, e.Document.Cycles.Count, "index");
            e.EditMeta("Colour cycle", d =>
            {
                var c = d.Cycles[index];
                if (step_ms is { } s) c.StepMs = s;
                if (reverse is { } r) c.Reverse = r;
                if (enabled is { } en) c.Enabled = en;
            });
            var cycle = e.Document.Cycles[index];
            return $"Colour cycle {index} '{cycle.Name}': slots {cycle.First}-{cycle.Last}, {cycle.StepMs} ms per step, " +
                   $"reverse {cycle.Reverse}, enabled {cycle.Enabled}.";
        });
    }

    [McpServerTool(Name = "replace_color", Destructive = true)]
    [Description("Replaces one exact colour with another in every frame and layer (a static palette swap), as one undo step. " +
                 "With update_palette and an opaque new colour, a matching palette entry is changed too, so colour cycles that use that slot follow.")]
    public Task<string> ReplaceColor(
        [Description("Colour to replace: #RRGGBB, #RRGGBBAA or transparent.")] string from,
        [Description("New colour: #RRGGBB, #RRGGBBAA or transparent.")] string to,
        [Description("Also change the colour in the palette.")] bool update_palette = true)
    {
        uint source = Color(from, "from"), target = Color(to, "to");
        if (source == target) throw new McpException("from and to are the same colour.");
        return session.RunAsync(e =>
        {
            int changed = 0;
            // The palette holds opaque colours only.
            bool inPalette = update_palette && target >> 24 == 0xFF && e.Document.Palette.Colors.Contains(source);
            if (!inPalette && !ContainsColor(e.Document, source))
                return $"No pixels are {ColorUtil.ToHex(source)}; nothing changed.";
            e.EditDocument("Replace colour", doc =>
            {
                changed = doc.ReplaceColor(source, target);
                if (!inPalette) return;
                int slot = doc.Palette.Colors.IndexOf(source);
                doc.Palette.Colors[slot] = target;
            });
            return $"Replaced {ColorUtil.ToHex(source)} with {ColorUtil.ToHex(target)} in {changed} pixel(s)" +
                   (inPalette ? " and in the palette." : ".");
        });
    }

    private static bool ContainsColor(SpriteDocument doc, uint color) =>
        doc.Frames.SelectMany(f => f.Cels).Any(c => c.Pixels.AsSpan().Contains(color));

    private static void CheckStep(int stepMs)
    {
        if (stepMs < ColorCycle.MinStepMs || stepMs > ColorCycle.MaxStepMs)
            throw new McpException($"step_ms must be {ColorCycle.MinStepMs} to {ColorCycle.MaxStepMs} (got {stepMs}).");
    }
}
