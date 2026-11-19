namespace PixelArtStudio.Core.Model;

/// <summary>Integer rectangle. <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Builds the rectangle spanning two corner pixels, both inclusive, in any order.</summary>
    public static RectI FromPoints(int x0, int y0, int x1, int y1)
    {
        int left = Math.Min(x0, x1), top = Math.Min(y0, y1);
        return new RectI(left, top, Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);
    }

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public RectI Intersect(RectI other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? default : new RectI(left, top, right - left, bottom - top);
    }

    public RectI Union(RectI other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        int left = Math.Min(X, other.X), top = Math.Min(Y, other.Y);
        return new RectI(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    public RectI Inflate(int amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public RectI Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);
}
