using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelArtStudio.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelArtStudio.Core.IO;

/// <summary>
/// The native <c>.pxs</c> format: a ZIP with <c>manifest.json</c> and one PNG per cel named <c>f{frame}_l{layer}.png</c>.
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".pxs";

    /// <summary>Version 2 adds colour cycles and sides over 1024 px. Plain sprites are still written as version 1.</summary>
    public const int CurrentVersion = 2;

    private const int LegacyMaxSize = 1024;
    private const string ManifestName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly PngEncoder CelEncoder = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        TransparentColorMode = PngTransparentColorMode.Preserve,
    };

    /// <summary>Writes to a temporary file first, so a failed save never corrupts an existing file.</summary>
    public static void Save(SpriteDocument doc, string path)
    {
        string tempPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                Write(doc, stream);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public static void Write(SpriteDocument doc, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // Older builds refuse newer versions, so only write 2 when the file needs it.
        bool needsV2 = doc.Cycles.Count > 0 || doc.Width > LegacyMaxSize || doc.Height > LegacyMaxSize;
        var manifest = new Manifest
        {
            Version = needsV2 ? CurrentVersion : 1,
            Width = doc.Width,
            Height = doc.Height,
            Layers = doc.Layers.Select(l => new LayerEntry { Name = l.Name, Visible = l.Visible, Opacity = l.Opacity }).ToList(),
            Frames = doc.Frames.Select(f => new FrameEntry { DurationMs = f.DurationMs }).ToList(),
            Palette = doc.Palette.Colors.Select(ColorUtil.ToHex).ToList(),
            Cycles = doc.Cycles.Count == 0 ? null : doc.Cycles.Select(c => new CycleEntry
            {
                Name = c.Name,
                First = c.First,
                Last = c.Last,
                StepMs = c.StepMs,
                Reverse = c.Reverse,
                Enabled = c.Enabled,
            }).ToList(),
        };
        using (var entry = zip.CreateEntry(ManifestName).Open())
            JsonSerializer.Serialize(entry, manifest, JsonOptions);

        for (int f = 0; f < doc.Frames.Count; f++)
        for (int l = 0; l < doc.Layers.Count; l++)
        {
            using var entry = zip.CreateEntry(CelName(f, l), CompressionLevel.NoCompression).Open();
            using var image = ImageConvert.ToImage(doc.GetCel(f, l));
            image.Save(entry, CelEncoder);
        }
    }

    public static SpriteDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <exception cref="InvalidDataException">The file is not a valid project, or is from a newer version.</exception>
    public static SpriteDocument Read(Stream stream)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var manifestEntry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("The file has no manifest.");

            Manifest manifest;
            using (var s = manifestEntry.Open())
                manifest = JsonSerializer.Deserialize<Manifest>(s, JsonOptions) ?? throw new InvalidDataException("The manifest is empty.");

            if (manifest.Version > CurrentVersion)
                throw new InvalidDataException($"This file was made by a newer version of PixelArtStudio (format {manifest.Version}).");
            if (manifest.Version < 1 || manifest.Layers.Count == 0 || manifest.Frames.Count == 0)
                throw new InvalidDataException("The manifest is invalid.");

            var layers = manifest.Layers.Select(l => new Layer(l.Name) { Visible = l.Visible, Opacity = l.Opacity });
            var doc = SpriteDocument.CreateForLoad(manifest.Width, manifest.Height, layers);

            for (int f = 0; f < manifest.Frames.Count; f++)
            {
                var frame = new Frame { DurationMs = manifest.Frames[f].DurationMs };
                for (int l = 0; l < manifest.Layers.Count; l++)
                    frame.Cels.Add(ReadCel(zip, f, l, doc.Width, doc.Height));
                doc.Frames.Add(frame);
            }

            if (manifest.Palette.Count > 0)
            {
                var palette = new Palette();
                foreach (string hex in manifest.Palette)
                    if (ColorUtil.TryParseHex(hex, out uint c)) palette.Colors.Add(c);
                doc.Palette = palette;
            }

            foreach (var c in manifest.Cycles ?? [])
            {
                doc.Cycles.Add(new ColorCycle
                {
                    Name = c.Name,
                    First = c.First,
                    Last = c.Last,
                    StepMs = c.StepMs,
                    Reverse = c.Reverse,
                    Enabled = c.Enabled,
                });
            }
            doc.NormalizeCycles();
            return doc;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or ImageFormatException or UnknownImageFormatException)
        {
            throw new InvalidDataException($"The project file is damaged: {ex.Message}", ex);
        }
    }

    private static PixelBuffer ReadCel(ZipArchive zip, int frame, int layer, int width, int height)
    {
        var entry = zip.GetEntry(CelName(frame, layer)) ?? throw new InvalidDataException($"Missing image {CelName(frame, layer)}.");
        using var s = entry.Open();
        using var image = Image.Load<Bgra32>(s);
        if (image.Width != width || image.Height != height)
            throw new InvalidDataException($"{CelName(frame, layer)} is {image.Width}x{image.Height}, expected {width}x{height}.");
        return ImageConvert.FromFrame(image.Frames.RootFrame);
    }

    private static string CelName(int frame, int layer) => $"f{frame}_l{layer}.png";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Manifest
    {
        public int Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<LayerEntry> Layers { get; set; } = [];
        public List<FrameEntry> Frames { get; set; } = [];
        public List<string> Palette { get; set; } = [];

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CycleEntry>? Cycles { get; set; }
    }

    private sealed class CycleEntry
    {
        public string Name { get; set; } = "Cycle";
        public int First { get; set; }
        public int Last { get; set; }
        public int StepMs { get; set; } = ColorCycle.DefaultStepMs;
        public bool Reverse { get; set; }
        public bool Enabled { get; set; } = true;
    }

    private sealed class LayerEntry
    {
        public string Name { get; set; } = "";
        public bool Visible { get; set; } = true;
        public double Opacity { get; set; } = 1.0;
    }

    private sealed class FrameEntry
    {
        public int DurationMs { get; set; } = Frame.DefaultDurationMs;
    }
}
