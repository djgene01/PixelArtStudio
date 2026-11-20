using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using PixelArtStudio.Core.History;
using PixelArtStudio.Core.IO;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Mcp;

namespace PixelArtStudio.ViewModels;

/// <summary>The editor surface the MCP tools drive. Always called on the UI thread.</summary>
public sealed partial class MainViewModel : IStudioEditor
{
    /// <summary>Status bar text for the MCP server, e.g. "MCP: 127.0.0.1:47821".</summary>
    [ObservableProperty]
    private string _mcpStatus = "MCP: off";

    int IStudioEditor.ActiveFrame
    {
        get => _activeFrame;
        set => ActiveFrameIndex = value;
    }

    int IStudioEditor.ActiveLayer
    {
        get => _activeLayer;
        set => ActiveLayerIndex = value;
    }

    bool IStudioEditor.EditCel(string name, int frame, int layer, Action<PixelBuffer> draw)
    {
        StopPlayback();
        FinishPendingEdits();
        var cel = Document.GetCel(frame, layer);
        var before = cel.Clone();
        try
        {
            draw(cel);
        }
        catch
        {
            cel.CopyFrom(before);
            throw;
        }

        bool changed = !cel.ContentEquals(before);
        if (changed)
        {
            History.Push(new CelEdit(name, frame, layer, before, cel.Clone()));
            RefreshThumbnail(frame);
            RaiseCanvasChanged(null);
        }
        StatusMessage = $"Claude: {name}";
        return changed;
    }

    void IStudioEditor.EditDocument(string name, Action<SpriteDocument> change)
    {
        StopPlayback();
        ApplyStructural(name, change);
        Selection = null;
        Zoom = Zoom;   // re-clamp in case the size changed
        StatusMessage = $"Claude: {name}";
    }

    void IStudioEditor.ReplaceDocument(SpriteDocument doc, string? path, string name)
    {
        LoadDocument(doc, path, name);
        if (path != null && string.Equals(Path.GetExtension(path), ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            AddRecent(path);
        StatusMessage = $"Claude: opened {name}";
    }

    void IStudioEditor.Save(string path)
    {
        FinishPendingEdits();
        ProjectFile.Save(Document, path);
        AfterSave(path);
        StatusMessage = $"Claude: saved {Path.GetFileName(path)}";
    }

    string? IStudioEditor.Undo() => StepFromClaude(undo: true);

    string? IStudioEditor.Redo() => StepFromClaude(undo: false);

    private string? StepFromClaude(bool undo)
    {
        StopPlayback();
        var entry = StepHistory(undo);
        if (entry != null) StatusMessage = $"Claude: {(undo ? "Undo" : "Redo")} {entry.Name}";
        return entry?.Name;
    }
}
