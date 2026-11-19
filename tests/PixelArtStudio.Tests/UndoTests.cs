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
