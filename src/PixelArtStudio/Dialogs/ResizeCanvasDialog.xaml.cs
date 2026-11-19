using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Dialogs;

public partial class ResizeCanvasDialog : Window
{
    public ResizeCanvasDialog(int width, int height)
    {
        InitializeComponent();
        WidthBox.Text = width.ToString();
        HeightBox.Text = height.ToString();

        foreach (Anchor anchor in Enum.GetValues<Anchor>())
        {
            var button = new RadioButton
            {
                Tag = anchor,
                GroupName = "Anchor",
                IsChecked = anchor == Anchor.Center,
                ToolTip = anchor.ToString(),
                Margin = new Thickness(2),
                Style = TryFindResource(typeof(ToggleButton)) as Style,
            };
            AnchorGrid.Children.Add(button);
        }
        Loaded += (_, _) => { WidthBox.Focus(); WidthBox.SelectAll(); };
    }

    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }

    public Anchor Anchor =>
        AnchorGrid.Children.OfType<RadioButton>().FirstOrDefault(b => b.IsChecked == true)?.Tag as Anchor? ?? Anchor.Center;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!DialogInput.TryReadInt(WidthBox, 1, SpriteDocument.MaxSize, "Width", out int w) ||
            !DialogInput.TryReadInt(HeightBox, 1, SpriteDocument.MaxSize, "Height", out int h))
            return;
        (NewWidth, NewHeight) = (w, h);
        DialogResult = true;
    }
}
