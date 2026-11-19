using System.Text.Json;
using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelArtStudio.Core.IO;

public enum ExportFormat
{
    Png,
    Bmp,
    PngSequence,
    SpriteSheet,
    Gif,
    Apng,
    WebP,
}

public sealed class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.Png;

    /// <summary>Integer nearest-neighbour upscale, 1 to 16.</summary>
    public int Scale { get; set; } = 1;

    /// <summary>Frame used by the single-image formats (PNG, BMP).</summary>
    public int CurrentFrame { get; set; }

    /// <summary>First frame of the range for multi-frame formats.</summary>
    public int FirstFrame { get; set; }

    /// <summary>Last frame of the range (inclusive); -1 means the last frame of the document.</summary>
    public int LastFrame { get; set; } = -1;

    public bool Loop { get; set; } = true;

    /// <summary>Sprite sheet columns; 0 means a single row.</summary>
    public int SheetColumns { get; set; }

    /// <summary>Write a JSON file next to the sprite sheet with frame rectangles and durations.</summary>
    public bool WriteJson { get; set; }
}

public static class ImageExporter
{
    public const int MaxScale = 16;

    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Bmp => ".bmp",
        ExportFormat.Gif => ".gif",
        ExportFormat.WebP => ".webp",
        _ => ".png",
    };

    public static bool IsAnimated(ExportFormat format) => format is ExportFormat.Gif or ExportFormat.Apng or ExportFormat.WebP;

    /// <summary>Exports the document and returns the paths of all files written.</summary>
    public static IReadOnlyList<string> Export(SpriteDocument doc, string path, ExportOptions options)
    {
        int scale = Math.Clamp(options.Scale, 1, MaxScale);
        PixelBuffer Render(int frame) => Compositor.Scale(Compositor.Flatten(doc, frame), scale);

        int last = options.LastFrame < 0 ? doc.Frames.Count - 1 : Math.Min(options.LastFrame, doc.Frames.Count - 1);
        int first = Math.Clamp(options.FirstFrame, 0, last);
        var range = Enumerable.Range(first, last - first + 1).ToList();

        switch (options.Format)
        {
            case ExportFormat.Png:
            case ExportFormat.Bmp:
            {
                int frame = Math.Clamp(options.CurrentFrame, 0, doc.Frames.Count - 1);
                using var image = ImageConvert.ToImage(Render(frame));
                image.Save(path, EncoderFor(options.Format));
                return [path];
            }

            case ExportFormat.PngSequence:
            {
                string dir = Path.GetDirectoryName(path) ?? ".";
                string name = Path.GetFileNameWithoutExtension(path);
                var written = new List<string>();
                for (int i = 0; i < range.Count; i++)
                {
                    string file = Path.Combine(dir, $"{name}_{i + 1:D4}.png");
                    using var image = ImageConvert.ToImage(Render(range[i]));
                    image.Save(file, PngEncoder);
                    written.Add(file);
                }
                return written;
            }

            case ExportFormat.SpriteSheet:
            {
                var frames = range.Select(Render).ToList();
                var sheet = SpriteSheet.Build(frames, options.SheetColumns, out var rects);
                using (var image = ImageConvert.ToImage(sheet))
                    image.Save(path, PngEncoder);
                if (!options.WriteJson) return [path];

                string jsonPath = Path.ChangeExtension(path, ".json");
                WriteSheetJson(jsonPath, Path.GetFileName(path), sheet, rects, range.Select(f => doc.Frames[f].DurationMs).ToList());
                return [path, jsonPath];
            }

            case ExportFormat.Gif:
            case ExportFormat.Apng:
            case ExportFormat.WebP:
            {
                var frames = range.Select(f => (Pixels: Render(f), doc.Frames[f].DurationMs)).ToList();
                using var image = BuildAnimation(frames, options.Format, options.Loop);
                image.Save(path, EncoderFor(options.Format));
                return [path];
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unknown export format.");
        }
    }

    private static readonly PngEncoder PngEncoder = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        TransparentColorMode = PngTransparentColorMode.Preserve,
    };

    private static readonly BmpEncoder BmpEncoder = new()
    {
        BitsPerPixel = BmpBitsPerPixel.Pixel32,
        SupportTransparency = true,
    };

    private static readonly GifEncoder GifEncoder = new() { ColorTableMode = GifColorTableMode.Local };

    private static readonly WebpEncoder WebpEncoder = new() { FileFormat = WebpFileFormatType.Lossless };

    private static Image<Bgra32> BuildAnimation(IReadOnlyList<(PixelBuffer Pixels, int DurationMs)> frames, ExportFormat format, bool loop)
    {
        // GIF has 1-bit transparency: threshold alpha at 128.
        PixelBuffer Prepare(PixelBuffer p) => format == ExportFormat.Gif ? ThresholdAlpha(p) : p;

        var image = ImageConvert.ToImage(Prepare(frames[0].Pixels));
        try
        {
            SetFrameMetadata(image.Frames.RootFrame.Metadata, format, frames[0].DurationMs);
            for (int i = 1; i < frames.Count; i++)
            {
                using var next = ImageConvert.ToImage(Prepare(frames[i].Pixels));
                var added = image.Frames.AddFrame(next.Frames.RootFrame);
                SetFrameMetadata(added.Metadata, format, frames[i].DurationMs);
            }

            switch (format)
            {
                case ExportFormat.Gif:
                    image.Metadata.GetGifMetadata().RepeatCount = (ushort)(loop ? 0 : 1);
                    break;
                case ExportFormat.Apng:
                    var png = image.Metadata.GetPngMetadata();
                    png.RepeatCount = loop ? 0u : 1u;
                    png.ColorType = PngColorType.RgbWithAlpha;
                    png.BitDepth = PngBitDepth.Bit8;
                    png.AnimateRootFrame = true;
                    break;
                case ExportFormat.WebP:
                    var webp = image.Metadata.GetWebpMetadata();
                    webp.RepeatCount = (ushort)(loop ? 0 : 1);
                    webp.FileFormat = WebpFileFormatType.Lossless;
                    break;
            }
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void SetFrameMetadata(ImageFrameMetadata metadata, ExportFormat format, int durationMs)
    {
        switch (format)
        {
            case ExportFormat.Gif:
                var gif = metadata.GetGifMetadata();
                gif.FrameDelay = Math.Max(2, durationMs / 10);
                gif.DisposalMethod = GifDisposalMethod.RestoreToBackground;
                break;
            case ExportFormat.Apng:
                var png = metadata.GetPngMetadata();
                png.FrameDelay = new Rational((uint)durationMs, 1000);
                png.DisposalMethod = PngDisposalMethod.DoNotDispose;
                png.BlendMethod = PngBlendMethod.Source;
                break;
            case ExportFormat.WebP:
                var webp = metadata.GetWebpMetadata();
                webp.FrameDelay = (uint)durationMs;
                webp.DisposalMethod = WebpDisposalMethod.DoNotDispose;
                webp.BlendMethod = WebpBlendMethod.Source;
                break;
        }
    }

    private static PixelBuffer ThresholdAlpha(PixelBuffer source)
    {
        var result = source.Clone();
        var px = result.Pixels;
        for (int i = 0; i < px.Length; i++)
            px[i] = px[i] >> 24 < 128 ? 0u : px[i] | 0xFF000000;
        return result;
    }

    private static void WriteSheetJson(string path, string imageName, PixelBuffer sheet, IReadOnlyList<RectI> rects, IReadOnlyList<int> durations)
    {
        var data = new
        {
            image = imageName,
            size = new { w = sheet.Width, h = sheet.Height },
            frames = rects.Select((r, i) => new { index = i, x = r.X, y = r.Y, w = r.Width, h = r.Height, durationMs = durations[i] }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IImageEncoder EncoderFor(ExportFormat format) => format switch
    {
        ExportFormat.Gif => GifEncoder,
        ExportFormat.WebP => WebpEncoder,
        ExportFormat.Bmp => BmpEncoder,
        _ => PngEncoder,
    };
}
