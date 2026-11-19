using PixelArtStudio.Core.Drawing;

namespace PixelArtStudio.Core.Model;

public enum Anchor
{
    TopLeft, Top, TopRight,
    Left, Center, Right,
    BottomLeft, Bottom, BottomRight,
}

/// <summary>
/// A sprite: layers shared by all frames, and frames holding one cel per layer.
/// Layer index 0 is the bottom layer. There is always at least one layer and one frame.
/// </summary>
public sealed class SpriteDocument
{
    public const int MaxSize = 1024;

    private SpriteDocument(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public List<Layer> Layers { get; } = [];
    public List<Frame> Frames { get; } = [];
    public Palette Palette { get; set; } = Palette.CreateDefault();

    public static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSize || height > MaxSize)
            throw new ArgumentOutOfRangeException(nameof(width), $"Canvas size must be between 1 and {MaxSize} per side (got {width}x{height}).");
    }

    /// <summary>A blank document with one layer and one frame.</summary>
    public static SpriteDocument Create(int width, int height)
    {
        var doc = new SpriteDocument(width, height);
        doc.Layers.Add(new Layer("Layer 1"));
        doc.Frames.Add(new Frame { Cels = { doc.NewCel() } });
        return doc;
    }

    /// <summary>A single-layer document with one frame per image. All images must be the same size.</summary>
    public static SpriteDocument FromFrames(IReadOnlyList<(PixelBuffer Pixels, int DurationMs)> frames, string layerName = "Layer 1")
    {
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));
        var doc = new SpriteDocument(frames[0].Pixels.Width, frames[0].Pixels.Height);
        doc.Layers.Add(new Layer(layerName));
        foreach (var (pixels, duration) in frames)
        {
            if (pixels.Width != doc.Width || pixels.Height != doc.Height)
                throw new ArgumentException("All frames must be the same size.", nameof(frames));
            doc.Frames.Add(new Frame { DurationMs = duration, Cels = { pixels } });
        }
        return doc;
    }

    /// <summary>A document with the given layers and no frames, for loaders that add frames themselves.</summary>
    internal static SpriteDocument CreateForLoad(int width, int height, IEnumerable<Layer> layers)
    {
        var doc = new SpriteDocument(width, height);
        doc.Layers.AddRange(layers);
        return doc;
    }

    public PixelBuffer NewCel() => new(Width, Height);

    public PixelBuffer GetCel(int frameIndex, int layerIndex) => Frames[frameIndex].Cels[layerIndex];

    // ---- Frames ----

    /// <summary>Inserts a blank frame after <paramref name="afterIndex"/> and returns its index.</summary>
    public int AddFrame(int afterIndex)
    {
        int index = Math.Clamp(afterIndex + 1, 0, Frames.Count);
        var frame = new Frame();
        if (Frames.Count > 0) frame.DurationMs = Frames[Math.Clamp(afterIndex, 0, Frames.Count - 1)].DurationMs;
        foreach (var _ in Layers) frame.Cels.Add(NewCel());
        Frames.Insert(index, frame);
        return index;
    }

    public int DuplicateFrame(int index)
    {
        Frames.Insert(index + 1, Frames[index].Clone());
        return index + 1;
    }

    /// <summary>Deletes a frame. Refused (returns false) for the last remaining frame.</summary>
    public bool DeleteFrame(int index)
    {
        if (Frames.Count <= 1 || (uint)index >= (uint)Frames.Count) return false;
        Frames.RemoveAt(index);
        return true;
    }

    public void MoveFrame(int from, int to)
    {
        to = Math.Clamp(to, 0, Frames.Count - 1);
        if (from == to) return;
        var frame = Frames[from];
        Frames.RemoveAt(from);
        Frames.Insert(to, frame);
    }

    // ---- Layers ----

    /// <summary>Inserts a blank layer above <paramref name="belowIndex"/> and returns its index.</summary>
    public int AddLayer(int belowIndex, string? name = null)
    {
        int index = Math.Clamp(belowIndex + 1, 0, Layers.Count);
        Layers.Insert(index, new Layer(name ?? NextLayerName()));
        foreach (var frame in Frames) frame.Cels.Insert(index, NewCel());
        return index;
    }

    public int DuplicateLayer(int index)
    {
        var copy = Layers[index].Clone();
        copy.Name += " copy";
        Layers.Insert(index + 1, copy);
        foreach (var frame in Frames) frame.Cels.Insert(index + 1, frame.Cels[index].Clone());
        return index + 1;
    }

    /// <summary>Deletes a layer. Refused (returns false) for the last remaining layer.</summary>
    public bool DeleteLayer(int index)
    {
        if (Layers.Count <= 1 || (uint)index >= (uint)Layers.Count) return false;
        Layers.RemoveAt(index);
        foreach (var frame in Frames) frame.Cels.RemoveAt(index);
        return true;
    }

    public void MoveLayer(int from, int to)
    {
        to = Math.Clamp(to, 0, Layers.Count - 1);
        if (from == to) return;
        var layer = Layers[from];
        Layers.RemoveAt(from);
        Layers.Insert(to, layer);
        foreach (var frame in Frames)
        {
            var cel = frame.Cels[from];
            frame.Cels.RemoveAt(from);
            frame.Cels.Insert(to, cel);
        }
    }

    /// <summary>Blends layer <paramref name="index"/> onto the layer below and removes it.</summary>
    public bool MergeDown(int index)
    {
        if (index <= 0 || index >= Layers.Count) return false;
        var upper = Layers[index];
        if (upper.Visible)
        {
            foreach (var frame in Frames)
                Compositor.BlendOnto(frame.Cels[index - 1], frame.Cels[index], upper.Opacity);
        }
        Layers.RemoveAt(index);
        foreach (var frame in Frames) frame.Cels.RemoveAt(index);
        return true;
    }

    // ---- Whole-image transforms ----

    /// <summary>Crops or pads every cel to the new size, keeping content at <paramref name="anchor"/>.</summary>
    public void Resize(int width, int height, Anchor anchor)
    {
        ValidateSize(width, height);
        int column = (int)anchor % 3, row = (int)anchor / 3;
        int dx = column switch { 0 => 0, 1 => (width - Width) / 2, _ => width - Width };
        int dy = row switch { 0 => 0, 1 => (height - Height) / 2, _ => height - Height };

        foreach (var frame in Frames)
        for (int i = 0; i < frame.Cels.Count; i++)
        {
            var resized = new PixelBuffer(width, height);
            Selection.Paste(resized, frame.Cels[i], dx, dy, skipTransparent: false);
            frame.Cels[i] = resized;
        }
        Width = width;
        Height = height;
    }

    public void FlipHorizontal()
    {
        foreach (var cel in AllCels()) cel.FlipHorizontal();
    }

    public void FlipVertical()
    {
        foreach (var cel in AllCels()) cel.FlipVertical();
    }

    public void Rotate90(bool clockwise)
    {
        foreach (var frame in Frames)
        for (int i = 0; i < frame.Cels.Count; i++)
            frame.Cels[i] = frame.Cels[i].Rotated90(clockwise);
        (Width, Height) = (Height, Width);
    }

    public SpriteDocument Clone()
    {
        var copy = new SpriteDocument(Width, Height) { Palette = Palette.Clone() };
        copy.Layers.AddRange(Layers.Select(l => l.Clone()));
        copy.Frames.AddRange(Frames.Select(f => f.Clone()));
        return copy;
    }

    /// <summary>Replaces this document's contents with a deep copy of <paramref name="source"/>.</summary>
    public void CopyStateFrom(SpriteDocument source)
    {
        var copy = source.Clone();
        Width = copy.Width;
        Height = copy.Height;
        Palette = copy.Palette;
        Layers.Clear();
        Layers.AddRange(copy.Layers);
        Frames.Clear();
        Frames.AddRange(copy.Frames);
    }

    private IEnumerable<PixelBuffer> AllCels() => Frames.SelectMany(f => f.Cels);

    private string NextLayerName()
    {
        int n = Layers.Count + 1;
        while (Layers.Any(l => l.Name == $"Layer {n}")) n++;
        return $"Layer {n}";
    }
}
