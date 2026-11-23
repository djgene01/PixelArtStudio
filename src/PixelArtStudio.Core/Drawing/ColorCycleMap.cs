using System.Numerics;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.Drawing;

/// <summary>
/// The colour remap of a document's colour cycles at one moment. A pixel whose colour equals a palette colour in
/// a cycle's slot range is shown as the colour currently rotated into that slot; every other colour is unchanged.
/// </summary>
public sealed class ColorCycleMap
{
    private const int MinTableSize = 1024;

    private readonly uint[] _keys;
    private readonly uint[] _values;
    private readonly int _shift;

    private ColorCycleMap(int capacity)
    {
        int size = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(MinTableSize, capacity * 2));
        _keys = new uint[size];
        _values = new uint[size];
        _shift = 32 - BitOperations.Log2((uint)size);
    }

    /// <summary>
    /// How many steps each cycle has rotated at <paramref name="elapsedMs"/>, in cycle order; -1 for a cycle that is
    /// disabled or does not fit the palette. Two times with equal steps give the same map.
    /// </summary>
    public static int[] StepsAt(Palette palette, IReadOnlyList<ColorCycle> cycles, long elapsedMs)
    {
        var steps = new int[cycles.Count];
        for (int i = 0; i < cycles.Count; i++)
        {
            var c = cycles[i];
            bool valid = c.Enabled && c.First >= 0 && c.Last < palette.Colors.Count && c.Length >= 2;
            steps[i] = valid ? (int)(Math.Max(0, elapsedMs) / c.StepMs % c.Length) : -1;
        }
        return steps;
    }

    /// <summary>The map at <paramref name="elapsedMs"/>, or null when no cycle has moved (nothing to remap).</summary>
    public static ColorCycleMap? Build(Palette palette, IReadOnlyList<ColorCycle> cycles, long elapsedMs) =>
        Build(palette, cycles, StepsAt(palette, cycles, elapsedMs));

    /// <summary>The map for the given per-cycle steps (from <see cref="StepsAt"/>), or null when every step is 0 or -1.</summary>
    public static ColorCycleMap? Build(Palette palette, IReadOnlyList<ColorCycle> cycles, int[] steps)
    {
        if (!steps.Any(s => s > 0)) return null;

        int capacity = 0;
        for (int i = 0; i < cycles.Count; i++)
            if (steps[i] >= 0) capacity += cycles[i].Length;

        var map = new ColorCycleMap(capacity);
        var colors = palette.Colors;
        for (int i = 0; i < cycles.Count; i++)
        {
            int s = steps[i];
            if (s < 0) continue;
            var c = cycles[i];
            int n = c.Length;
            for (int k = 0; k < n; k++)
            {
                int source = c.Reverse ? (k + s) % n : ((k - s) % n + n) % n;
                map.TryAdd(colors[c.First + k], colors[c.First + source]);
            }
        }
        return map;
    }

    /// <summary>The colour to show for <paramref name="argb"/>.</summary>
    public uint Map(uint argb)
    {
        if (argb >> 24 == 0) return argb;
        int mask = _keys.Length - 1;
        for (int i = Slot(argb); ; i = (i + 1) & mask)
        {
            uint key = _keys[i];
            if (key == argb) return _values[i];
            if (key == 0) return argb;
        }
    }

    /// <summary>Adds a mapping unless the colour already has one (the first cycle and slot win).</summary>
    private void TryAdd(uint key, uint value)
    {
        if (key >> 24 == 0) return;
        int mask = _keys.Length - 1;
        for (int i = Slot(key); ; i = (i + 1) & mask)
        {
            if (_keys[i] == key) return;
            if (_keys[i] != 0) continue;
            _keys[i] = key;
            _values[i] = value;
            return;
        }
    }

    private int Slot(uint key) => (int)((key * 2654435761u) >> _shift);
}
