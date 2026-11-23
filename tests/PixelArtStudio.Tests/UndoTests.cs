using PixelArtStudio.Core.History;
using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Tests;

public class UndoTests
{
    private static CelEdit Paint(SpriteDocument doc, int x, uint color)
    {
        var cel = doc.GetCel(0, 0);
        var before = cel.Clone();
        cel.Set(x, 0, color);
        return new CelEdit("Paint", 0, 0, before, cel.Clone());
    }

    [Fact]
    public void CelEdit_UndoRedoRoundTrip()
    {
        var doc = SpriteDocument.Create(4, 1);
        var stack = new UndoStack();
        stack.Push(Paint(doc, 1, 0xFFFF0000));

        Assert.True(stack.Undo(doc));
        Assert.Equal(0u, doc.GetCel(0, 0).Get(1, 0));
        Assert.True(stack.Redo(doc));
        Assert.Equal(0xFFFF0000u, doc.GetCel(0, 0).Get(1, 0));
    }

    [Fact]
    public void Push_ClearsRedo()
    {
        var doc = SpriteDocument.Create(4, 1);
        var stack = new UndoStack();
        stack.Push(Paint(doc, 0, 0xFFFF0000));
        stack.Push(Paint(doc, 1, 0xFFFF0000));
        stack.Undo(doc);
        Assert.True(stack.CanRedo);

        stack.Push(Paint(doc, 2, 0xFF00FF00));

        Assert.False(stack.CanRedo);
        Assert.Equal(2, stack.Count);
    }

    [Fact]
    public void DocumentSnapshot_RestoresStructure()
    {
        var doc = SpriteDocument.Create(4, 4);
        var stack = new UndoStack();
        var before = doc.Clone();
        doc.AddFrame(0);
        doc.AddLayer(0);
        doc.Resize(8, 2, Anchor.Center);
        stack.Push(new DocumentSnapshot("Changes", before, doc.Clone()));

        stack.Undo(doc);
        Assert.Equal((4, 4, 1, 1), (doc.Width, doc.Height, doc.Frames.Count, doc.Layers.Count));

        stack.Redo(doc);
        Assert.Equal((8, 2, 2, 2), (doc.Width, doc.Height, doc.Frames.Count, doc.Layers.Count));
    }

    [Fact]
    public void Capacity_DropsOldestEntries()
    {
        var doc = SpriteDocument.Create(4, 1);
        var stack = new UndoStack(capacity: 3);
        for (int i = 0; i < 5; i++) stack.Push(Paint(doc, i % 4, 0xFF000000 | (uint)i));

        Assert.Equal(3, stack.Count);
        int undone = 0;
        while (stack.Undo(doc)) undone++;
        Assert.Equal(3, undone);
    }

    [Fact]
    public void DirtyMarker_TracksSavedPosition()
    {
        var doc = SpriteDocument.Create(4, 1);
        var stack = new UndoStack();
        Assert.False(stack.IsDirty);

        stack.Push(Paint(doc, 0, 0xFFFF0000));
        Assert.True(stack.IsDirty);

        stack.MarkSaved();
        Assert.False(stack.IsDirty);

        stack.Undo(doc);
        Assert.True(stack.IsDirty);
        stack.Redo(doc);
        Assert.False(stack.IsDirty);
    }

    [Fact]
    public void CelEdit_StoresOnlyTheChangedRectangle()
    {
        var doc = SpriteDocument.Create(100, 100);
        var cel = doc.GetCel(0, 0);
        var before = cel.Clone();
        cel.Set(10, 20, 0xFFFF0000);
        cel.Set(12, 25, 0xFF00FF00);

        var edit = new CelEdit("Paint", 0, 0, before, cel);   // the live cel as "after"

        Assert.False(edit.IsEmpty);
        Assert.Equal(new RectI(10, 20, 3, 6), edit.Bounds);
        Assert.True(edit.SizeBytes < 1000);

        edit.Undo(doc);
        Assert.True(doc.GetCel(0, 0).IsTransparent());
        edit.Redo(doc);
        Assert.Equal(0xFFFF0000u, doc.GetCel(0, 0).Get(10, 20));
        Assert.Equal(0xFF00FF00u, doc.GetCel(0, 0).Get(12, 25));
    }

    [Fact]
    public void CelEdit_WithoutChanges_IsEmpty()
    {
        var cel = new PixelBuffer(4, 4);
        Assert.True(new CelEdit("Nothing", 0, 0, cel.Clone(), cel).IsEmpty);
    }

    [Fact]
    public void ByteBudget_DropsOldestButKeepsTheNewest()
    {
        var doc = SpriteDocument.Create(1, 1);
        var stack = new UndoStack(capacity: 100, maxBytes: 1000);
        for (int i = 0; i < 5; i++) stack.Push(new SizedEntry(300));
        Assert.Equal(3, stack.Count);
        Assert.True(stack.TotalBytes <= 1000);

        stack.Push(new SizedEntry(5000));
        Assert.Equal(1, stack.Count);
        Assert.True(stack.Undo(doc));
    }

