using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PixelArtStudio.ViewModels;

/// <summary>One timeline cell. Its <see cref="Thumbnail"/> is the flattened frame, shrunk to at most 256 px (also used for the preview).</summary>
public sealed partial class FrameViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number))]
    private int _index;

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private int _durationMs;

    public int Number => Index + 1;
}
