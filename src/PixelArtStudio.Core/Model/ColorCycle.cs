namespace PixelArtStudio.Core.Model;

/// <summary>
/// Colour cycling over a contiguous range of palette slots, the classic "palette swap" animation: every
/// <see cref="StepMs"/> the colours of slots <see cref="First"/> to <see cref="Last"/> rotate by one place.
/// Pixels are not changed; the rotation is applied when the sprite is shown or exported.
/// </summary>
public sealed class ColorCycle
{
    public const int MinStepMs = 20;
    public const int MaxStepMs = 10_000;
    public const int DefaultStepMs = 100;

    private int _stepMs = DefaultStepMs;

    public string Name { get; set; } = "Cycle";

    /// <summary>First palette slot of the range (inclusive).</summary>
    public int First { get; set; }

    /// <summary>Last palette slot of the range (inclusive).</summary>
    public int Last { get; set; }

    /// <summary>Time per one-slot step, clamped to 20..10000 ms and rounded to 10 ms.</summary>
    public int StepMs
    {
        get => _stepMs;
        set => _stepMs = Math.Clamp((int)Math.Round(value / 10.0) * 10, MinStepMs, MaxStepMs);
    }

    /// <summary>Rotate towards lower slots instead of higher ones.</summary>
    public bool Reverse { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Number of slots in the range.</summary>
    public int Length => Last - First + 1;

    public ColorCycle Clone() => new()
    {
        Name = Name,
        First = First,
        Last = Last,
        StepMs = StepMs,
        Reverse = Reverse,
        Enabled = Enabled,
    };
}
