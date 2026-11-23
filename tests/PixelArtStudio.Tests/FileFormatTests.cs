using System.Text.Json;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelArtStudio.Tests;

public sealed class FileFormatTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pxs-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>3 frames, 2 layers, 6x4, with distinct durations and some semi-transparent pixels.</summary>
    private static SpriteDocument CreateSample()
    {
        var doc = SpriteDocument.Create(6, 4);
        doc.AddLayer(0, "Top");
        doc.Layers[1].Opacity = 0.75;
        doc.AddFrame(0);
        doc.AddFrame(1);
        int[] durations = [100, 250, 40];
        for (int f = 0; f < 3; f++)
        {
            doc.Frames[f].DurationMs = durations[f];
            doc.GetCel(f, 0).Set(f, 0, 0xFFFF0000);
            doc.GetCel(f, 1).Set(5 - f, 3, 0x8000FF00);
        }
        return doc;
    }

    [Fact]
    public void ProjectFile_RoundTripIsPixelIdentical()
    {
        var doc = CreateSample();
        doc.Layers[0].Visible = false;
        string path = PathFor("sample.pxs");

        ProjectFile.Save(doc, path);
        var loaded = ProjectFile.Load(path);

        Assert.Equal((doc.Width, doc.Height), (loaded.Width, loaded.Height));
        Assert.Equal(doc.Layers.Select(l => (l.Name, l.Visible, l.Opacity)), loaded.Layers.Select(l => (l.Name, l.Visible, l.Opacity)));
        Assert.Equal(doc.Frames.Select(f => f.DurationMs), loaded.Frames.Select(f => f.DurationMs));
        Assert.Equal(doc.Palette.Colors, loaded.Palette.Colors);
        for (int f = 0; f < doc.Frames.Count; f++)
        for (int l = 0; l < doc.Layers.Count; l++)
            Assert.True(doc.GetCel(f, l).ContentEquals(loaded.GetCel(f, l)));
        Assert.False(File.Exists(path + ".tmp"));
    }

    private static int ManifestVersion(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("manifest.json")!.Open();
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("version").GetInt32();
    }

    [Fact]
    public void ProjectFile_PlainSpritesStayVersion1()
    {
        string path = PathFor("plain.pxs");
        ProjectFile.Save(CreateSample(), path);

        Assert.Equal(1, ManifestVersion(path));
        Assert.Empty(ProjectFile.Load(path).Cycles);
    }

    [Fact]
    public void ProjectFile_Version2RoundTripsCycles()
    {
        var doc = CreateSample();
        doc.Cycles.Add(new ColorCycle { Name = "Water", First = 2, Last = 6, StepMs = 120, Reverse = true });
        doc.Cycles.Add(new ColorCycle { Name = "Lava", First = 8, Last = 9, Enabled = false });
        string path = PathFor("cycles.pxs");

        ProjectFile.Save(doc, path);
        var loaded = ProjectFile.Load(path);

        Assert.Equal(2, ManifestVersion(path));
        Assert.Equal(
            doc.Cycles.Select(c => (c.Name, c.First, c.Last, c.StepMs, c.Reverse, c.Enabled)),
            loaded.Cycles.Select(c => (c.Name, c.First, c.Last, c.StepMs, c.Reverse, c.Enabled)));
    }

    [Fact]
    public void ProjectFile_LargeCanvasIsVersion2()
    {
        string path = PathFor("big.pxs");
        ProjectFile.Save(SpriteDocument.Create(1500, 8), path);

        Assert.Equal(2, ManifestVersion(path));
        Assert.Equal(1500, ProjectFile.Load(path).Width);
    }

    [Fact]
    public void Gif_BakedCyclesAddAFramePerStep()
    {
        const uint red = 0xFFFF0000, green = 0xFF00FF00, blue = 0xFF0000FF;
        var doc = SpriteDocument.Create(4, 4);
        doc.Palette = new Palette { Colors = { red, green, blue } };
        doc.Cycles.Add(new ColorCycle { First = 0, Last = 2, StepMs = 100 });
        doc.GetCel(0, 0).Set(0, 0, red);
        string path = PathFor("baked.gif");

        ImageExporter.Export(doc, path, new ExportOptions { Format = ExportFormat.Gif, BakeCycles = true }, out bool truncated);

        Assert.False(truncated);
        using var image = Image.Load<Bgra32>(path);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal(new[] { 10, 10, 10 }, image.Frames.Select(f => f.Metadata.GetGifMetadata().FrameDelay));
        Assert.Equal(255, image.Frames[0][0, 0].R);   // red, then the colour from slot 2 (blue), then green
        Assert.Equal(255, image.Frames[1][0, 0].B);
        Assert.Equal(255, image.Frames[2][0, 0].G);
    }

    [Fact]
    public void Export_WithoutBake_IgnoresCycles()
    {
        var doc = CreateSample();
        doc.Cycles.Add(new ColorCycle { First = 0, Last = 3 });
        string path = PathFor("plain.gif");

        ImageExporter.Export(doc, path, new ExportOptions { Format = ExportFormat.Gif });

        using var image = Image.Load<Bgra32>(path);
        Assert.Equal(3, image.Frames.Count);
    }

    [Fact]
    public void ProjectFile_CorruptFile_ThrowsInvalidData()
    {
        string path = PathFor("bad.pxs");
        File.WriteAllText(path, "not a zip");
        Assert.Throws<InvalidDataException>(() => ProjectFile.Load(path));
    }

    [Fact]
    public void Gif_ExportHasFramesDelaysAndLoop()
    {
        string path = PathFor("anim.gif");
        ImageExporter.Export(CreateSample(), path, new ExportOptions { Format = ExportFormat.Gif, Scale = 2 });

        using var image = Image.Load<Bgra32>(path);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal((12, 8), (image.Width, image.Height));
        Assert.Equal(new[] { 10, 25, 4 }, image.Frames.Select(f => f.Metadata.GetGifMetadata().FrameDelay));
        Assert.Equal(0, image.Metadata.GetGifMetadata().RepeatCount);
        Assert.Equal(0, image.Frames.RootFrame[5, 5].A);       // transparent area stays transparent
        Assert.Equal(255, image.Frames.RootFrame[0, 0].R);
    }

    [Fact]
    public void Apng_ExportHasFramesAndDelays()
    {
        string path = PathFor("anim.png");
        ImageExporter.Export(CreateSample(), path, new ExportOptions { Format = ExportFormat.Apng });

        using var image = Image.Load<Bgra32>(path);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal((6, 4), (image.Width, image.Height));
        var delays = image.Frames.Select(f => (int)Math.Round(f.Metadata.GetPngMetadata().FrameDelay.ToDouble() * 1000));
        Assert.Equal(new[] { 100, 250, 40 }, delays);
    }

    [Fact]
    public void WebP_ExportHasFramesDelaysAndSemiTransparency()
    {
        string path = PathFor("anim.webp");
        ImageExporter.Export(CreateSample(), path, new ExportOptions { Format = ExportFormat.WebP, Loop = false });

        using var image = Image.Load<Bgra32>(path);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal(new uint[] { 100, 250, 40 }, image.Frames.Select(f => f.Metadata.GetWebpMetadata().FrameDelay));
        Assert.Equal(1, image.Metadata.GetWebpMetadata().RepeatCount);
        Assert.InRange(image.Frames.RootFrame[5, 3].A, 90, 100);   // 0x80 * 0.75 layer opacity
    }

    [Fact]
    public void Importer_ReadsAnimatedGifAsFramesWithDurations()
    {
        string path = PathFor("anim.gif");
        ImageExporter.Export(CreateSample(), path, new ExportOptions { Format = ExportFormat.Gif });

        var doc = ImageImporter.LoadDocument(path);

        Assert.Equal(3, doc.Frames.Count);
        Assert.Equal(new[] { 100, 250, 40 }, doc.Frames.Select(f => f.DurationMs));
        Assert.Equal(0xFFFF0000u, doc.GetCel(1, 0).Get(1, 0));
    }

    [Fact]
    public void ScaledPngExport_HasScaledSize()
    {
        string path = PathFor("frame.png");
        ImageExporter.Export(CreateSample(), path, new ExportOptions { Format = ExportFormat.Png, Scale = 8, CurrentFrame = 1 });

        using var image = Image.Load<Bgra32>(path);
        Assert.Equal((48, 32), (image.Width, image.Height));
        Assert.Equal(255, image[8, 0].R);    // frame 1 has red at x=1
    }

    [Fact]
    public void PngSequence_WritesNumberedFiles()
    {
        var files = ImageExporter.Export(CreateSample(), PathFor("seq.png"),
            new ExportOptions { Format = ExportFormat.PngSequence, FirstFrame = 1 });

        Assert.Equal(new[] { "seq_0001.png", "seq_0002.png" }, files.Select(Path.GetFileName));
        Assert.All(files, f => Assert.True(File.Exists(f)));
    }

    [Theory]
    [InlineData(0, 18, 4)]
    [InlineData(2, 12, 8)]
    public void SpriteSheet_DimensionsAndJson(int columns, int expectedWidth, int expectedHeight)
    {
        string path = PathFor("sheet.png");
        var files = ImageExporter.Export(CreateSample(), path,
            new ExportOptions { Format = ExportFormat.SpriteSheet, SheetColumns = columns, WriteJson = true });

        using (var image = Image.Load<Bgra32>(path))
            Assert.Equal((expectedWidth, expectedHeight), (image.Width, image.Height));

        Assert.Equal(2, files.Count);
        using var json = JsonDocument.Parse(File.ReadAllText(files[1]));
        var frames = json.RootElement.GetProperty("frames");
        Assert.Equal(3, frames.GetArrayLength());
        Assert.Equal(250, frames[1].GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public void SpriteSheet_SliceRoundTrip()
    {
        var doc = CreateSample();
        doc.Layers[1].Opacity = 1;
        var frames = Enumerable.Range(0, 3).Select(f => Core.Drawing.Compositor.Flatten(doc, f)).ToList();
        var sheet = SpriteSheet.Build(frames, 2, out _);

        var sliced = SpriteSheet.Slice(sheet, 6, 4, skipEmpty: true);

        Assert.Equal(3, sliced.Frames.Count);   // the 4th grid cell is empty and skipped
        for (int f = 0; f < 3; f++)
            Assert.True(frames[f].ContentEquals(sliced.GetCel(f, 0)));
    }
}
