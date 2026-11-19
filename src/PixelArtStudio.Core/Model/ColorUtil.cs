using System.Globalization;

namespace PixelArtStudio.Core.Model;

/// <summary>Helpers for colours stored as straight-alpha <c>0xAARRGGBB</c>.</summary>
public static class ColorUtil
{
    public const uint Transparent = 0;

    public static uint FromArgb(byte a, byte r, byte g, byte b) => (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | b;
    public static uint FromRgb(byte r, byte g, byte b) => FromArgb(255, r, g, b);

    public static byte A(uint c) => (byte)(c >> 24);
    public static byte R(uint c) => (byte)(c >> 16);
    public static byte G(uint c) => (byte)(c >> 8);
    public static byte B(uint c) => (byte)c;

    /// <summary>Two colours match if they are equal or both fully transparent.</summary>
    public static bool Same(uint a, uint b) => a == b || (a >> 24 == 0 && b >> 24 == 0);

    /// <summary>Formats as <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when the colour is not opaque.</summary>
    public static string ToHex(uint c)
    {
        string rgb = $"#{R(c):X2}{G(c):X2}{B(c):X2}";
        return A(c) == 255 ? rgb : rgb + A(c).ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>Parses <c>RRGGBB</c> or <c>RRGGBBAA</c>, with or without a leading <c>#</c>.</summary>
    public static bool TryParseHex(string? text, out uint color)
    {
        color = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().TrimStart('#');
        if ((s.Length != 6 && s.Length != 8) ||
            !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            return false;

        color = s.Length == 6 ? 0xFF000000 | value : (value >> 8) | (value & 0xFF) << 24;
        return true;
    }
}
