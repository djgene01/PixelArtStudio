using System.Windows;
using System.Windows.Controls;
using PixelArtStudio.Core.IO;

namespace PixelArtStudio.Dialogs;

public partial class ExportDialog : Window
{
    private static readonly (ExportFormat Format, string Label, string Hint)[] Formats =
    [
        (ExportFormat.Png, "PNG image (current frame)", "Keeps full transparency."),
        (ExportFormat.Bmp, "BMP image (current frame)", "32-bit BMP with alpha. Some programs ignore BMP transparency."),
        (ExportFormat.PngSequence, "PNG sequence", "One file per frame: name_0001.png, name_0002.png, ..."),
        (ExportFormat.SpriteSheet, "Sprite sheet (PNG)", "All frames in a grid in one image."),
        (ExportFormat.Gif, "Animated GIF", "GIF supports at most 256 colours per frame and no partial transparency: pixels under 50% alpha become transparent, the rest opaque."),
        (ExportFormat.Apng, "Animated PNG (APNG)", "Full colour and transparency. Plays in modern browsers."),
        (ExportFormat.WebP, "Animated WebP (lossless)", "Full colour and transparency. Plays in modern browsers."),
    ];

    private static ExportFormat _lastFormat = ExportFormat.Png;
    private static int _lastScale = 1;

    private readonly int _frameCount;
    private readonly int _currentFrame;
    private readonly bool _hasCycles;

    /// <param name="hasCycles">The sprite has enabled colour cycles, so multi-frame formats can bake them.</param>
    public ExportDialog(int frameCount, int currentFrame, bool hasCycles)
    {
        (_frameCount, _currentFrame, _hasCycles) = (frameCount, currentFrame, hasCycles);
        InitializeComponent();

        foreach (var f in Formats) FormatBox.Items.Add(new ComboBoxItem { Content = f.Label, Tag = f.Format });
        foreach (int s in new[] { 1, 2, 3, 4, 5, 6, 8, 10, 12, 16 })
            ScaleBox.Items.Add(new ComboBoxItem { Content = $"{s * 100}%  ({s}x)", Tag = s });

        FormatBox.SelectedIndex = Array.FindIndex(Formats, f => f.Format == _lastFormat);
        ScaleBox.SelectedItem = ScaleBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == _lastScale) ?? ScaleBox.Items[0];
        FromBox.Text = "1";
        ToBox.Text = frameCount.ToString();
    }

    public ExportOptions? Options { get; private set; }

    private ExportFormat SelectedFormat => FormatBox.SelectedItem is ComboBoxItem { Tag: ExportFormat f } ? f : ExportFormat.Png;

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        var format = SelectedFormat;
        FormatHint.Text = Formats.First(f => f.Format == format).Hint;
        bool multiFrame = format is not (ExportFormat.Png or ExportFormat.Bmp);
        RangePanel.Visibility = multiFrame ? Visibility.Visible : Visibility.Collapsed;
        SheetPanel.Visibility = format == ExportFormat.SpriteSheet ? Visibility.Visible : Visibility.Collapsed;
        LoopBox.Visibility = ImageExporter.IsAnimated(format) ? Visibility.Visible : Visibility.Collapsed;
        BakePanel.Visibility = _hasCycles && multiFrame ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var format = SelectedFormat;
        var options = new ExportOptions
        {
            Format = format,
            Scale = ScaleBox.SelectedItem is ComboBoxItem { Tag: int s } ? s : 1,
            CurrentFrame = _currentFrame,
            Loop = LoopBox.IsChecked == true,
            WriteJson = JsonBox.IsChecked == true,
            BakeCycles = BakePanel.Visibility == Visibility.Visible && BakeBox.IsChecked == true,
        };

        if (RangePanel.Visibility == Visibility.Visible)
        {
            if (!DialogInput.TryReadInt(FromBox, 1, _frameCount, "From frame", out int from) ||
                !DialogInput.TryReadInt(ToBox, from, _frameCount, "To frame", out int to))
                return;
            (options.FirstFrame, options.LastFrame) = (from - 1, to - 1);
        }
        if (format == ExportFormat.SpriteSheet)
        {
            if (!DialogInput.TryReadInt(ColumnsBox, 0, 1000, "Columns", out int columns)) return;
            options.SheetColumns = columns;
        }

        (_lastFormat, _lastScale) = (format, options.Scale);
        Options = options;
        DialogResult = true;
    }
}
