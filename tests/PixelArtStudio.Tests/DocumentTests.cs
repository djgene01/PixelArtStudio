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
