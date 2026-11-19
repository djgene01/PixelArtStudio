using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.IO;

public static class SpriteSheet
{
    /// <summary>Lays frames out in a grid. <paramref name="columns"/> of 0 or less means a single row.</summary>
    public static PixelBuffer Build(IReadOnlyList<PixelBuffer> frames, int columns, out IReadOnlyList<RectI> frameRects)
    {
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));
        int w = frames[0].Width, h = frames[0].Height;
        int cols = columns <= 0 ? frames.Count : Math.Min(columns, frames.Count);
        int rows = (frames.Count + cols - 1) / cols;

        var sheet = new PixelBuffer(w * cols, h * rows);
        var rects = new List<RectI>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            var rect = new RectI(i % cols * w, i / cols * h, w, h);
            Selection.Paste(sheet, frames[i], rect.X, rect.Y, skipTransparent: false);
            rects.Add(rect);
        }
        frameRects = rects;
        return sheet;
    }

    /// <summary>Cuts a sheet into frames, row by row. Partial cells at the right and bottom edges are ignored.</summary>
    /// <param name="skipEmpty">Drop fully transparent cells (at least one frame is always kept).</param>
    public static SpriteDocument Slice(PixelBuffer sheet, int frameWidth, int frameHeight, bool skipEmpty = true, int durationMs = Frame.DefaultDurationMs)
    {
        SpriteDocument.ValidateSize(frameWidth, frameHeight);
        int cols = sheet.Width / frameWidth, rows = sheet.Height / frameHeight;
        if (cols == 0 || rows == 0)
            throw new ArgumentException($"The image ({sheet.Width}x{sheet.Height}) is smaller than one {frameWidth}x{frameHeight} frame.");

        var frames = new List<(PixelBuffer, int)>();
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var cell = Selection.Extract(sheet, new RectI(c * frameWidth, r * frameHeight, frameWidth, frameHeight));
            if (skipEmpty && cell.IsTransparent()) continue;
            frames.Add((cell, durationMs));
        }
        if (frames.Count == 0) frames.Add((new PixelBuffer(frameWidth, frameHeight), durationMs));
        return SpriteDocument.FromFrames(frames);
    }
}
