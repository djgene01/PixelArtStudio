using System.Windows;
using System.Windows.Controls;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Dialogs;

public partial class NewDocumentDialog : Window
{
    public NewDocumentDialog(int width, int height)
    {
        InitializeComponent();
        LimitHint.Text = $"1 to {SpriteDocument.MaxSize} pixels per side.";
        WidthBox.Text = width.ToString();
        HeightBox.Text = height.ToString();
        Loaded += (_, _) => { WidthBox.Focus(); WidthBox.SelectAll(); };
    }

    public int SpriteWidth { get; private set; }
    public int SpriteHeight { get; private set; }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ComboBoxItem { Tag: string size }) return;
        var parts = size.Split('x');
        WidthBox.Text = parts[0];
        HeightBox.Text = parts[1];
    }

    /// <summary>Shows how much memory each cel (one layer of one frame) will take.</summary>
    private void OnSizeChanged(object sender, TextChangedEventArgs e)
    {
        if (MemoryHint == null) return;
        if (!int.TryParse(WidthBox.Text.Trim(), out int w) || !int.TryParse(HeightBox.Text.Trim(), out int h) ||
            w < 1 || h < 1 || w > SpriteDocument.MaxSize || h > SpriteDocument.MaxSize)
        {
            MemoryHint.Text = "";
            return;
        }
        double megabytes = (double)w * h * 4 / (1024 * 1024);
        MemoryHint.Text = megabytes < 1
            ? $"About {w * h * 4 / 1024.0:0} KB per layer per frame."
            : $"About {megabytes:0.#} MB per layer per frame.";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!DialogInput.TryReadInt(WidthBox, 1, SpriteDocument.MaxSize, "Width", out int w) ||
            !DialogInput.TryReadInt(HeightBox, 1, SpriteDocument.MaxSize, "Height", out int h))
            return;
        (SpriteWidth, SpriteHeight) = (w, h);
        DialogResult = true;
    }
}
