using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Services;

namespace PixelArtStudio.ViewModels;

/// <summary>A palette colour at one slot of the palette.</summary>
public sealed partial class SwatchViewModel(int index, uint color) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The slot belongs to at least one colour cycle.</summary>
    [ObservableProperty]
    private bool _inCycle;

    public int Index { get; } = index;
    public uint Color { get; } = color;
    public Brush Brush { get; } = BitmapHelper.ToBrush(color);
    public string Hex => ColorUtil.ToHex(Color);
    public string ToolTip => $"{Hex}  (slot {Index})\nClick: primary colour. Shift+click: select a range. Right-click: secondary colour.";
}
