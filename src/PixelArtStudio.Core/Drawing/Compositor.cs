using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.Drawing;

/// <summary>Layer blending and scaling.</summary>
public static class Compositor
{
    /// <summary>Source-over blend of straight-alpha colours.</summary>
    public static uint BlendOver(uint dst, uint src, double opacity = 1.0)
    {
        uint sa = src >> 24;
        if (opacity < 1.0) sa = (uint)Math.Round(sa * Math.Max(opacity, 0.0));
        if (sa == 0) return dst;
        if (sa == 255) return src | 0xFF000000;

        uint da = dst >> 24;
        if (da == 0) return sa << 24 | (src & 0x00FFFFFF);

        // Integer source-over with alphas in 0..255, intermediate values scaled by 255*255:
        // outA = sa + da*(1-sa); outC = (sc*sa + dc*da*(1-sa)) / outA.
        uint dWeight = da * (255 - sa);
        uint outAScaled = sa * 255 + dWeight;
        uint Channel(int shift)
        {
            uint sc = (src >> shift) & 0xFF, dc = (dst >> shift) & 0xFF;
            return (sc * sa * 255 + dc * dWeight + outAScaled / 2) / outAScaled;
        }
        uint outA = (outAScaled + 127) / 255;
        return outA << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    /// <summary>Blends every pixel of <paramref name="src"/> onto <paramref name="dst"/> (same size).</summary>
    public static void BlendOnto(PixelBuffer dst, PixelBuffer src, double opacity = 1.0)
    {
        var d = dst.Pixels;
        var s = src.Pixels;
        for (int i = 0; i < d.Length; i++) d[i] = BlendOver(d[i], s[i], opacity);
    }

    /// <summary>Blends the visible layers of a frame, bottom to top, into a new buffer.</summary>
    /// <param name="overrideLayer">Layer whose cel is replaced by <paramref name="overrideCel"/> (for tool previews).</param>
    public static PixelBuffer Flatten(SpriteDocument doc, int frameIndex, int overrideLayer = -1, PixelBuffer? overrideCel = null)
    {
        var result = doc.NewCel();
        FlattenInto(result, doc, frameIndex, result.Bounds, overrideLayer, overrideCel);
        return result;
    }

    /// <summary>Recomposes only <paramref name="region"/> of <paramref name="target"/>.</summary>
    public static void FlattenInto(PixelBuffer target, SpriteDocument doc, int frameIndex, RectI region,
        int overrideLayer = -1, PixelBuffer? overrideCel = null)
    {
        region = region.Intersect(target.Bounds);
        if (region.IsEmpty) return;

        var frame = doc.Frames[frameIndex];
        var output = target.Pixels;
        int width = target.Width;
        for (int y = region.Y; y < region.Bottom; y++)
            Array.Clear(output, y * width + region.X, region.Width);

        for (int l = 0; l < doc.Layers.Count; l++)
        {
            var layer = doc.Layers[l];
            if (!layer.Visible || layer.Opacity <= 0) continue;
            var cel = l == overrideLayer && overrideCel != null ? overrideCel.Pixels : frame.Cels[l].Pixels;
            double opacity = layer.Opacity;
            for (int y = region.Y; y < region.Bottom; y++)
            {
                int row = y * width;
                for (int x = region.X; x < region.Right; x++)
                {
                    uint src = cel[row + x];
                    if (src >> 24 != 0) output[row + x] = BlendOver(output[row + x], src, opacity);
                }
            }
        }
    }

    /// <summary>Nearest-neighbour integer upscale.</summary>
    public static PixelBuffer Scale(PixelBuffer source, int factor)
    {
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
        if (factor == 1) return source.Clone();

        var result = new PixelBuffer(source.Width * factor, source.Height * factor);
        for (int y = 0; y < result.Height; y++)
        {
            int srcRow = y / factor * source.Width;
            int dstRow = y * result.Width;
            for (int x = 0; x < result.Width; x++)
                result.Pixels[dstRow + x] = source.Pixels[srcRow + x / factor];
        }
        return result;
    }
}
