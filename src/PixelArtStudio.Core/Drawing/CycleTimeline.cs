using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.Drawing;

/// <summary>One stretch of a baked animation: which frame to show, the cycling time to render it at, and how long it shows.</summary>
public readonly record struct CycleSegment(int Frame, long ElapsedMs, int DurationMs);

/// <summary>
/// Turns frames plus colour cycles into a list of still images for export ("baking"): a new image starts whenever
/// the frame changes or any cycle steps, over one full loop of the frames and all cycles together.
/// </summary>
public static class CycleTimeline
{
    public const long MaxLoopMs = 60_000;
    public const int MaxSegments = 600;
    public const int MinSegmentMs = 20;

    /// <param name="truncated">Set when the full loop was longer than <see cref="MaxLoopMs"/> or needed more than <see cref="MaxSegments"/> images.</param>
    public static List<CycleSegment> Build(SpriteDocument doc, int firstFrame, int lastFrame, out bool truncated)
    {
        truncated = false;
        var frames = Enumerable.Range(firstFrame, lastFrame - firstFrame + 1).ToList();
        var durations = frames.Select(f => doc.Frames[f].DurationMs).ToList();
        var starts = new long[frames.Count];
        for (int i = 1; i < frames.Count; i++) starts[i] = starts[i - 1] + durations[i - 1];
        long frameLoop = starts[^1] + durations[^1];

        var steps = ColorCycleMap.StepsAt(doc.Palette, doc.Cycles, 0);
        var active = doc.Cycles.Where((_, i) => steps[i] >= 0).ToList();
        if (active.Count == 0)
            return frames.Select((f, i) => new CycleSegment(f, starts[i], durations[i])).ToList();

        long loop = 1;
        foreach (var c in active) loop = Lcm(loop, (long)c.StepMs * c.Length);
        if (frames.Count > 1) loop = Lcm(loop, frameLoop);
        if (loop > MaxLoopMs)
        {
            loop = MaxLoopMs;
            truncated = true;
        }

        var boundaries = new SortedSet<long>();
        if (frames.Count > 1)
        {
            for (long rep = 0; rep < loop; rep += frameLoop)
            foreach (long s in starts)
                if (rep + s < loop) boundaries.Add(rep + s);
        }
        foreach (var c in active)
            for (long t = 0; t < loop; t += c.StepMs) boundaries.Add(t);
        boundaries.Add(0);

        var times = boundaries.ToList();
        var segments = new List<CycleSegment>();
        for (int i = 0; i < times.Count; i++)
        {
            long t = times[i];
            int duration = (int)((i + 1 < times.Count ? times[i + 1] : loop) - t);
            if (duration < MinSegmentMs && segments.Count > 0)
            {
                var last = segments[^1];
                segments[^1] = last with { DurationMs = last.DurationMs + duration };
                continue;
            }
            if (segments.Count == MaxSegments)
            {
                truncated = true;
                break;
            }
            segments.Add(new CycleSegment(frames[FrameAt(starts, t % frameLoop)], t, duration));
        }
        return segments;
    }

    private static int FrameAt(long[] starts, long t)
    {
        int index = Array.BinarySearch(starts, t);
        return index >= 0 ? index : ~index - 1;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    /// <summary>Least common multiple, saturating just above <see cref="MaxLoopMs"/> so it cannot overflow.</summary>
    private static long Lcm(long a, long b) => Math.Min(a / Gcd(a, b) * b, MaxLoopMs + 1);
}
