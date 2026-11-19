using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Services;

/// <summary>
/// Converts between <see cref="PixelBuffer"/> and WPF bitmaps. <see cref="PixelFormats.Bgra32"/> is straight
/// alpha and has the same memory layout as a <c>0xAARRGGBB</c> uint, so no per-pixel conversion is needed.
/// </summary>
public static class BitmapHelper
{
    public static BitmapSource ToBitmapSource(PixelBuffer buffer)
    {
        var bitmap = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32, null, buffer.Pixels, buffer.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static PixelBuffer FromBitmapSource(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var buffer = new PixelBuffer(converted.PixelWidth, converted.PixelHeight);
        converted.CopyPixels(buffer.Pixels, buffer.Width * 4, 0);
        return buffer;
    }

    public static Color ToColor(uint argb) =>
        Color.FromArgb(ColorUtil.A(argb), ColorUtil.R(argb), ColorUtil.G(argb), ColorUtil.B(argb));

    public static SolidColorBrush ToBrush(uint argb)
    {
        var brush = new SolidColorBrush(ToColor(argb));
        brush.Freeze();
        return brush;
    }
}
