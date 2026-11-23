using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Mcp.Tools;

namespace PixelArtStudio.Tests;

public class McpToolTests : IDisposable
{
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    private readonly FakeStudio _studio = new(8, 8);
    private readonly string _dir = Directory.CreateTempSubdirectory("pas-mcp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private DrawingTools Drawing => new(_studio);
    private PixelBuffer Cel(int frame = 0, int layer = 0) => _studio.Document.GetCel(frame, layer);

    private static DrawOp Op(string type, string? color = "#FF0000", int? x0 = null, int? y0 = null, int? x1 = null, int? y1 = null) =>
        new() { Type = type, Color = color, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };

    private static DrawOp Px(int x, int y) => new() { Type = "pixels", Color = "#FF0000", Points = [[x, y]] };

    [Fact]
    public async Task Draw_AppliesEveryOpTypeAsOneUndoStep()
    {
        await Drawing.Draw(
        [
            new DrawOp { Type = "pixels", Color = "#FF0000", Points = [[0, 0], [7, 7]] },
            Op("line", "#0000FF", 0, 2, 7, 2),
            Op("rect", "#00FF00", 0, 4, 2, 6),
            new DrawOp { Type = "ellipse", Color = "#FFFFFF", X0 = 4, Y0 = 4, X1 = 6, Y1 = 6, Filled = true },
            Op("clear", null, 7, 2, 7, 2),
        ]);

        var cel = Cel();
        Assert.Equal(Red, cel.Get(0, 0));
        Assert.Equal(Red, cel.Get(7, 7));
        Assert.Equal(Blue, cel.Get(3, 2));
        Assert.Equal(0u, cel.Get(7, 2));                  // cleared
        Assert.Equal(0xFF00FF00u, cel.Get(0, 5));         // rect outline
        Assert.Equal(0u, cel.Get(1, 5));                  // rect inside stays empty
        Assert.Equal(0xFFFFFFFFu, cel.Get(5, 5));         // filled ellipse centre
        Assert.Equal(1, _studio.History.Count);
    }

    [Fact]
    public async Task Draw_FillAndClearWholeCel()
    {
        await Drawing.Draw([Op("line", "#0000FF", 0, 3, 7, 3), new DrawOp { Type = "fill", Color = "#FF0000", X = 0, Y = 0 }]);
        Assert.Equal(Red, Cel().Get(5, 1));
        Assert.Equal(0u, Cel().Get(5, 6));                // below the line is a separate region

        await Drawing.Draw([new DrawOp { Type = "clear" }]);
        Assert.True(Cel().IsTransparent());
        Assert.Equal(2, _studio.History.Count);
    }

    [Fact]
    public async Task Draw_InvalidOpFailsBeforeDrawing()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            Drawing.Draw([Op("line", "#FF0000", 0, 0, 7, 0), Op("rect", "red", 0, 0, 1, 1)]));
        Assert.Contains("ops[1]", ex.Message);
        Assert.True(Cel().IsTransparent());
        Assert.Equal(0, _studio.History.Count);

