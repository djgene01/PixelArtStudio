using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tests;

public class DocumentTests
{
    private static void AssertConsistent(SpriteDocument doc)
    {
        Assert.NotEmpty(doc.Frames);
        Assert.NotEmpty(doc.Layers);
        Assert.All(doc.Frames, f =>
        {
            Assert.Equal(doc.Layers.Count, f.Cels.Count);
            Assert.All(f.Cels, c => Assert.Equal((doc.Width, doc.Height), (c.Width, c.Height)));
        });
    }

    [Fact]
    public void FrameAndLayerOperations_KeepCelCountsConsistent()
    {
        var doc = SpriteDocument.Create(8, 8);
        doc.AddFrame(0);
        doc.AddLayer(0);
        doc.DuplicateFrame(1);
        doc.DuplicateLayer(1);
        AssertConsistent(doc);
        Assert.Equal(3, doc.Frames.Count);
        Assert.Equal(3, doc.Layers.Count);

        doc.MoveLayer(0, 2);
        doc.MoveFrame(2, 0);
        Assert.True(doc.MergeDown(2));
        Assert.True(doc.DeleteLayer(0));
        Assert.True(doc.DeleteFrame(1));
        AssertConsistent(doc);
        Assert.Single(doc.Layers);
        Assert.Equal(2, doc.Frames.Count);
    }

    [Fact]
    public void DeletingLastFrameOrLayer_IsRefused()
    {
        var doc = SpriteDocument.Create(4, 4);
        Assert.False(doc.DeleteFrame(0));
        Assert.False(doc.DeleteLayer(0));
        Assert.False(doc.MergeDown(0));
        AssertConsistent(doc);
    }

    [Fact]
    public void MoveLayer_MovesCelsWithIt()
    {
        var doc = SpriteDocument.Create(2, 2);
        doc.AddLayer(0, "Top");
        doc.GetCel(0, 1).Set(0, 0, 0xFF112233);

        doc.MoveLayer(1, 0);

        Assert.Equal("Top", doc.Layers[0].Name);
        Assert.Equal(0xFF112233u, doc.GetCel(0, 0).Get(0, 0));
    }

    [Theory]
    [InlineData(Anchor.TopLeft, 0, 0)]
    [InlineData(Anchor.Center, 2, 1)]
    [InlineData(Anchor.BottomRight, 4, 2)]
    public void Resize_PadsAroundAnchor(Anchor anchor, int expectedX, int expectedY)
    {
        var doc = SpriteDocument.Create(2, 2);
        doc.GetCel(0, 0).Set(0, 0, 0xFFFFFFFF);

        doc.Resize(6, 4, anchor);

        AssertConsistent(doc);
        Assert.Equal(0xFFFFFFFFu, doc.GetCel(0, 0).Get(expectedX, expectedY));
    }

