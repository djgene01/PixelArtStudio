namespace PixelArtStudio.Core.Model;

/// <summary>A width x height grid of straight-alpha <c>0xAARRGGBB</c> pixels, row-major.</summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
        : this(width, height, new uint[CheckedArea(width, height)])
    {
    }

    public PixelBuffer(int width, int height, uint[] pixels)
    {
        if (pixels.Length != CheckedArea(width, height))
            throw new ArgumentException("Pixel array length does not match the size.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }
    public RectI Bounds => new(0, 0, Width, Height);

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>Returns the pixel, or transparent when out of range.</summary>
    public uint Get(int x, int y) => Contains(x, y) ? Pixels[y * Width + x] : 0;

    /// <summary>Sets the pixel; out-of-range writes are ignored.</summary>
    public void Set(int x, int y, uint color)
    {
        if (Contains(x, y)) Pixels[y * Width + x] = color;
    }

    public PixelBuffer Clone() => new(Width, Height, (uint[])Pixels.Clone());

    public void Clear(uint color = 0) => Array.Fill(Pixels, color);

    public void CopyFrom(PixelBuffer source)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("Buffers differ in size.", nameof(source));
        Array.Copy(source.Pixels, Pixels, Pixels.Length);
    }

    /// <summary>Copies <paramref name="region"/> (clipped to both buffers) from a buffer of the same size.</summary>
    public void CopyFrom(PixelBuffer source, RectI region)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("Buffers differ in size.", nameof(source));
        region = region.Intersect(Bounds);
        for (int y = region.Y; y < region.Bottom; y++)
            Array.Copy(source.Pixels, y * Width + region.X, Pixels, y * Width + region.X, region.Width);
    }

    public bool ContentEquals(PixelBuffer other) =>
        other.Width == Width && other.Height == Height && Pixels.AsSpan().SequenceEqual(other.Pixels);

    /// <summary>The smallest rectangle holding every pixel that differs from <paramref name="other"/>, or null when equal.</summary>
    public RectI? DiffBounds(PixelBuffer other)
    {
        if (other.Width != Width || other.Height != Height)
            throw new ArgumentException("Buffers differ in size.", nameof(other));

        int top = -1, bottom = -1, left = Width, right = -1;
        for (int y = 0; y < Height; y++)
        {
            var a = Pixels.AsSpan(y * Width, Width);
            var b = other.Pixels.AsSpan(y * Width, Width);
            int first = a.CommonPrefixLength(b);
            if (first == Width) continue;

            int last = Width - 1;
            while (a[last] == b[last]) last--;
            if (top < 0) top = y;
            bottom = y;
            left = Math.Min(left, first);
            right = Math.Max(right, last);
        }
        return top < 0 ? null : new RectI(left, top, right - left + 1, bottom - top + 1);
    }

    public bool IsTransparent()
    {
        foreach (uint p in Pixels)
            if (p >> 24 != 0) return false;
        return true;
    }

    public void FlipHorizontal()
    {
        for (int y = 0; y < Height; y++)
            Array.Reverse(Pixels, y * Width, Width);
    }

    public void FlipVertical()
    {
        var row = new uint[Width];
        for (int top = 0, bottom = Height - 1; top < bottom; top++, bottom--)
        {
            Array.Copy(Pixels, top * Width, row, 0, Width);
            Array.Copy(Pixels, bottom * Width, Pixels, top * Width, Width);
            Array.Copy(row, 0, Pixels, bottom * Width, Width);
        }
    }

    /// <summary>Returns a copy rotated by 90 degrees; width and height swap.</summary>
    public PixelBuffer Rotated90(bool clockwise)
    {
        var result = new PixelBuffer(Height, Width);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int nx = clockwise ? Height - 1 - y : y;
            int ny = clockwise ? x : Width - 1 - x;
            result.Pixels[ny * result.Width + nx] = Pixels[y * Width + x];
        }
        return result;
    }

    private static int CheckedArea(int width, int height)
    {
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Size must be at least 1x1.");
        return checked(width * height);
    }
}
