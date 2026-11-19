using System.Runtime.InteropServices;
using PixelArtStudio.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelArtStudio.Core.IO;

/// <summary>
/// Converts between <see cref="PixelBuffer"/> and ImageSharp. A little-endian <c>0xAARRGGBB</c> uint has
/// the same byte layout as <see cref="Bgra32"/>, so pixels are copied without per-channel work.
/// </summary>
internal static class ImageConvert
{
    public static Image<Bgra32> ToImage(PixelBuffer buffer) =>
        Image.LoadPixelData<Bgra32>(MemoryMarshal.Cast<uint, Bgra32>(buffer.Pixels.AsSpan()), buffer.Width, buffer.Height);

    public static PixelBuffer FromFrame(ImageFrame<Bgra32> frame)
    {
        var buffer = new PixelBuffer(frame.Width, frame.Height);
        frame.CopyPixelDataTo(MemoryMarshal.Cast<uint, Bgra32>(buffer.Pixels.AsSpan()));
        return buffer;
    }
}
