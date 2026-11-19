using System.Windows;
using System.Windows.Controls;

namespace PixelArtStudio.Dialogs;

public partial class NewDocumentDialog : Window
{
    public NewDocumentDialog(int width, int height)
    {
        InitializeComponent();
        WidthBox.Text = width.ToString();
        HeightBox.Text = height.ToString();
        Loaded += (_, _) => { WidthBox.Focus(); WidthBox.SelectAll(); };
    }

    public int SpriteWidth { get; private set; }
    public int SpriteHeight { get; private set; }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is ComboBoxItem { Tag: string size })
            WidthBox.Text = HeightBox.Text = size;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!DialogInput.TryReadInt(WidthBox, 1, 1024, "Width", out int w) ||
            !DialogInput.TryReadInt(HeightBox, 1, 1024, "Height", out int h))
            return;
        (SpriteWidth, SpriteHeight) = (w, h);
        DialogResult = true;
    }
}
