using System.Reflection;
using System.Windows;

namespace PixelArtStudio;

/// <summary>Shown while the main window loads, with the Pixel the Pixie mascot.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version == null ? "Starting..." : $"Version {version.ToString(3)}  -  starting...";
    }
}
