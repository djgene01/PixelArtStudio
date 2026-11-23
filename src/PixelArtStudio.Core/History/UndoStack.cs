using PixelArtStudio.Core.Model;

namespace PixelArtStudio.Core.History;

/// <summary>
/// Undo/redo history bounded by entry count and by memory, that also tracks whether the document changed since
/// the last save. The newest entry is always kept, even if it alone exceeds the byte budget.
/// </summary>
public sealed class UndoStack(int capacity = 100, long maxBytes = 768L << 20)
{
    private readonly List<IUndoEntry> _entries = [];
    private int _position;          // number of applied entries
    private int _savedPosition;     // -1 when the saved state is no longer reachable

    public event EventHandler? Changed;

    public int Capacity { get; } = Math.Max(1, capacity);
    public long MaxBytes { get; } = Math.Max(0, maxBytes);
    public long TotalBytes => _entries.Sum(e => e.SizeBytes);
    public int Count => _entries.Count;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _entries.Count;
    public bool IsDirty => _position != _savedPosition;
    public IUndoEntry? PeekUndo => CanUndo ? _entries[_position - 1] : null;
    public IUndoEntry? PeekRedo => CanRedo ? _entries[_position] : null;

    /// <summary>Adds an already-applied change. Discards the redo side.</summary>
    public void Push(IUndoEntry entry)
    {
        if (_position < _entries.Count)
        {
            _entries.RemoveRange(_position, _entries.Count - _position);
            if (_savedPosition > _position) _savedPosition = -1;
        }

        // Never merge into the saved state, or the dirty marker would be wrong.
        if (_position > 0 && _position != _savedPosition &&
            _entries[_position - 1] is IMergeableUndoEntry top && top.TryMerge(entry))
        {
            OnChanged();
            return;
        }

        _entries.Add(entry);
        _position++;
        if (_entries.Count > Capacity) DropOldest();
        long total = TotalBytes;
        while (total > MaxBytes && _entries.Count > 1) total -= DropOldest();
        OnChanged();
    }

    /// <summary>Removes the oldest entry and returns its size.</summary>
    private long DropOldest()
    {
        long size = _entries[0].SizeBytes;
        _entries.RemoveAt(0);
        _position--;
        _savedPosition = _savedPosition > 0 ? _savedPosition - 1 : -1;
        return size;
    }

    public bool Undo(SpriteDocument doc)
    {
        if (!CanUndo) return false;
        _position--;
        _entries[_position].Undo(doc);
        OnChanged();
        return true;
    }

    public bool Redo(SpriteDocument doc)
    {
        if (!CanRedo) return false;
        _entries[_position].Redo(doc);
        _position++;
        OnChanged();
        return true;
    }

    public void MarkSaved()
    {
        _savedPosition = _position;
        OnChanged();
    }

    public void Clear()
    {
        _entries.Clear();
        _position = 0;
        _savedPosition = 0;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