    [Fact]
    public void Resize_RejectsOutOfRangeSizes()
    {
        var doc = SpriteDocument.Create(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Resize(0, 4, Anchor.Center));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Resize(4, SpriteDocument.MaxSize + 1, Anchor.Center));
    }

    [Fact]
    public void MaxSize_Is4096()
    {
        var doc = SpriteDocument.Create(SpriteDocument.MaxSize, SpriteDocument.MaxSize);
        Assert.Equal((4096, 4096), (doc.Width, doc.Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteDocument.Create(4097, 1));
    }

    [Fact]
    public void FitsBudget_CountsEveryCel()
    {
        Assert.True(SpriteDocument.FitsBudget(4096, 4096, 4, 8));       // 512M pixels exactly
        Assert.False(SpriteDocument.FitsBudget(4096, 4096, 4, 9));
        Assert.True(SpriteDocument.FitsBudget(64, 64, 10, 1000));
    }

    [Fact]
    public void ReplaceColor_ChangesEveryCelAndCountsPixels()
    {
        var doc = SpriteDocument.Create(3, 3);
        doc.AddFrame(0);
        doc.AddLayer(0);
        doc.GetCel(0, 0).Set(0, 0, 0xFFFF0000);
        doc.GetCel(1, 1).Set(2, 2, 0xFFFF0000);
        doc.GetCel(1, 0).Set(1, 1, 0xFF00FF00);

        Assert.Equal(2, doc.ReplaceColor(0xFFFF0000, 0xFF0000FF));
        Assert.Equal(0xFF0000FFu, doc.GetCel(0, 0).Get(0, 0));
        Assert.Equal(0xFF0000FFu, doc.GetCel(1, 1).Get(2, 2));
        Assert.Equal(0xFF00FF00u, doc.GetCel(1, 0).Get(1, 1));
        Assert.Equal(0, doc.ReplaceColor(0xFFFF0000, 0xFF0000FF));
    }

    [Fact]
    public void NormalizeCycles_ClampsToPaletteAndDropsShortRanges()
    {
        var doc = SpriteDocument.Create(2, 2);
        doc.Palette = new Palette { Colors = { 0xFF000000, 0xFF111111, 0xFF222222, 0xFF333333 } };
        doc.Cycles.Add(new ColorCycle { First = 2, Last = 9 });   // clamped to 2-3
        doc.Cycles.Add(new ColorCycle { First = 3, Last = 7 });   // only slot 3 left: dropped
        doc.Cycles.Add(new ColorCycle { First = 1, Last = 0 });   // reversed: becomes 0-1

        doc.NormalizeCycles();

        Assert.Equal([(2, 3), (0, 1)], doc.Cycles.Select(c => (c.First, c.Last)));
    }

    [Fact]
    public void Clone_CopiesCyclesDeeply()
    {
        var doc = SpriteDocument.Create(2, 2);
        doc.Cycles.Add(new ColorCycle { First = 0, Last = 3, StepMs = 200 });

        var copy = doc.Clone();
        copy.Cycles[0].StepMs = 500;

        Assert.Equal(200, doc.Cycles[0].StepMs);
        var target = SpriteDocument.Create(1, 1);
        target.CopyStateFrom(doc);
        Assert.Equal((0, 3, 200), (target.Cycles[0].First, target.Cycles[0].Last, target.Cycles[0].StepMs));
    }

    [Fact]
    public void PixelBuffer_DiffBoundsAndRegionCopy()
    {
        var a = new PixelBuffer(10, 10);
        var b = a.Clone();
        Assert.Null(a.DiffBounds(b));

        b.Set(3, 4, 0xFFFFFFFF);
        b.Set(7, 2, 0xFFFFFFFF);
        Assert.Equal(new RectI(3, 2, 5, 3), a.DiffBounds(b));

        a.CopyFrom(b, new RectI(0, 0, 5, 5));
        Assert.Equal(0xFFFFFFFFu, a.Get(3, 4));
        Assert.Equal(0u, a.Get(7, 2));
    }

    [Fact]
    public void Rotate90_SwapsSizeAndMovesPixels()
    {
        var doc = SpriteDocument.Create(3, 2);
        doc.GetCel(0, 0).Set(0, 0, 0xFFFFFFFF);

        doc.Rotate90(clockwise: true);

        Assert.Equal((2, 3), (doc.Width, doc.Height));
        Assert.Equal(0xFFFFFFFFu, doc.GetCel(0, 0).Get(1, 0));
        AssertConsistent(doc);
    }

    [Fact]
    public void Flip_MirrorsPixels()
    {
        var doc = SpriteDocument.Create(3, 3);
        doc.GetCel(0, 0).Set(0, 0, 0xFFFFFFFF);

        doc.FlipHorizontal();
        Assert.Equal(0xFFFFFFFFu, doc.GetCel(0, 0).Get(2, 0));

        doc.FlipVertical();
        Assert.Equal(0xFFFFFFFFu, doc.GetCel(0, 0).Get(2, 2));
    }

    [Fact]
    public void Palette_HexAndGplRoundTrip()
    {
        var palette = Palette.CreateDefault();

        Assert.Equal(palette.Colors, Palette.ParseHex(palette.ToHex()).Colors);
        Assert.Equal(palette.Colors, Palette.ParseGpl(palette.ToGpl("test")).Colors);
    }

    [Theory]
    [InlineData("#FF8000", 0xFFFF8000)]
    [InlineData("ff800080", 0x80FF8000)]
    public void ColorUtil_ParsesHex(string text, uint expected)
    {
        Assert.True(ColorUtil.TryParseHex(text, out uint c));
        Assert.Equal(expected, c);
        Assert.True(ColorUtil.TryParseHex(ColorUtil.ToHex(c), out uint again));
        Assert.Equal(c, again);
    }
}
