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

    /// <summary>Regions of at least this many pixels are composed on several threads.</summary>
    public const int ParallelThreshold = 262_144;

    /// <summary>Blends the visible layers of a frame, bottom to top, into a new buffer.</summary>
    /// <param name="overrideLayer">Layer whose cel is replaced by <paramref name="overrideCel"/> (for tool previews).</param>
    /// <param name="cycle">Colour cycling remap applied to each layer's pixels before blending.</param>
    public static PixelBuffer Flatten(SpriteDocument doc, int frameIndex, int overrideLayer = -1, PixelBuffer? overrideCel = null,
        ColorCycleMap? cycle = null)
    {
        var result = doc.NewCel();
        FlattenInto(result, doc, frameIndex, result.Bounds, overrideLayer, overrideCel, cycle);
        return result;
    }

    /// <summary>Recomposes only <paramref name="region"/> of <paramref name="target"/>.</summary>
    public static void FlattenInto(PixelBuffer target, SpriteDocument doc, int frameIndex, RectI region,
        int overrideLayer = -1, PixelBuffer? overrideCel = null, ColorCycleMap? cycle = null)
    {
        region = region.Intersect(target.Bounds);
        if (region.IsEmpty) return;

        var frame = doc.Frames[frameIndex];
        var layers = new List<(uint[] Pixels, double Opacity)>();
        for (int l = 0; l < doc.Layers.Count; l++)
        {
            var layer = doc.Layers[l];
            if (!layer.Visible || layer.Opacity <= 0) continue;
            var cel = l == overrideLayer && overrideCel != null ? overrideCel.Pixels : frame.Cels[l].Pixels;
            layers.Add((cel, layer.Opacity));
        }

        var output = target.Pixels;
        int width = target.Width;
        void ComposeRow(int y)
        {
            int start = y * width + region.X, end = start + region.Width;
            Array.Clear(output, start, region.Width);
            foreach (var (cel, opacity) in layers)
            {
                for (int i = start; i < end; i++)
                {
                    uint src = cel[i];
                    if (src >> 24 == 0) continue;
                    if (cycle != null) src = cycle.Map(src);
                    output[i] = BlendOver(output[i], src, opacity);
                }
            }
        }

        if ((long)region.Width * region.Height >= ParallelThreshold)
            Parallel.For(region.Y, region.Bottom, ComposeRow);
        else
            for (int y = region.Y; y < region.Bottom; y++) ComposeRow(y);
    }

    /// <summary>
    /// The flattened frame, shrunk by a whole factor (nearest-neighbour) so neither side exceeds
    /// <paramref name="maxSide"/>. Only the sampled pixels are composed, so it is cheap for big sprites.
    /// </summary>
    public static PixelBuffer FlattenScaled(SpriteDocument doc, int frameIndex, int maxSide, ColorCycleMap? cycle = null)
    {
        int longSide = Math.Max(doc.Width, doc.Height);
        if (longSide <= maxSide) return Flatten(doc, frameIndex, cycle: cycle);

        int factor = (longSide + maxSide - 1) / maxSide;
        int w = (doc.Width + factor - 1) / factor, h = (doc.Height + factor - 1) / factor;
        var result = new PixelBuffer(w, h);
        var frame = doc.Frames[frameIndex];
        for (int l = 0; l < doc.Layers.Count; l++)
        {
            var layer = doc.Layers[l];
            if (!layer.Visible || layer.Opacity <= 0) continue;
            var cel = frame.Cels[l].Pixels;
            for (int y = 0; y < h; y++)
            {
                int srcRow = y * factor * doc.Width, dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    uint src = cel[srcRow + x * factor];
                    if (src >> 24 == 0) continue;
                    if (cycle != null) src = cycle.Map(src);
                    result.Pixels[dstRow + x] = BlendOver(result.Pixels[dstRow + x], src, layer.Opacity);
                }
            }
        }
        return result;
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
