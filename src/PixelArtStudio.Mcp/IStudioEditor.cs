using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Mcp;

/// <summary>
/// What the MCP tools need from the editor. Every member is called on the editor's thread,
/// through <see cref="IStudioSession.RunAsync{T}"/>.
/// </summary>
public interface IStudioEditor
{
    SpriteDocument Document { get; }
    int ActiveFrame { get; set; }
    int ActiveLayer { get; set; }
    string DocumentName { get; }
    string? FilePath { get; }
    bool IsDirty { get; }

    /// <summary>Runs <paramref name="draw"/> on one cel as a single undo step. Returns false if no pixel changed.</summary>
    bool EditCel(string name, int frame, int layer, Action<PixelBuffer> draw);

    /// <summary>Applies a structural change as a single whole-document undo step.</summary>
    void EditDocument(string name, Action<SpriteDocument> change);

    /// <summary>
    /// Applies a change to palette, colour cycles, layer properties or frame durations as a single undo step.
    /// It must not change pixels or the frame and layer layout.
    /// </summary>
    void EditMeta(string name, Action<SpriteDocument> change);

    /// <summary>Replaces the open document and clears the history.</summary>
    void ReplaceDocument(SpriteDocument doc, string? path, string name);

    /// <summary>Saves a <c>.pxs</c> project and marks the document clean. Throws on failure.</summary>
    void Save(string path);

    /// <summary>Undoes one step and returns its name, or null when there is nothing to undo.</summary>
    string? Undo();

    /// <summary>Redoes one step and returns its name, or null when there is nothing to redo.</summary>
    string? Redo();
}

/// <summary>Runs work against the editor on the editor's thread.</summary>
public interface IStudioSession
{
    Task<T> RunAsync<T>(Func<IStudioEditor, T> action);
}
