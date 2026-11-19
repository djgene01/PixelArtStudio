using CommunityToolkit.Mvvm.ComponentModel;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.ViewModels;

/// <summary>A row in the layer list. Edits go through the main view model so they are undoable.</summary>
public sealed class LayerViewModel(MainViewModel owner) : ObservableObject
{
    private string _name = "";
    private bool _visible = true;
    private int _opacityPercent = 100;

    public int Index { get; private set; }

    public string Name
    {
        get => _name;
        set
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0 || trimmed == _name) { OnPropertyChanged(); return; }
            _name = trimmed;
            owner.SetLayerName(Index, trimmed);
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (!SetProperty(ref _visible, value)) return;
            owner.SetLayerVisible(Index, value);
        }
    }

    public int OpacityPercent => _opacityPercent;

    internal void Sync(int index, Layer layer)
    {
        Index = index;
        _name = layer.Name;
        _visible = layer.Visible;
        _opacityPercent = (int)Math.Round(layer.Opacity * 100);
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>A palette colour.</summary>
public sealed class SwatchViewModel(uint color)
{
    public uint Color { get; } = color;
    public System.Windows.Media.Brush Brush { get; } = Services.BitmapHelper.ToBrush(color);
    public string Hex => ColorUtil.ToHex(Color);
}
