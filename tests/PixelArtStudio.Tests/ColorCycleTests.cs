using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tests;

public class ColorCycleTests
{
    private const uint A = 0xFFFF0000, B = 0xFF00FF00, C = 0xFF0000FF, D = 0xFFFFFFFF;

    private static Palette PaletteOf(params uint[] colors)
    {
        var palette = new Palette();
        palette.Colors.AddRange(colors);
        return palette;
    }

    private static ColorCycle Cycle(int first, int last, int stepMs = 100, bool reverse = false) =>
        new() { First = first, Last = last, StepMs = stepMs, Reverse = reverse };

    [Fact]
    public void Map_Forward_ShiftsColoursUpOneSlotPerStep()
    {
        var map = ColorCycleMap.Build(PaletteOf(A, B, C, D), [Cycle(0, 2)], elapsedMs: 100)!;

        Assert.Equal(C, map.Map(A));   // slot 0 shows the colour from slot 2
        Assert.Equal(A, map.Map(B));
        Assert.Equal(B, map.Map(C));
        Assert.Equal(D, map.Map(D));   // outside the range
        Assert.Equal(0x80FF0000u, map.Map(0x80FF0000));   // not an exact palette colour
        Assert.Equal(0u, map.Map(0));
    }

    [Fact]
    public void Map_Reverse_ShiftsColoursDown()
    {
        var map = ColorCycleMap.Build(PaletteOf(A, B, C), [Cycle(0, 2, reverse: true)], elapsedMs: 150)!;

        Assert.Equal(B, map.Map(A));
        Assert.Equal(C, map.Map(B));
        Assert.Equal(A, map.Map(C));
    }

    [Fact]
    public void Build_ReturnsNullWhenNothingMoved()
    {
        var palette = PaletteOf(A, B, C);
        Assert.Null(ColorCycleMap.Build(palette, [Cycle(0, 2)], 0));
        Assert.Null(ColorCycleMap.Build(palette, [Cycle(0, 2)], 300));   // full turn
        Assert.Null(ColorCycleMap.Build(palette, [], 1234));
    }

    [Fact]
    public void DisabledOrInvalidCycles_AreIgnored()
    {
        var palette = PaletteOf(A, B, C);
        var disabled = Cycle(0, 2);
        disabled.Enabled = false;

        Assert.Null(ColorCycleMap.Build(palette, [disabled], 100));
        Assert.Null(ColorCycleMap.Build(palette, [Cycle(1, 5)], 100));   // past the end of the palette
        Assert.Equal([-1, -1], ColorCycleMap.StepsAt(palette, [disabled, Cycle(1, 5)], 100));
    }

    [Fact]
    public void DuplicateColours_FirstSlotWins()
    {
        var map = ColorCycleMap.Build(PaletteOf(A, B, A), [Cycle(0, 2)], 100)!;

        Assert.Equal(A, map.Map(A));   // slot 0 shows slot 2 (also A); slot 2's entry is ignored
        Assert.Equal(A, map.Map(B));
    }

    [Fact]
    public void StepsAt_ChangesOnlyOnStepBoundaries()
    {
        var palette = PaletteOf(A, B, C, D);
        ColorCycle[] cycles = [Cycle(0, 3, stepMs: 50)];
        Assert.Equal(ColorCycleMap.StepsAt(palette, cycles, 10), ColorCycleMap.StepsAt(palette, cycles, 49));
        Assert.NotEqual(ColorCycleMap.StepsAt(palette, cycles, 49), ColorCycleMap.StepsAt(palette, cycles, 50));
        Assert.Equal([1], ColorCycleMap.StepsAt(palette, cycles, 250));   // 5 steps over 4 slots
    }

    [Fact]
    public void Flatten_AppliesCycleBeforeBlending()
    {
        var doc = SpriteDocument.Create(2, 1);
        doc.Palette = PaletteOf(A, B);
        doc.Cycles.Add(Cycle(0, 1));
        doc.GetCel(0, 0).Set(0, 0, A);
        doc.GetCel(0, 0).Set(1, 0, D);

        var cycled = Compositor.Flatten(doc, 0, cycle: ColorCycleMap.Build(doc.Palette, doc.Cycles, 100));

        Assert.Equal(B, cycled.Get(0, 0));
        Assert.Equal(D, cycled.Get(1, 0));
        Assert.Equal(A, doc.GetCel(0, 0).Get(0, 0));   // stored pixels are untouched
    }

    [Fact]
    public void StepMs_IsClampedAndRounded()
    {
        Assert.Equal(ColorCycle.MinStepMs, new ColorCycle { StepMs = 1 }.StepMs);
        Assert.Equal(ColorCycle.MaxStepMs, new ColorCycle { StepMs = 99_999 }.StepMs);
        Assert.Equal(130, new ColorCycle { StepMs = 127 }.StepMs);
    }
}
