namespace PixelArtStudio.Core.Model;

/// <summary>Layer properties shared by every frame. Pixels live in <see cref="Frame.Cels"/>.</summary>
public sealed class Layer
{
    private double _opacity = 1.0;

    public Layer(string name) => Name = name;

    public string Name { get; set; }
    public bool Visible { get; set; } = true;

    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 0.0, 1.0);
    }

    public Layer Clone() => new(Name) { Visible = Visible, Opacity = Opacity };
}
