using System.Windows;
using Microsoft.Win32;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Dialogs;

namespace PixelArtStudio.Services;

/// <summary>File pickers, message boxes and the app's own dialogs, owned by the main window.</summary>
public sealed class DialogService
{
    public const string ProjectFilter = "PixelArtStudio sprite (*.pxs)|*.pxs";
    public const string ImageFilter = "Images (*.png;*.gif;*.webp;*.bmp)|*.png;*.gif;*.webp;*.bmp";
    public const string OpenFilter = "Sprites and images (*.pxs;*.png;*.gif;*.webp;*.bmp)|*.pxs;*.png;*.gif;*.webp;*.bmp|" + ProjectFilter + "|" + ImageFilter;
    public const string PaletteFilter = "Palettes (*.hex;*.gpl)|*.hex;*.gpl|Hex palette (*.hex)|*.hex|GIMP palette (*.gpl)|*.gpl";

    private static Window? Owner => Application.Current?.MainWindow;

    public string? OpenFile(string filter, string title)
    {
        var dialog = new OpenFileDialog { Filter = filter, Title = title };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string filter, string title, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, Title = title, FileName = fileName, OverwritePrompt = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public MessageBoxResult Ask(string message, string title, MessageBoxButton buttons) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, MessageBoxImage.Question)
            : MessageBox.Show(message, title, buttons, MessageBoxImage.Question);

    public void ShowError(string message, string title = "PixelArtStudio")
    {
        if (Owner is { } owner) MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public (int Width, int Height)? AskNewDocument(int width, int height)
    {
        var dialog = new NewDocumentDialog(width, height) { Owner = Owner };
        return dialog.ShowDialog() == true ? (dialog.SpriteWidth, dialog.SpriteHeight) : null;
    }

    public (int Width, int Height, Anchor Anchor)? AskResize(int width, int height)
    {
        var dialog = new ResizeCanvasDialog(width, height) { Owner = Owner };
        return dialog.ShowDialog() == true ? (dialog.NewWidth, dialog.NewHeight, dialog.Anchor) : null;
    }

    public (int FrameWidth, int FrameHeight, int DurationMs, bool SkipEmpty)? AskSheetSlice(int sheetWidth, int sheetHeight)
    {
        var dialog = new ImportSheetDialog(sheetWidth, sheetHeight) { Owner = Owner };
        return dialog.ShowDialog() == true ? (dialog.FrameWidth, dialog.FrameHeight, dialog.DurationMs, dialog.SkipEmpty) : null;
    }

    public ExportOptions? AskExport(int frameCount, int currentFrame)
    {
        var dialog = new ExportDialog(frameCount, currentFrame) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Options : null;
    }
}
