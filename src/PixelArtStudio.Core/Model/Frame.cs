namespace PixelArtStudio.Core.Model;

/// <summary>One animation frame: a duration and one cel per layer, in layer order.</summary>
public sealed class Frame
{
    public const int DefaultDurationMs = 100;
    public const int MinDurationMs = 10;
    public const int MaxDurationMs = 60_000;

    private int _durationMs = DefaultDurationMs;

    public int DurationMs
    {
        get => _durationMs;
        set => _durationMs = Math.Clamp(value, MinDurationMs, MaxDurationMs);
    }

    public List<PixelBuffer> Cels { get; } = [];

    public Frame Clone()
    {
        var copy = new Frame { DurationMs = DurationMs };
        foreach (var cel in Cels) copy.Cels.Add(cel.Clone());
        return copy;
    }
}
