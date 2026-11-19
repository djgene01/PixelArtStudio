using PixelArtStudio.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelArtStudio.Core.IO;

/// <summary>Reads PNG, BMP, GIF and WebP. Animated files become one frame per image frame.</summary>
public static class ImageImporter
{
    /// <summary>Largest image accepted as a sprite sheet source.</summary>
    public const int MaxSheetSize = 16384;

    public static readonly string[] Extensions = [".png", ".bmp", ".gif", ".webp"];

    /// <exception cref="InvalidDataException">The file is not a supported image, or too large for a canvas.</exception>
    public static SpriteDocument LoadDocument(string path)
    {
        using var image = LoadImage(path);
        if (image.Width > SpriteDocument.MaxSize || image.Height > SpriteDocument.MaxSize)
            throw new InvalidDataException(
                $"The image is {image.Width}x{image.Height}; the largest canvas is {SpriteDocument.MaxSize}x{SpriteDocument.MaxSize}. " +
                "If it is a sprite sheet, use Import Sprite Sheet instead.");

        var format = image.Metadata.DecodedImageFormat;
        var frames = image.Frames
            .Select(f => (ImageConvert.FromFrame(f), ReadDelayMs(f.Metadata, format)))
            .ToList();
        return SpriteDocument.FromFrames(frames, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Loads the first frame of an image file.</summary>
    public static PixelBuffer LoadPixels(string path)
    {
        using var image = LoadImage(path);
        if (image.Width > MaxSheetSize || image.Height > MaxSheetSize)
            throw new InvalidDataException($"The image is {image.Width}x{image.Height}; the limit is {MaxSheetSize} per side.");
        return ImageConvert.FromFrame(image.Frames.RootFrame);
    }

    /// <summary>Loads the first frame of an encoded image, e.g. PNG data from the clipboard.</summary>
    public static PixelBuffer LoadPixels(Stream stream)
    {
        try
        {
            using var image = Image.Load<Bgra32>(stream);
            return ImageConvert.FromFrame(image.Frames.RootFrame);
        }
        catch (Exception ex) when (ex is ImageFormatException or UnknownImageFormatException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static Image<Bgra32> LoadImage(string path)
    {
        try
        {
            return Image.Load<Bgra32>(path);
        }
        catch (Exception ex) when (ex is ImageFormatException or UnknownImageFormatException)
        {
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a supported image: {ex.Message}", ex);
        }
    }

    private static int ReadDelayMs(ImageFrameMetadata metadata, SixLabors.ImageSharp.Formats.IImageFormat? format)
    {
        int ms = 0;
        if (format is GifFormat && metadata.TryGetGifMetadata(out var gif))
            ms = gif.FrameDelay < 2 ? 0 : gif.FrameDelay * 10;   // browsers treat 0-1 cs as "default"
        else if (format is PngFormat && metadata.TryGetPngMetadata(out var png) && png.FrameDelay.Denominator != 0)
            ms = (int)Math.Round(png.FrameDelay.ToDouble() * 1000);
        else if (format is WebpFormat && metadata.TryGetWebpFrameMetadata(out var webp))
            ms = (int)webp.FrameDelay;

        return ms > 0 ? ms : Frame.DefaultDurationMs;
    }
}
