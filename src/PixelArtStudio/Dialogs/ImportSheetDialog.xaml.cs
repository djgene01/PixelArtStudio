using System.Windows;
using System.Windows.Controls;
using PixelArtStudio.Core.Model;
using Frame = PixelArtStudio.Core.Model.Frame;

namespace PixelArtStudio.Dialogs;

public partial class ImportSheetDialog : Window
{
    private readonly int _sheetWidth, _sheetHeight;

    public ImportSheetDialog(int sheetWidth, int sheetHeight)
    {
        (_sheetWidth, _sheetHeight) = (sheetWidth, sheetHeight);
        InitializeComponent();
        SheetInfo.Text = $"Sheet size: {sheetWidth} x {sheetHeight} px";

        // Guess square frames the size of the shorter side, which fits a single-row strip.
        int guess = Math.Min(Math.Min(sheetWidth, sheetHeight), SpriteDocument.MaxSize);
        WidthBox.Text = HeightBox.Text = guess.ToString();
        Loaded += (_, _) => { WidthBox.Focus(); WidthBox.SelectAll(); };
    }

    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }
    public int DurationMs { get; private set; }
    public bool SkipEmpty => SkipEmptyBox.IsChecked == true;

    private void OnSizeChanged(object sender, TextChangedEventArgs e)
    {
        if (GridInfo == null) return;
        GridInfo.Text = int.TryParse(WidthBox.Text, out int w) && int.TryParse(HeightBox.Text, out int h) && w > 0 && h > 0
            ? $"{_sheetWidth / w} columns x {_sheetHeight / h} rows"
            : "";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!DialogInput.TryReadInt(WidthBox, 1, Math.Min(_sheetWidth, SpriteDocument.MaxSize), "Frame width", out int w) ||
            !DialogInput.TryReadInt(HeightBox, 1, Math.Min(_sheetHeight, SpriteDocument.MaxSize), "Frame height", out int h) ||
            !DialogInput.TryReadInt(DurationBox, Frame.MinDurationMs, Frame.MaxDurationMs, "Frame duration", out int ms))
            return;
        (FrameWidth, FrameHeight, DurationMs) = (w, h, ms);
        DialogResult = true;
    }
}