    [Fact]
    public void ByteBudget_KeepsDirtyMarkerRight()
    {
        var doc = SpriteDocument.Create(1, 1);
        var stack = new UndoStack(capacity: 100, maxBytes: 1000);
        stack.Push(new SizedEntry(400));
        stack.MarkSaved();
        stack.Push(new SizedEntry(400));
        stack.Push(new SizedEntry(400));   // drops the first entry; the saved state is the oldest one left

        while (stack.Undo(doc)) { }
        Assert.False(stack.IsDirty);

        stack.Redo(doc);
        stack.Redo(doc);
        stack.Push(new SizedEntry(400));   // drops another: the saved state is no longer reachable
        while (stack.Undo(doc)) { }
        Assert.True(stack.IsDirty);
    }

    [Fact]
    public void MetaEdit_RestoresPaletteCyclesLayersAndDurations()
    {
        var doc = SpriteDocument.Create(2, 2);
        var stack = new UndoStack();
        var before = DocumentMeta.Capture(doc);
        doc.Palette.Colors[0] = 0xFF123456;
        doc.Cycles.Add(new ColorCycle { First = 0, Last = 3 });
        doc.Layers[0].Opacity = 0.25;
        doc.Layers[0].Name = "Renamed";
        doc.Frames[0].DurationMs = 500;
        stack.Push(new MetaEdit("Meta", before, DocumentMeta.Capture(doc)));

        stack.Undo(doc);
        Assert.Equal(Palette.CreateDefault().Colors, doc.Palette.Colors);
        Assert.Empty(doc.Cycles);
        Assert.Equal((1.0, "Layer 1", 100), (doc.Layers[0].Opacity, doc.Layers[0].Name, doc.Frames[0].DurationMs));

        stack.Redo(doc);
        Assert.Equal(0xFF123456u, doc.Palette.Colors[0]);
        Assert.Single(doc.Cycles);
        Assert.Equal((0.25, "Renamed", 500), (doc.Layers[0].Opacity, doc.Layers[0].Name, doc.Frames[0].DurationMs));
    }

    [Fact]
    public void SnapshotCapture_SharesUnchangedCelsAndRestoresCorrectly()
    {
        var doc = SpriteDocument.Create(16, 16);
        doc.AddFrame(0);
        doc.AddFrame(1);
        doc.GetCel(0, 0).Set(0, 0, 0xFFFF0000);
        var stack = new UndoStack();
        long celBytes = 16 * 16 * sizeof(uint);

        var before = doc.Clone();
        doc.GetCel(1, 0).Set(5, 5, 0xFF00FF00);
        var snapshot = DocumentSnapshot.Capture("Edit", before, doc);
        stack.Push(snapshot);
        Assert.True(snapshot.SizeBytes < 6 * celBytes);   // 3 before + 1 changed after, not 6 cels

        // Later edits to the live document must not leak into the snapshot.
        doc.GetCel(0, 0).Set(0, 0, 0xFF0000FF);
        doc.GetCel(2, 0).Set(1, 1, 0xFF0000FF);

        stack.Undo(doc);
        Assert.Equal(0xFFFF0000u, doc.GetCel(0, 0).Get(0, 0));
        Assert.Equal(0u, doc.GetCel(1, 0).Get(5, 5));
        Assert.Equal(0u, doc.GetCel(2, 0).Get(1, 1));

        stack.Redo(doc);
        Assert.Equal(0xFF00FF00u, doc.GetCel(1, 0).Get(5, 5));
        Assert.Equal(0xFFFF0000u, doc.GetCel(0, 0).Get(0, 0));

        // Undo and redo hand out copies: editing after redo must not change the stored states.
        doc.GetCel(0, 0).Set(0, 0, 0xFF0000FF);
        stack.Undo(doc);
        Assert.Equal(0xFFFF0000u, doc.GetCel(0, 0).Get(0, 0));
    }

    private sealed class SizedEntry(long size) : IUndoEntry
    {
        public string Name => "Sized";
        public long SizeBytes => size;
        public void Undo(SpriteDocument doc) { }
        public void Redo(SpriteDocument doc) { }
    }

    [Fact]
    public void MergeableSnapshots_CollapseWithSameKey()
    {
        var doc = SpriteDocument.Create(2, 2);
        var stack = new UndoStack();
        var original = doc.Clone();

        foreach (double opacity in new[] { 0.8, 0.6, 0.4 })
        {
            var before = doc.Clone();
            doc.Layers[0].Opacity = opacity;
            stack.Push(new DocumentSnapshot("Opacity", before, doc.Clone(), "opacity:0"));
        }

        Assert.Equal(1, stack.Count);
        stack.Undo(doc);
        Assert.Equal(original.Layers[0].Opacity, doc.Layers[0].Opacity);
        stack.Redo(doc);
        Assert.Equal(0.4, doc.Layers[0].Opacity);
    }
}
