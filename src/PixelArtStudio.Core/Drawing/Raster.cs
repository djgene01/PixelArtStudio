using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.Drawing;

/// <summary>Brush size, mirror flags and optional selection clip applied to every raster operation.</summary>
public readonly record struct PaintOptions(int BrushSize = 1, bool MirrorX = false, bool MirrorY = false, RectI? Clip = null);

/// <summary>Pixel drawing primitives. All operations ignore pixels outside the buffer or the clip.</summary>
public static class Raster
{
    public const int MaxBrushSize = 64;

    /// <summary>Stamps the square brush centred on (x, y), plus its mirror images.</summary>
    public static void Plot(PixelBuffer target, int x, int y, uint color, in PaintOptions options)
    {
        int size = Math.Clamp(options.BrushSize, 1, MaxBrushSize);
        int left = x - (size - 1) / 2, top = y - (size - 1) / 2;
        for (int py = top; py < top + size; py++)
        for (int px = left; px < left + size; px++)
            PlotMirrored(target, px, py, color, options);
    }

    /// <summary>Sets one pixel and its mirror images, without the brush.</summary>
    public static void PlotMirrored(PixelBuffer target, int x, int y, uint color, in PaintOptions options)
    {
        SetClipped(target, x, y, color, options.Clip);
        int mx = target.Width - 1 - x, my = target.Height - 1 - y;
        if (options.MirrorX) SetClipped(target, mx, y, color, options.Clip);
        if (options.MirrorY) SetClipped(target, x, my, color, options.Clip);
        if (options.MirrorX && options.MirrorY) SetClipped(target, mx, my, color, options.Clip);
    }

    /// <summary>
    /// Bresenham line including both end points. Points are generated in a canonical direction, so
    /// (a, b) and (b, a) cover the same pixels; the result is ordered from the first argument.
    /// </summary>
    public static List<(int X, int Y)> LinePoints(int x0, int y0, int x1, int y1)
    {
        bool swap = x1 < x0 || (x1 == x0 && y1 < y0);
        if (swap) (x0, y0, x1, y1) = (x1, y1, x0, y0);

        var points = new List<(int, int)>();
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            points.Add((x0, y0));
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
        if (swap) points.Reverse();
        return points;
    }

    public static void Line(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, in PaintOptions options)
    {
        foreach (var (x, y) in LinePoints(x0, y0, x1, y1))
            Plot(target, x, y, color, options);
    }

    /// <summary>Snaps the end point so the line is horizontal, vertical or at 45 degrees.</summary>
    public static (int X, int Y) ConstrainLine(int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0;
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        if (adx > ady * 2) return (x1, y0);
        if (ady > adx * 2) return (x0, y1);
        int d = Math.Max(adx, ady);
        return (x0 + Math.Sign(dx) * d, y0 + Math.Sign(dy) * d);
    }

    /// <summary>Snaps the end point so the box from the start point is square.</summary>
    public static (int X, int Y) ConstrainSquare(int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0;
        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return (x0 + (dx < 0 ? -d : d), y0 + (dy < 0 ? -d : d));
    }

    public static void Rectangle(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, bool filled, in PaintOptions options)
    {
        var r = RectI.FromPoints(x0, y0, x1, y1);
        if (filled)
        {
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                PlotMirrored(target, x, y, color, options);
            return;
        }
        for (int x = r.X; x < r.Right; x++)
        {
            Plot(target, x, r.Y, color, options);
            Plot(target, x, r.Bottom - 1, color, options);
        }
        for (int y = r.Y; y < r.Bottom; y++)
        {
            Plot(target, r.X, y, color, options);
            Plot(target, r.Right - 1, y, color, options);
        }
    }

