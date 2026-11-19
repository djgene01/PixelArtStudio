using System.Globalization;
using System.Text;

namespace PixelArtStudio.Core.Model;

/// <summary>An ordered list of opaque colours.</summary>
public sealed class Palette
{
    private static readonly string[] Pico8 =
    [
        "000000", "1D2B53", "7E2553", "008751", "AB5236", "5F574F", "C2C3C7", "FFF1E8",
        "FF004D", "FFA300", "FFEC27", "00E436", "29ADFF", "83769C", "FF77A8", "FFCCAA",
    ];

    private static readonly string[] Db32 =
    [
        "000000", "222034", "45283C", "663931", "8F563B", "DF7126", "D9A066", "EEC39A",
        "FBF236", "99E550", "6ABE30", "37946E", "4B692F", "524B24", "323C39", "3F3F74",
        "306082", "5B6EE1", "639BFF", "5FCDE4", "CBDBFC", "FFFFFF", "9BADB7", "847E87",
        "696A6A", "595652", "76428A", "AC3232", "D95763", "D77BBA", "8F974A", "8A6F30",
    ];

    public List<uint> Colors { get; } = [];

    /// <summary>PICO-8 (16 colours) followed by DawnBringer 32.</summary>
    public static Palette CreateDefault()
    {
        var palette = new Palette();
        foreach (string hex in Pico8.Concat(Db32))
        {
            ColorUtil.TryParseHex(hex, out uint c);
            palette.Colors.Add(c);
        }
        return palette;
    }

    public Palette Clone()
    {
        var copy = new Palette();
        copy.Colors.AddRange(Colors);
        return copy;
    }

    public static Palette Load(string path)
    {
        string text = File.ReadAllText(path);
        return IsGpl(path) ? ParseGpl(text) : ParseHex(text);
    }

    public void Save(string path) => File.WriteAllText(path, IsGpl(path) ? ToGpl(Path.GetFileNameWithoutExtension(path)) : ToHex());

    /// <summary>Parses one <c>RRGGBB</c> per line; blank lines and <c>;</c> comments are skipped.</summary>
    public static Palette ParseHex(string text)
    {
        var palette = new Palette();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (!ColorUtil.TryParseHex(line, out uint c))
                throw new FormatException($"'{line}' is not a RRGGBB colour.");
            palette.Colors.Add(c | 0xFF000000);
        }
        return palette;
    }

    public string ToHex()
    {
        var sb = new StringBuilder();
        foreach (uint c in Colors)
            sb.Append($"{ColorUtil.R(c):x2}{ColorUtil.G(c):x2}{ColorUtil.B(c):x2}").Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses a GIMP palette: a header, then lines of <c>R G B [name]</c>.</summary>
    public static Palette ParseGpl(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("GIMP Palette", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Not a GIMP palette file.");

        var palette = new Palette();
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0 || line.StartsWith('#') || line.Contains(':')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 ||
                !byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) ||
                !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) ||
                !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
                continue;
            palette.Colors.Add(ColorUtil.FromRgb(r, g, b));
        }
        return palette;
    }

    public string ToGpl(string name)
    {
        var sb = new StringBuilder();
        sb.Append("GIMP Palette\n").Append("Name: ").Append(name).Append('\n').Append("Columns: 8\n#\n");
        foreach (uint c in Colors)
            sb.Append(CultureInfo.InvariantCulture,
                $"{ColorUtil.R(c),3} {ColorUtil.G(c),3} {ColorUtil.B(c),3}\t{ColorUtil.ToHex(c)}\n");
        return sb.ToString();
    }

    private static bool IsGpl(string path) => string.Equals(Path.GetExtension(path), ".gpl", StringComparison.OrdinalIgnoreCase);
}
