using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tests;

public class CompositorTests
{
    [Fact]
    public void BlendOver_OpaqueSourceReplaces() =>
        Assert.Equal(0xFF00FF00u, Compositor.BlendOver(0xFFFF0000, 0xFF00FF00));

    [Fact]
    public void BlendOver_TransparentSourceKeepsDestination() =>
        Assert.Equal(0xFFFF0000u, Compositor.BlendOver(0xFFFF0000, 0x0000FF00));

    [Fact]
    public void BlendOver_HalfAlphaOverOpaque_MixesColours()
    {
        uint result = Compositor.BlendOver(0xFF000000, 0x80FFFFFF);
        Assert.Equal(255, ColorUtil.A(result));
        Assert.InRange(ColorUtil.R(result), 127, 129);
    }

    [Fact]
    public void BlendOver_HalfOverHalf_CombinesAlpha()
    {
        uint result = Compositor.BlendOver(0x80FF0000, 0x800000FF);
        Assert.InRange(ColorUtil.A(result), 190, 193);   // 0.5 + 0.5*0.5 = 0.75
    }

    [Fact]
    public void Flatten_SkipsHiddenLayersAndAppliesOpacity()
    {
        var doc = SpriteDocument.Create(2, 1);
        doc.AddLayer(0);
        doc.GetCel(0, 0).Set(0, 0, 0xFFFF0000);
        doc.GetCel(0, 1).Set(0, 0, 0xFF0000FF);
        doc.GetCel(0, 1).Set(1, 0, 0xFF0000FF);

        Assert.Equal(0xFF0000FFu, Compositor.Flatten(doc, 0).Get(0, 0));

        doc.Layers[1].Visible = false;
        Assert.Equal(0xFFFF0000u, Compositor.Flatten(doc, 0).Get(0, 0));
        Assert.Equal(0u, Compositor.Flatten(doc, 0).Get(1, 0));

        doc.Layers[1].Visible = true;
        doc.Layers[1].Opacity = 0.5;
        uint mixed = Compositor.Flatten(doc, 0).Get(0, 0);
        Assert.InRange(ColorUtil.R(mixed), 126, 129);
        Assert.InRange(ColorUtil.B(mixed), 126, 129);
    }

    [Fact]
    public void FlattenScaled_ShrinksByWholeFactor()
    {
        var doc = SpriteDocument.Create(10, 4);
        doc.GetCel(0, 0).Set(3, 3, 0xFFFF0000);
        doc.GetCel(0, 0).Set(4, 3, 0xFF00FF00);

        var small = Compositor.FlattenScaled(doc, 0, maxSide: 4);   // factor 3

        Assert.Equal((4, 2), (small.Width, small.Height));
        Assert.Equal(0xFFFF0000u, small.Get(1, 1));                 // samples (3, 3)
        Assert.True(Compositor.Flatten(doc, 0).ContentEquals(Compositor.FlattenScaled(doc, 0, maxSide: 10)));
    }

    [Fact]
    public void FlattenInto_ParallelMatchesSerial()
    {
        var doc = SpriteDocument.Create(600, 600);   // over the parallel threshold
        doc.AddLayer(0);
        doc.Layers[1].Opacity = 0.5;
        var random = new Random(42);
        foreach (var cel in doc.Frames[0].Cels)
            for (int i = 0; i < cel.Pixels.Length; i += 7) cel.Pixels[i] = (uint)random.Next() | 0x80000000;

        var parallel = Compositor.Flatten(doc, 0);
        var serial = doc.NewCel();
        for (int y = 0; y < doc.Height; y += 100)   // 600 x 100 bands stay under the threshold
            Compositor.FlattenInto(serial, doc, 0, new RectI(0, y, doc.Width, 100));

        Assert.True(parallel.ContentEquals(serial));
    }

    [Fact]
    public void Scale_RepeatsPixels()
    {
        var buf = new PixelBuffer(2, 1);
        buf.Set(1, 0, 0xFFFFFFFF);

        var scaled = Compositor.Scale(buf, 3);

        Assert.Equal(6, scaled.Width);
        Assert.Equal(3, scaled.Height);
        Assert.Equal(0u, scaled.Get(2, 2));
        Assert.Equal(0xFFFFFFFFu, scaled.Get(3, 0));
        Assert.Equal(0xFFFFFFFFu, scaled.Get(5, 2));
    }
}