        await Assert.ThrowsAsync<McpException>(() => Drawing.Draw([Op("spray")]));
        await Assert.ThrowsAsync<McpException>(() => Drawing.Draw([Op("line", "#FF0000", 0, 0)]));
    }

    [Fact]
    public async Task OutOfRangeIndices_AreRejectedWithTheValidRange()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => Drawing.Draw([Px(0, 0)], frame: 3));
        Assert.Equal("frame 3 is out of range (0-0).", ex.Message);
        await Assert.ThrowsAsync<McpException>(() => Drawing.Draw([Px(0, 0)], layer: 1));
        await Assert.ThrowsAsync<McpException>(() => new FrameTools(_studio).MoveFrame(0, 5));
    }

    [Fact]
    public async Task PaintRows_RoundTripsThroughGetPixels()
    {
        string[] rows = [".kk.", "kyyk", "kyyk", ".kk."];
        await Drawing.PaintRows(2, 1, rows, new() { ["k"] = "#000000", ["y"] = "#FFEC27" });
        Assert.Equal(0xFF000000u, Cel().Get(3, 1));
        Assert.Equal(0xFFFFEC27u, Cel().Get(3, 2));
        Assert.Equal(1, _studio.History.Count);

        using var json = JsonDocument.Parse(await Drawing.GetPixels(x: 2, y: 1, width: 4, height: 4));
        var root = json.RootElement;
        var legend = root.GetProperty("legend").EnumerateObject().ToDictionary(p => p.Value.GetString()!, p => p.Name);
        var readRows = root.GetProperty("rows").EnumerateArray().Select(r => r.GetString()!).ToArray();
        var expected = rows.Select(r => r.Replace("k", legend["#000000"]).Replace("y", legend["#FFEC27"])).ToArray();
        Assert.Equal(expected, readRows);
    }

    [Fact]
    public async Task PaintRows_RejectsUnknownCharactersAndBadColours()
    {
        await Assert.ThrowsAsync<McpException>(() => Drawing.PaintRows(0, 0, ["ab"], new() { ["a"] = "#FF0000" }));
        await Assert.ThrowsAsync<McpException>(() => Drawing.PaintRows(0, 0, ["a"], new() { ["a"] = "#GG0000" }));
        await Assert.ThrowsAsync<McpException>(() => Drawing.PaintRows(0, 0, ["."], new() { ["."] = "#FF0000" }));
        Assert.Equal(0, _studio.History.Count);
    }

    [Fact]
    public async Task GetPixels_RejectsLargeRegions()
    {
        var studio = new FakeStudio(200, 10);
        await Assert.ThrowsAsync<McpException>(() => new DrawingTools(studio).GetPixels());
        await new DrawingTools(studio).GetPixels(width: 128);
    }

    [Fact]
    public async Task CopyAndShiftCel()
    {
        await new FrameTools(_studio).AddFrame();
        await Drawing.Draw([Px(1, 1)], frame: 0);
        await Drawing.CopyCel(0, 0, 1, 0);
        Assert.Equal(Red, Cel(1).Get(1, 1));

        await Drawing.ShiftCel(2, 3, frame: 1);
        Assert.Equal(0u, Cel(1).Get(1, 1));
        Assert.Equal(Red, Cel(1).Get(3, 4));
        Assert.Equal(Red, Cel(0).Get(1, 1));
    }

    [Fact]
    public async Task FrameTools_AddDuplicateMoveDelete()
    {
        var frames = new FrameTools(_studio);
        await Drawing.Draw([Px(0, 0)]);
        await frames.AddFrame();                              // blank frame 1
        await frames.AddFrame(duplicate_of: 0);               // copy of 0 at 1
        Assert.Equal(3, _studio.Document.Frames.Count);
        Assert.Equal(Red, Cel(1).Get(0, 0));
        Assert.True(Cel(2).IsTransparent());
        Assert.Equal(1, _studio.ActiveFrame);

        await frames.MoveFrame(2, 0);
        Assert.True(Cel(0).IsTransparent());

        await frames.SetFrameDuration(5, all_frames: true);
        Assert.All(_studio.Document.Frames, f => Assert.Equal(Frame.MinDurationMs, f.DurationMs));

        await frames.DeleteFrame(0);
        await frames.DeleteFrame(0);
        Assert.Single(_studio.Document.Frames);
        await Assert.ThrowsAsync<McpException>(() => frames.DeleteFrame());
    }

    [Fact]
    public async Task LayerTools_AddSetMergeDelete()
    {
        var layers = new LayerTools(_studio);
        await layers.AddLayer("Outline");
        Assert.Equal(2, _studio.Document.Layers.Count);
        Assert.Equal("Outline", _studio.Document.Layers[1].Name);
        Assert.Equal(1, _studio.ActiveLayer);

        await layers.SetLayer(opacity: 0.5, visible: false);
        Assert.False(_studio.Document.Layers[1].Visible);
        Assert.Equal(0.5, _studio.Document.Layers[1].Opacity);
        await Assert.ThrowsAsync<McpException>(() => layers.SetLayer(opacity: 2));

        await layers.AddLayer(below: -1);
        Assert.Equal(3, _studio.Document.Layers.Count);
        Assert.Equal(0, _studio.ActiveLayer);
        await Assert.ThrowsAsync<McpException>(() => layers.MergeDown(0));

        await layers.MergeDown(1);
        await layers.DeleteLayer(0);
        Assert.Single(_studio.Document.Layers);
        await Assert.ThrowsAsync<McpException>(() => layers.DeleteLayer());
    }

    [Fact]
    public async Task CanvasTools_ResizeRotatePalette()
    {
        var canvas = new CanvasTools(_studio);
        await Drawing.Draw([Px(0, 0)]);
        await canvas.ResizeCanvas(10, 6, "top_left");
        Assert.Equal((10, 6), (_studio.Document.Width, _studio.Document.Height));
        Assert.Equal(Red, Cel().Get(0, 0));

        await canvas.Rotate(clockwise: true);
        Assert.Equal((6, 10), (_studio.Document.Width, _studio.Document.Height));
        await Assert.ThrowsAsync<McpException>(() => canvas.ResizeCanvas(SpriteDocument.MaxSize + 1, 2, "top_left"));
        await Assert.ThrowsAsync<McpException>(() => canvas.ResizeCanvas(4, 4, "middle"));

        await canvas.SetPalette(["#000000", "#FF0000", "#FF0000"]);
        Assert.Equal([0xFF000000u, Red], _studio.Document.Palette.Colors);
    }

    [Fact]
    public async Task NewSprite_RefusesWhenDirtyUnlessDiscarding()
    {
        var docs = new DocumentTools(_studio);
        await Drawing.Draw([Px(0, 0)]);
        var original = _studio.Document;

        var ex = await Assert.ThrowsAsync<McpException>(() => docs.NewSprite(16, 16));
        Assert.Contains("unsaved changes", ex.Message);
        Assert.Same(original, _studio.Document);

        await docs.NewSprite(16, 16, frames: 4, discard_changes: true);
        Assert.Equal((16, 16, 4), (_studio.Document.Width, _studio.Document.Height, _studio.Document.Frames.Count));
        Assert.False(_studio.IsDirty);
    }

    [Fact]
    public async Task SaveAndOpen_RoundTripProject()
    {
        var docs = new DocumentTools(_studio);
        await Drawing.Draw([Px(2, 3)]);
        await Assert.ThrowsAsync<McpException>(() => docs.Save());
        await Assert.ThrowsAsync<McpException>(() => docs.Save(Path.Combine(_dir, "x.png")));

        string path = Path.Combine(_dir, "hero.pxs");
        await docs.Save(path);
        Assert.False(_studio.IsDirty);
        Assert.Equal(path, _studio.FilePath);

        await docs.NewSprite(4, 4);
        await docs.OpenFile(path);
        Assert.Equal(Red, Cel().Get(2, 3));
        Assert.Equal("hero", _studio.DocumentName);
    }

    [Fact]
    public async Task Export_WritesFiles()
    {
        var docs = new DocumentTools(_studio);
        await new FrameTools(_studio).AddFrame();

        string gif = Path.Combine(_dir, "anim.gif");
        string result = await docs.Export(gif, "gif", scale: 2);
        Assert.Contains(gif, result);
        Assert.True(File.Exists(gif));

        string sheet = Path.Combine(_dir, "sheet.png");
        await docs.Export(sheet, "sprite_sheet", write_json: true);
        Assert.Equal((16, 8), (ImageImporter.LoadPixels(sheet).Width, ImageImporter.LoadPixels(sheet).Height));
        Assert.True(File.Exists(Path.Combine(_dir, "sheet.json")));

        await Assert.ThrowsAsync<McpException>(() => docs.Export(gif, "tiff"));
        await Assert.ThrowsAsync<McpException>(() => docs.Export(Path.Combine(_dir, "missing", "a.png"), "png"));
    }

    [Fact]
    public async Task ViewFrame_ReturnsDecodablePng()
    {
        await Drawing.Draw([Px(0, 0)]);
        var blocks = (await new ViewTools(_studio).ViewFrame()).ToList();

        var image = Assert.Single(blocks.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        var pixels = ImageImporter.LoadPixels(new MemoryStream(image.DecodedData.ToArray()));
        Assert.Equal((128, 128), (pixels.Width, pixels.Height));   // 512 / 8 = 64x, capped at 16x
    }

    [Fact]
    public async Task ViewAnimation_LaysFramesOut()
    {
        var frames = new FrameTools(_studio);
        for (int i = 0; i < 4; i++) await frames.AddFrame();
        var blocks = (await new ViewTools(_studio).ViewAnimation(columns: 3, scale: 2)).ToList();

        var image = Assert.Single(blocks.OfType<ImageContentBlock>());
        var pixels = ImageImporter.LoadPixels(new MemoryStream(image.DecodedData.ToArray()));
        Assert.Equal((3 * 8 * 2, 2 * 8 * 2), (pixels.Width, pixels.Height));
        Assert.Contains("5 frame(s)", Assert.Single(blocks.OfType<TextContentBlock>()).Text);
    }

    [Fact]
    public async Task ColorCycleTools_AddSetRemoveAndUndo()
    {
        var canvas = new CanvasTools(_studio);
        await canvas.SetPalette(["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"]);

        string added = await canvas.AddColorCycle(0, 2, step_ms: 120, name: "Fire");
        Assert.Contains("Fire", added);
        var cycle = Assert.Single(_studio.Document.Cycles);
        Assert.Equal((0, 2, 120, false), (cycle.First, cycle.Last, cycle.StepMs, cycle.Reverse));

        await canvas.SetColorCycle(0, reverse: true, enabled: false);
        Assert.True(_studio.Document.Cycles[0].Reverse);
        Assert.False(_studio.Document.Cycles[0].Enabled);

        await Assert.ThrowsAsync<McpException>(() => canvas.AddColorCycle(2, 2));      // one slot
        await Assert.ThrowsAsync<McpException>(() => canvas.AddColorCycle(0, 9));      // past the palette
        await Assert.ThrowsAsync<McpException>(() => canvas.AddColorCycle(0, 1, step_ms: 5));
        await Assert.ThrowsAsync<McpException>(() => canvas.SetColorCycle(3, enabled: true));

        await canvas.RemoveColorCycle(0);
        Assert.Empty(_studio.Document.Cycles);
        await new DocumentTools(_studio).Undo();
        Assert.Single(_studio.Document.Cycles);

        using var json = JsonDocument.Parse(await new DocumentTools(_studio).GetState());
        Assert.Equal("Fire", json.RootElement.GetProperty("cycles")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SetPalette_DropsCyclesThatNoLongerFit()
    {
        var canvas = new CanvasTools(_studio);
        await canvas.AddColorCycle(10, 20);
        string result = await canvas.SetPalette(["#000000", "#FFFFFF"]);

        Assert.Contains("removed", result);
        Assert.Empty(_studio.Document.Cycles);
    }

    [Fact]
    public async Task ReplaceColor_RecoloursEveryCelAndThePalette()
    {
        var canvas = new CanvasTools(_studio);
        await canvas.SetPalette(["#FF0000", "#00FF00"]);
        await Drawing.Draw([Px(1, 1)]);
        await new FrameTools(_studio).AddFrame(duplicate_of: 0);
        int steps = _studio.History.Count;

        string result = await canvas.ReplaceColor("#FF0000", "#0000FF");

        Assert.Contains("2 pixel(s)", result);
        Assert.Equal(Blue, Cel(0).Get(1, 1));
        Assert.Equal(Blue, Cel(1).Get(1, 1));
        Assert.Equal(Blue, _studio.Document.Palette.Colors[0]);
        Assert.Equal(steps + 1, _studio.History.Count);

        await new DocumentTools(_studio).Undo();
        Assert.Equal(Red, Cel(1).Get(1, 1));
        Assert.Equal(Red, _studio.Document.Palette.Colors[0]);
        await Assert.ThrowsAsync<McpException>(() => canvas.ReplaceColor("#FF0000", "#FF0000"));
    }

    [Fact]
    public async Task SizeAndBudgetLimits_AreEnforced()
    {
        var docs = new DocumentTools(_studio);
        await Assert.ThrowsAsync<McpException>(() => docs.NewSprite(SpriteDocument.MaxSize + 1, 8));
        var ex = await Assert.ThrowsAsync<McpException>(() => docs.NewSprite(4096, 4096, frames: 64));
        Assert.Contains("too large", ex.Message);

        await docs.NewSprite(2048, 2048, frames: 1, discard_changes: true);
        Assert.Equal(2048, _studio.Document.Width);

        var studio = new FakeStudio(4096, 4096);
        for (int i = 0; i < 31; i++) studio.Document.AddLayer(i);   // 32 layers of 16M pixels = the whole budget
        await Assert.ThrowsAsync<McpException>(() => new LayerTools(studio).AddLayer());
        await Assert.ThrowsAsync<McpException>(() => new FrameTools(studio).AddFrame());
    }

    [Fact]
    public async Task ViewFrame_RegionAndCycleTime()
    {
        var canvas = new CanvasTools(_studio);
        await canvas.SetPalette(["#FF0000", "#00FF00"]);
        await Drawing.Draw([Px(2, 2)]);
        await canvas.AddColorCycle(0, 1);
        var view = new ViewTools(_studio);

        var region = (await view.ViewFrame(x: 2, y: 2, width: 3, height: 2)).ToList();
        var pixels = ImageImporter.LoadPixels(new MemoryStream(Assert.Single(region.OfType<ImageContentBlock>()).DecodedData.ToArray()));
        Assert.Equal((48, 32), (pixels.Width, pixels.Height));   // 3x2 at 16x
        Assert.Equal(Red, pixels.Get(0, 0));
        Assert.Contains("region (2, 2) 3x2", Assert.Single(region.OfType<TextContentBlock>()).Text);

        var cycled = (await view.ViewFrame(x: 2, y: 2, width: 1, height: 1, scale: 1, cycle_time_ms: 100)).ToList();
        var one = ImageImporter.LoadPixels(new MemoryStream(Assert.Single(cycled.OfType<ImageContentBlock>()).DecodedData.ToArray()));
        Assert.Equal(0xFF00FF00u, one.Get(0, 0));
        Assert.Equal(Red, Cel().Get(2, 2));
    }

    [Fact]
    public async Task ViewFrame_ShrinksBigSprites()
    {
        var studio = new FakeStudio(3000, 10);
        var blocks = (await new ViewTools(studio).ViewFrame()).ToList();

        var pixels = ImageImporter.LoadPixels(new MemoryStream(Assert.Single(blocks.OfType<ImageContentBlock>()).DecodedData.ToArray()));
        Assert.Equal((1500, 5), (pixels.Width, pixels.Height));
        Assert.Contains("shrunk to 1/2", Assert.Single(blocks.OfType<TextContentBlock>()).Text);
    }

    [Fact]
    public async Task Export_BakeCycles()
    {
        var canvas = new CanvasTools(_studio);
        await canvas.SetPalette(["#FF0000", "#00FF00", "#0000FF"]);
        await Drawing.Draw([Px(0, 0)]);
        await canvas.AddColorCycle(0, 2);

        string gif = Path.Combine(_dir, "cycle.gif");
        await new DocumentTools(_studio).Export(gif, "gif", bake_cycles: true);

        var doc = ImageImporter.LoadDocument(gif);
        Assert.Equal(3, doc.Frames.Count);
    }

    [Fact]
    public async Task UndoRedo_RestorePixels()
    {
        var docs = new DocumentTools(_studio);
        await Drawing.Draw([Px(1, 1)]);

        Assert.Equal("Undid: Draw pixels", await docs.Undo());
        Assert.Equal(0u, Cel().Get(1, 1));
        Assert.Equal("Nothing to undo.", await docs.Undo());

        Assert.Equal("Redid: Draw pixels", await docs.Redo());
        Assert.Equal(Red, Cel().Get(1, 1));
    }

    [Fact]
    public async Task GetState_DescribesTheDocument()
    {
        await new LayerTools(_studio).AddLayer("Top");
        using var json = JsonDocument.Parse(await new DocumentTools(_studio).GetState());
        var root = json.RootElement;
        Assert.Equal(8, root.GetProperty("width").GetInt32());
        Assert.True(root.GetProperty("dirty").GetBoolean());
        Assert.Equal(1, root.GetProperty("active_layer").GetInt32());
        Assert.Equal("Top", root.GetProperty("layers")[1].GetProperty("name").GetString());
        Assert.Equal(100, root.GetProperty("frames")[0].GetProperty("duration_ms").GetInt32());
    }
}
