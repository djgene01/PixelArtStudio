using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;
using static PixelArtStudio.Mcp.Tools.ToolHelpers;

namespace PixelArtStudio.Mcp.Tools;

/// <summary>One drawing operation of the <c>draw</c> tool. Which fields are used depends on <see cref="Type"/>.</summary>
public sealed class DrawOp
{
    [JsonPropertyName("type")]
    [Description("pixels, line, rect, ellipse, fill or clear.")]
    public string Type { get; set; } = "";

    [JsonPropertyName("color")]
    [Description("#RRGGBB, #RRGGBBAA or transparent. Required for every type except clear.")]
    public string? Color { get; set; }

    [JsonPropertyName("points")]
    [Description("pixels: list of [x, y] pairs.")]
    public int[][]? Points { get; set; }

    [JsonPropertyName("x0")]
    [Description("line/rect/ellipse/clear: first corner or end point.")]
    public int? X0 { get; set; }

    [JsonPropertyName("y0")]
    public int? Y0 { get; set; }

    [JsonPropertyName("x1")]
    [Description("line/rect/ellipse/clear: second corner or end point (inclusive).")]
    public int? X1 { get; set; }

    [JsonPropertyName("y1")]
    public int? Y1 { get; set; }

    [JsonPropertyName("x")]
    [Description("fill: seed pixel.")]
    public int? X { get; set; }

    [JsonPropertyName("y")]
    public int? Y { get; set; }

    [JsonPropertyName("filled")]
    [Description("rect/ellipse: fill the shape instead of drawing its outline. Default false.")]
    public bool? Filled { get; set; }

    [JsonPropertyName("contiguous")]
    [Description("fill: only the connected region (true, default) or every pixel of the seed colour (false).")]
    public bool? Contiguous { get; set; }

    [JsonPropertyName("brush_size")]
    [Description("pixels/line/outlines: square brush size 1-8. Default 1.")]
    public int? BrushSize { get; set; }
}

[McpServerToolType]
public sealed class DrawingTools(IStudioSession session)
{
    public const int MaxRegion = 128;

    /// <summary>Characters used for colours in <c>get_pixels</c>; '.' is reserved for transparent.</summary>
    private const string LegendChars =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&*+-/:;<=>?@^_~|()[]{},'`";

