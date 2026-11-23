using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tests;

public class CycleTimelineTests
{
    private static SpriteDocument Doc(int[] durations, params ColorCycle[] cycles)
    {
        var doc = SpriteDocument.Create(2, 2);
        for (int i = 1; i < durations.Length; i++) doc.AddFrame(i - 1);
        for (int i = 0; i < durations.Length; i++) doc.Frames[i].DurationMs = durations[i];
        doc.Cycles.AddRange(cycles);
        return doc;
    }

    [Fact]
    public void NoCycles_GivesThePlainFrames()
    {
        var doc = Doc([100, 250]);
        var segments = CycleTimeline.Build(doc, 0, 1, out bool truncated);

        Assert.False(truncated);
        Assert.Equal([new CycleSegment(0, 0, 100), new CycleSegment(1, 100, 250)], segments);
    }

    [Fact]
    public void SingleFrame_LoopsOverOneFullCycle()
    {
        var doc = Doc([100], new ColorCycle { First = 0, Last = 3, StepMs = 100 });
        var segments = CycleTimeline.Build(doc, 0, 0, out bool truncated);

        Assert.False(truncated);
        Assert.Equal([0L, 100, 200, 300], segments.Select(s => s.ElapsedMs));
        Assert.All(segments, s => Assert.Equal((0, 100), (s.Frame, s.DurationMs)));
    }

    [Fact]
    public void LoopIsTheLcmOfFramesAndCycles()
    {
        var doc = Doc([150, 150], new ColorCycle { First = 0, Last = 1, StepMs = 100 });
        var segments = CycleTimeline.Build(doc, 0, 1, out bool truncated);

        Assert.False(truncated);
        Assert.Equal(600, segments.Sum(s => s.DurationMs));   // lcm(300, 200)
        Assert.Equal([0L, 100, 150, 200, 300, 400, 450, 500], segments.Select(s => s.ElapsedMs));
        Assert.Equal([0, 0, 1, 1, 0, 0, 1, 1], segments.Select(s => s.Frame));
    }

    [Fact]
    public void ShortSegments_MergeIntoThePreviousOne()
    {
        var doc = Doc([110, 90], new ColorCycle { First = 0, Last = 1, StepMs = 100 });
        var segments = CycleTimeline.Build(doc, 0, 1, out _);

        Assert.Equal([new CycleSegment(0, 0, 110), new CycleSegment(1, 110, 90)], segments);
    }

    [Fact]
    public void LongLoops_AreTruncated()
    {
        var doc = Doc([100], new ColorCycle { First = 0, Last = 6, StepMs = 10_000 });
        var segments = CycleTimeline.Build(doc, 0, 0, out bool truncated);

        Assert.True(truncated);
        Assert.Equal(CycleTimeline.MaxLoopMs, segments.Sum(s => (long)s.DurationMs));
        Assert.Equal(6, segments.Count);
    }

    [Fact]
    public void ManySteps_AreCappedAtMaxSegments()
    {
        var doc = Doc([100], new ColorCycle { First = 0, Last = 47, StepMs = 20 },
            new ColorCycle { First = 0, Last = 46, StepMs = 30 });
        var segments = CycleTimeline.Build(doc, 0, 0, out bool truncated);

        Assert.True(truncated);
        Assert.Equal(CycleTimeline.MaxSegments, segments.Count);
    }
}
