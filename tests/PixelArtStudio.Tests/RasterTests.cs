using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tests;

public class RasterTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;

    [Theory]
    [InlineData(0, 0, 7, 3)]
    [InlineData(5, 9, 1, 0)]
    [InlineData(2, 2, 2, 8)]
    [InlineData(0, 4, 9, 4)]
    [InlineData(3, 3, 3, 3)]
    public void Line_IncludesBothEndPoints(int x0, int y0, int x1, int y1)
    {
        var points = Raster.LinePoints(x0, y0, x1, y1);

        Assert.Equal((x0, y0), points[0]);
        Assert.Equal((x1, y1), points[^1]);
        Assert.Equal(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1, points.Count);
    }

    [Theory]
    [InlineData(0, 0, 7, 3)]
    [InlineData(1, 8, 6, 0)]
    [InlineData(0, 0, 5, 5)]
    public void Line_IsSymmetric(int x0, int y0, int x1, int y1)
    {
        var forward = Raster.LinePoints(x0, y0, x1, y1).ToHashSet();
        var backward = Raster.LinePoints(x1, y1, x0, y0).ToHashSet();

        Assert.True(forward.SetEquals(backward));
    }

    [Fact]
    public void Line_PointsAreConnected()
    {
        var points = Raster.LinePoints(0, 0, 13, 5);
        for (int i = 1; i < points.Count; i++)
        {
            Assert.True(Math.Abs(points[i].X - points[i - 1].X) <= 1);
            Assert.True(Math.Abs(points[i].Y - points[i - 1].Y) <= 1);
        }
    }

    [Fact]
    public void FloodFill_Contiguous_StopsAtBorder()
    {
        var buf = new PixelBuffer(10, 10);
        Raster.Line(buf, 5, 0, 5, 9, Blue, new PaintOptions(1));

        Assert.True(Raster.FloodFill(buf, 0, 0, Red, contiguous: true, new PaintOptions(1)));

        Assert.Equal(Red, buf.Get(4, 9));
        Assert.Equal(Blue, buf.Get(5, 3));
        Assert.Equal(0u, buf.Get(6, 0));
        Assert.Equal(50, buf.Pixels.Count(p => p == Red));
    }

    [Fact]
    public void FloodFill_Global_ReplacesAllMatches()
    {
        var buf = new PixelBuffer(10, 10);
        Raster.Line(buf, 5, 0, 5, 9, Blue, new PaintOptions(1));

        Raster.FloodFill(buf, 0, 0, Red, contiguous: false, new PaintOptions(1));

        Assert.Equal(90, buf.Pixels.Count(p => p == Red));
        Assert.Equal(10, buf.Pixels.Count(p => p == Blue));
    }

    [Fact]
    public void FloodFill_RespectsClip()
    {
        var buf = new PixelBuffer(10, 10);
        var clip = new RectI(2, 2, 3, 3);

        Raster.FloodFill(buf, 3, 3, Red, contiguous: true, new PaintOptions(1, Clip: clip));

        Assert.Equal(9, buf.Pixels.Count(p => p == Red));
        Assert.Equal(0u, buf.Get(1, 1));
        Assert.Equal(0u, buf.Get(5, 5));
    }

    [Fact]
    public void FloodFill_OutsideOrSameColour_ReturnsFalse()
    {
        var buf = new PixelBuffer(4, 4);
        Assert.False(Raster.FloodFill(buf, -1, 2, Red, true, new PaintOptions(1)));
        Assert.False(Raster.FloodFill(buf, 1, 1, 0x00123456, true, new PaintOptions(1)));
    }

    [Fact]
    public void Plot_OutsideCanvas_DoesNotThrow()
    {
        var buf = new PixelBuffer(4, 4);
        Raster.Line(buf, -10, -10, 20, 20, Red, new PaintOptions(8, MirrorX: true, MirrorY: true));
        Assert.Equal(Red, buf.Get(0, 0));
    }

    [Fact]
    public void Plot_MirrorX_PaintsOppositeSide()
    {
        var buf = new PixelBuffer(8, 4);
        Raster.Plot(buf, 1, 2, Red, new PaintOptions(1, MirrorX: true));
        Assert.Equal(Red, buf.Get(6, 2));
        Assert.Equal(2, buf.Pixels.Count(p => p == Red));
    }

    [Fact]
    public void Rectangle_OutlineAndFilled()
    {
        var outline = new PixelBuffer(10, 10);
        Raster.Rectangle(outline, 1, 1, 4, 3, Red, filled: false, new PaintOptions(1));
        Assert.Equal(10, outline.Pixels.Count(p => p == Red));

        var filled = new PixelBuffer(10, 10);
        Raster.Rectangle(filled, 4, 3, 1, 1, Red, filled: true, new PaintOptions(1));
        Assert.Equal(12, filled.Pixels.Count(p => p == Red));
    }

    [Fact]
    public void Ellipse_StaysInsideBoundingBoxAndTouchesEdges()
    {
        var points = Raster.EllipsePoints(2, 3, 11, 8, filled: false);

        Assert.All(points, p => Assert.True(p.X >= 2 && p.X <= 11 && p.Y >= 3 && p.Y <= 8));
        Assert.Contains(points, p => p.X == 2);
        Assert.Contains(points, p => p.X == 11);
        Assert.Contains(points, p => p.Y == 3);
        Assert.Contains(points, p => p.Y == 8);
    }
}
