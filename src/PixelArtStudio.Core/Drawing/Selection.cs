using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.Drawing;

/// <summary>Rectangular region copy, clear and paste.</summary>
public static class Selection
{
    /// <summary>Copies <paramref name="rect"/> out of <paramref name="source"/>; parts outside the source are transparent.</summary>
    public static PixelBuffer Extract(PixelBuffer source, RectI rect)
    {
        var result = new PixelBuffer(Math.Max(1, rect.Width), Math.Max(1, rect.Height));
        var area = rect.Intersect(source.Bounds);
        for (int y = area.Y; y < area.Bottom; y++)
            Array.Copy(source.Pixels, y * source.Width + area.X,
                result.Pixels, (y - rect.Y) * result.Width + (area.X - rect.X), area.Width);
        return result;
    }

    public static void Clear(PixelBuffer target, RectI rect)
    {
        var area = rect.Intersect(target.Bounds);
        for (int y = area.Y; y < area.Bottom; y++)
            Array.Clear(target.Pixels, y * target.Width + area.X, area.Width);
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="target"/> with its top-left at (x, y), clipped.
    /// With <paramref name="skipTransparent"/>, fully transparent source pixels leave the target unchanged.
    /// </summary>
    public static void Paste(PixelBuffer target, PixelBuffer source, int x, int y, bool skipTransparent)
    {
        var area = new RectI(x, y, source.Width, source.Height).Intersect(target.Bounds);
        for (int ty = area.Y; ty < area.Bottom; ty++)
        {
            int srcRow = (ty - y) * source.Width - x;
            int dstRow = ty * target.Width;
            if (!skipTransparent)
            {
                Array.Copy(source.Pixels, srcRow + area.X, target.Pixels, dstRow + area.X, area.Width);
                continue;
            }
            for (int tx = area.X; tx < area.Right; tx++)
            {
                uint p = source.Pixels[srcRow + tx];
                if (p >> 24 != 0) target.Pixels[dstRow + tx] = p;
            }
        }
    }
}