    [McpServerTool(Name = "draw")]
    [Description("Applies a batch of drawing operations to one cel (frame + layer) as a single undo step. " +
                 "Types: pixels {points, color}, line {x0,y0,x1,y1,color}, rect {x0,y0,x1,y1,color,filled}, " +
                 "ellipse {x0,y0,x1,y1,color,filled} (inscribed in the box), fill {x,y,color,contiguous}, " +
                 "clear {x0,y0,x1,y1} (or no coordinates to clear the whole cel). Ops run in order; pixels outside the canvas are ignored. " +
                 "Colours replace pixels (no blending); use transparent to erase.")]
    public Task<string> Draw(
        [Description("Operations to apply, in order.")] DrawOp[] ops,
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Layer index (0 is the bottom). Default: the active layer.")] int? layer = null)
    {
        if (ops == null || ops.Length == 0) throw new McpException("ops must contain at least one operation.");
        var actions = ops.Select((op, i) => Compile(op, i)).ToList();

        return session.RunAsync(e =>
        {
            int f = Frame(e, frame), l = Layer(e, layer);
            bool changed = e.EditCel(ops.Length == 1 ? $"Draw {ops[0].Type}" : $"Draw ({ops.Length} ops)", f, l, cel =>
            {
                foreach (var action in actions) action(cel);
            });
            return changed
                ? $"Drew {ops.Length} op(s) on frame {f}, {LayerLabel(e, l)}."
                : $"No pixels changed on frame {f}, {LayerLabel(e, l)}.";
        });
    }

    [McpServerTool(Name = "paint_rows")]
    [Description("Paints a block of pixels from text rows, one character per pixel, as a single undo step. " +
                 "The legend maps each character to a colour; '.' leaves the pixel unchanged. " +
                 "Example: x=0, y=0, rows=[\".kk.\", \"kyyk\"], legend={\"k\":\"#000000\",\"y\":\"#FFEC27\"}.")]
    public Task<string> PaintRows(
        [Description("Left column of the block.")] int x,
        [Description("Top row of the block.")] int y,
        [Description("Rows of the block, top to bottom.")] string[] rows,
        [Description("Map of single characters to colours (#RRGGBB, #RRGGBBAA or transparent).")] Dictionary<string, string> legend,
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Layer index (0 is the bottom). Default: the active layer.")] int? layer = null)
    {
        if (rows == null || rows.Length == 0) throw new McpException("rows must contain at least one row.");
        var colors = new Dictionary<char, uint>();
        foreach (var (key, value) in legend ?? [])
        {
            if (key.Length != 1) throw new McpException($"legend key '{key}' must be a single character.");
            if (key[0] == '.') throw new McpException("'.' is reserved for 'leave unchanged' and cannot be in the legend.");
            colors[key[0]] = Color(value, $"legend '{key}'");
        }
        for (int r = 0; r < rows.Length; r++)
        foreach (char c in rows[r])
            if (c != '.' && !colors.ContainsKey(c))
                throw new McpException($"Row {r} uses '{c}', which is not in the legend.");

        return session.RunAsync(e =>
        {
            int f = Frame(e, frame), l = Layer(e, layer);
            int painted = 0;
            bool changed = e.EditCel("Paint rows", f, l, cel =>
            {
                for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length; i++)
                {
                    char c = rows[r][i];
                    if (c == '.' || !cel.Contains(x + i, y + r)) continue;
                    cel.Set(x + i, y + r, colors[c]);
                    painted++;
                }
            });
            return changed
                ? $"Painted {painted} pixel(s) on frame {f}, {LayerLabel(e, l)}."
                : $"No pixels changed on frame {f}, {LayerLabel(e, l)}.";
        });
    }

    [McpServerTool(Name = "get_pixels", ReadOnly = true)]
    [Description("Reads a region as text rows plus a legend (the same format paint_rows takes). '.' is transparent. " +
                 "Without layer it reads the flattened frame (visible layers). Regions are limited to 128x128.")]
    public Task<string> GetPixels(
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Layer index to read one layer. Default: the flattened frame.")] int? layer = null,
        [Description("Left column. Default 0.")] int x = 0,
        [Description("Top row. Default 0.")] int y = 0,
        [Description("Width. Default: to the right edge.")] int? width = null,
        [Description("Height. Default: to the bottom edge.")] int? height = null) =>
        session.RunAsync(e =>
        {
            var doc = e.Document;
            int f = Frame(e, frame);
            if (x < 0 || y < 0 || x >= doc.Width || y >= doc.Height)
                throw new McpException($"({x}, {y}) is outside the {doc.Width}x{doc.Height} canvas.");
            int w = Math.Min(width ?? doc.Width - x, doc.Width - x);
            int h = Math.Min(height ?? doc.Height - y, doc.Height - y);
            if (w < 1 || h < 1) throw new McpException("width and height must be at least 1.");
            if (w > MaxRegion || h > MaxRegion)
                throw new McpException($"The region is {w}x{h}; request at most {MaxRegion}x{MaxRegion} with x, y, width and height.");

            PixelBuffer source;
            string label;
            if (layer is { } requested)
            {
                int l = Layer(e, requested);
                source = doc.GetCel(f, l);
                label = LayerLabel(e, l);
            }
            else
            {
                source = Compositor.Flatten(doc, f);
                label = "flattened";
            }

            var legend = new Dictionary<uint, char>();
            var rowText = new List<string>(h);
            var sb = new StringBuilder(w);
            for (int py = y; py < y + h; py++)
            {
                sb.Clear();
                for (int px = x; px < x + w; px++)
                {
                    uint c = source.Get(px, py);
                    if (c >> 24 == 0)
                    {
                        sb.Append('.');
                        continue;
                    }
                    if (!legend.TryGetValue(c, out char ch))
                    {
                        if (legend.Count == LegendChars.Length)
                            throw new McpException($"The region has more than {LegendChars.Length} colours; request a smaller region.");
                        ch = LegendChars[legend.Count];
                        legend[c] = ch;
                    }
                    sb.Append(ch);
                }
                rowText.Add(sb.ToString());
            }

            return Json(new
            {
                Frame = f,
                Source = label,
                X = x,
                Y = y,
                Width = w,
                Height = h,
                Legend = legend.ToDictionary(p => p.Value.ToString(), p => ColorUtil.ToHex(p.Key)),
                Rows = rowText,
            });
        });

    [McpServerTool(Name = "copy_cel")]
    [Description("Copies all pixels of one cel (frame + layer) over another, as a single undo step. Useful to start an animation frame from another.")]
    public Task<string> CopyCel(
        [Description("Source frame.")] int from_frame,
        [Description("Source layer.")] int from_layer,
        [Description("Target frame.")] int to_frame,
        [Description("Target layer.")] int to_layer) =>
        session.RunAsync(e =>
        {
            int ff = Frame(e, from_frame, "from_frame"), fl = Layer(e, from_layer, "from_layer");
            int tf = Frame(e, to_frame, "to_frame"), tl = Layer(e, to_layer, "to_layer");
            var source = e.Document.GetCel(ff, fl).Clone();
            bool changed = e.EditCel("Copy cel", tf, tl, cel => cel.CopyFrom(source));
            return changed
                ? $"Copied frame {ff} {LayerLabel(e, fl)} to frame {tf} {LayerLabel(e, tl)}."
                : "The target cel already matched the source.";
        });

    [McpServerTool(Name = "shift_cel")]
    [Description("Moves every pixel of one cel by (dx, dy), as a single undo step. Pixels moved off the canvas are lost; uncovered pixels become transparent.")]
    public Task<string> ShiftCel(
        [Description("Pixels to move right (negative: left).")] int dx,
        [Description("Pixels to move down (negative: up).")] int dy,
        [Description("Frame index. Default: the active frame.")] int? frame = null,
        [Description("Layer index (0 is the bottom). Default: the active layer.")] int? layer = null) =>
        session.RunAsync(e =>
        {
            int f = Frame(e, frame), l = Layer(e, layer);
            bool changed = e.EditCel("Shift cel", f, l, cel =>
            {
                var pixels = Selection.Extract(cel, cel.Bounds);
                Selection.Clear(cel, cel.Bounds);
                Selection.Paste(cel, pixels, dx, dy, skipTransparent: false);
            });
            return changed
                ? $"Shifted frame {f}, {LayerLabel(e, l)} by ({dx}, {dy})."
                : $"No pixels changed on frame {f}, {LayerLabel(e, l)}.";
        });

    /// <summary>Validates one op and turns it into a drawing action, so bad input fails before anything is drawn.</summary>
    private static Action<PixelBuffer> Compile(DrawOp op, int index)
    {
        string where = $"ops[{index}]";
        string type = (op.Type ?? "").Trim().ToLowerInvariant();
        int brush = op.BrushSize ?? 1;
        if (brush < 1 || brush > Raster.MaxBrushSize)
            throw new McpException($"{where}: brush_size must be 1 to {Raster.MaxBrushSize} (got {brush}).");
        var options = new PaintOptions(brush);

        (int X0, int Y0, int X1, int Y1) Box()
        {
            if (op.X0 is not { } x0 || op.Y0 is not { } y0 || op.X1 is not { } x1 || op.Y1 is not { } y1)
                throw new McpException($"{where}: {type} needs x0, y0, x1 and y1.");
            return (x0, y0, x1, y1);
        }

        switch (type)
        {
            case "pixels":
            {
                uint color = Color(op.Color, $"{where}.color");
                if (op.Points is not { Length: > 0 } points) throw new McpException($"{where}: pixels needs points, e.g. [[0,0],[1,0]].");
                var list = points.Select((p, i) => p is { Length: 2 }
                    ? (X: p[0], Y: p[1])
                    : throw new McpException($"{where}.points[{i}] must be an [x, y] pair.")).ToList();
                return cel =>
                {
                    foreach (var (x, y) in list) Raster.Plot(cel, x, y, color, options);
                };
            }
            case "line":
            {
                uint color = Color(op.Color, $"{where}.color");
                var (x0, y0, x1, y1) = Box();
                return cel => Raster.Line(cel, x0, y0, x1, y1, color, options);
            }
            case "rect":
            {
                uint color = Color(op.Color, $"{where}.color");
                var (x0, y0, x1, y1) = Box();
                bool filled = op.Filled ?? false;
                return cel => Raster.Rectangle(cel, x0, y0, x1, y1, color, filled, options);
            }
            case "ellipse":
            {
                uint color = Color(op.Color, $"{where}.color");
                var (x0, y0, x1, y1) = Box();
                bool filled = op.Filled ?? false;
                return cel => Raster.Ellipse(cel, x0, y0, x1, y1, color, filled, options);
            }
            case "fill":
            {
                uint color = Color(op.Color, $"{where}.color");
                if (op.X is not { } x || op.Y is not { } y) throw new McpException($"{where}: fill needs x and y.");
                bool contiguous = op.Contiguous ?? true;
                return cel => Raster.FloodFill(cel, x, y, color, contiguous, options);
            }
            case "clear":
            {
                if (op.X0 == null && op.Y0 == null && op.X1 == null && op.Y1 == null) return cel => cel.Clear();
                var (x0, y0, x1, y1) = Box();
                var rect = RectI.FromPoints(x0, y0, x1, y1);
                return cel => Selection.Clear(cel, rect);
            }
            default:
                throw new McpException($"{where}: type '{op.Type}' is not valid; use pixels, line, rect, ellipse, fill or clear.");
        }
    }
}
