using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Services;

/// <summary>
/// Copies images to and from the Windows clipboard. The "PNG" format keeps alpha for apps that read it;
/// the standard bitmap format is added for everything else.
/// </summary>
public static class ClipboardService
{
    private const string PngFormat = "PNG";
    private const int MaxPasteSize = 4096;

    public static bool TrySetImage(PixelBuffer pixels)
    {
        try
        {
            var bitmap = BitmapHelper.ToBitmapSource(pixels);
            var data = new DataObject();
            data.SetImage(bitmap);

            var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(png);
            data.SetData(PngFormat, png, false);

            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return false;   // clipboard is locked by another process
        }
    }

    public static PixelBuffer? TryGetImage()
    {
        try
        {
            PixelBuffer? result = null;
            if (Clipboard.ContainsData(PngFormat) && Clipboard.GetData(PngFormat) is MemoryStream png)
                result = ImageImporter.LoadPixels(png);
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                result = BitmapHelper.FromBitmapSource(image);

            return result != null && result.Width <= MaxPasteSize && result.Height <= MaxPasteSize ? result : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }
}