    /// <summary>Pixels of the ellipse inscribed in the box between two corners (midpoint algorithm, A. Zingl).</summary>
    public static HashSet<(int X, int Y)> EllipsePoints(int x0, int y0, int x1, int y1, bool filled)
    {
        var points = new HashSet<(int, int)>();
        void Span(int xa, int xb, int y)
        {
            if (filled) for (int x = xa; x <= xb; x++) points.Add((x, y));
            else { points.Add((xa, y)); points.Add((xb, y)); }
        }

        long a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
        long dx = 4 * (1 - a) * b * b, dy = 4 * (b1 + 1) * a * a;
        long err = dx + dy + b1 * a * a;

        if (x0 > x1) { x0 = x1; x1 += (int)a; }
        if (y0 > y1) y0 = y1;
        y0 += (int)((b + 1) / 2);
        y1 = y0 - (int)b1;
        a *= 8 * a;
        b1 = 8 * b * b;

        do
        {
            Span(x0, x1, y0);
            Span(x0, x1, y1);
            long e2 = 2 * err;
            if (e2 <= dy) { y0++; y1--; err += dy += a; }
            if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
        } while (x0 <= x1);

        // Flat ellipses (width 1 or 2) stop early; finish the tips.
        while (y0 - y1 < b)
        {
            Span(x0 - 1, x1 + 1, y0++);
            Span(x0 - 1, x1 + 1, y1--);
        }
        return points;
    }

    public static void Ellipse(PixelBuffer target, int x0, int y0, int x1, int y1, uint color, bool filled, in PaintOptions options)
    {
        foreach (var (x, y) in EllipsePoints(x0, y0, x1, y1, filled))
        {
            if (filled) PlotMirrored(target, x, y, color, options);
            else Plot(target, x, y, color, options);
        }
    }

    /// <summary>
    /// Replaces the colour under (x, y) with <paramref name="color"/>: the connected region when
    /// <paramref name="contiguous"/>, otherwise every matching pixel. Returns true if anything changed.
    /// </summary>
    public static bool FloodFill(PixelBuffer target, int x, int y, uint color, bool contiguous, in PaintOptions options)
    {
        bool changed = FillAt(target, x, y, color, contiguous, options.Clip);
        int mx = target.Width - 1 - x, my = target.Height - 1 - y;
        if (options.MirrorX) changed |= FillAt(target, mx, y, color, contiguous, options.Clip);
        if (options.MirrorY) changed |= FillAt(target, x, my, color, contiguous, options.Clip);
        if (options.MirrorX && options.MirrorY) changed |= FillAt(target, mx, my, color, contiguous, options.Clip);
        return changed;
    }

    private static bool FillAt(PixelBuffer target, int x, int y, uint color, bool contiguous, RectI? clip)
    {
        var bounds = clip?.Intersect(target.Bounds) ?? target.Bounds;
        if (!bounds.Contains(x, y)) return false;

        uint seed = target.Get(x, y);
        if (ColorUtil.Same(seed, color)) return false;

        var px = target.Pixels;
        int w = target.Width;

        if (!contiguous)
        {
            for (int yy = bounds.Y; yy < bounds.Bottom; yy++)
            for (int xx = bounds.X; xx < bounds.Right; xx++)
                if (ColorUtil.Same(px[yy * w + xx], seed)) px[yy * w + xx] = color;
            return true;
        }

        // Scanline fill: fill a horizontal run, then queue the start of each matching run above and below.
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            if (!ColorUtil.Same(px[sy * w + sx], seed)) continue;

            int left = sx, right = sx;
            while (left - 1 >= bounds.X && ColorUtil.Same(px[sy * w + left - 1], seed)) left--;
            while (right + 1 < bounds.Right && ColorUtil.Same(px[sy * w + right + 1], seed)) right++;
            for (int i = left; i <= right; i++) px[sy * w + i] = color;

            foreach (int ny in (ReadOnlySpan<int>)[sy - 1, sy + 1])
            {
                if (ny < bounds.Y || ny >= bounds.Bottom) continue;
                bool inRun = false;
                for (int i = left; i <= right; i++)
                {
                    bool match = ColorUtil.Same(px[ny * w + i], seed);
                    if (match && !inRun) stack.Push((i, ny));
                    inRun = match;
                }
            }
        }
        return true;
    }

    private static void SetClipped(PixelBuffer target, int x, int y, uint color, RectI? clip)
    {
        if (clip is { } c && !c.Contains(x, y)) return;
        target.Set(x, y, color);
    }
}
