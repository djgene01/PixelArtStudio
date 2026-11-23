using PixelArtStudio.Core.History;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Mcp;

namespace PixelArtStudio.Tests;

/// <summary>A headless editor for the MCP tools: a document and an undo stack, with actions run inline.</summary>
internal sealed class FakeStudio : IStudioEditor, IStudioSession
{
    private readonly object _lock = new();
    private int _activeFrame;
    private int _activeLayer;

    public FakeStudio(int width = 8, int height = 8) => Document = SpriteDocument.Create(width, height);

    public SpriteDocument Document { get; private set; }
    public UndoStack History { get; } = new(100);
    public string DocumentName { get; private set; } = "Untitled";
    public string? FilePath { get; private set; }
    public bool IsDirty => History.IsDirty;

    public int ActiveFrame
    {
        get => _activeFrame;
        set => _activeFrame = Math.Clamp(value, 0, Document.Frames.Count - 1);
    }

    public int ActiveLayer
    {
        get => _activeLayer;
        set => _activeLayer = Math.Clamp(value, 0, Document.Layers.Count - 1);
    }

    public Task<T> RunAsync<T>(Func<IStudioEditor, T> action)
    {
        lock (_lock)
        {
            try
            {
                return Task.FromResult(action(this));
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }

    public bool EditCel(string name, int frame, int layer, Action<PixelBuffer> draw)
    {
        var cel = Document.GetCel(frame, layer);
        var before = cel.Clone();
        draw(cel);
        var edit = new CelEdit(name, frame, layer, before, cel);
        if (edit.IsEmpty) return false;
        History.Push(edit);
        return true;
    }

    public void EditDocument(string name, Action<SpriteDocument> change)
    {
        var before = Document.Clone();
        change(Document);
        History.Push(DocumentSnapshot.Capture(name, before, Document));
        ClampActive();
    }

    public void EditMeta(string name, Action<SpriteDocument> change)
    {
        var before = DocumentMeta.Capture(Document);
        change(Document);
        History.Push(new MetaEdit(name, before, DocumentMeta.Capture(Document)));
    }

    public void ReplaceDocument(SpriteDocument doc, string? path, string name)
    {
        Document = doc;
        FilePath = path;
        DocumentName = name;
        _activeFrame = 0;
        _activeLayer = doc.Layers.Count - 1;
        History.Clear();
    }

    public void Save(string path)
    {
        ProjectFile.Save(Document, path);
        FilePath = path;
        DocumentName = Path.GetFileNameWithoutExtension(path);
        History.MarkSaved();
    }

    public string? Undo()
    {
        var entry = History.PeekUndo;
        if (!History.Undo(Document)) return null;
        ClampActive();
        return entry!.Name;
    }

    public string? Redo()
    {
        var entry = History.PeekRedo;
        if (!History.Redo(Document)) return null;
        ClampActive();
        return entry!.Name;
    }

    private void ClampActive()
    {
        ActiveFrame = _activeFrame;
        ActiveLayer = _activeLayer;
    }
}
