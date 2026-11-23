using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixelArtStudio.Core.History;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Services;
using PixelArtStudio.Tools;
using Sel = PixelArtStudio.Core.Drawing.Selection;

namespace PixelArtStudio.ViewModels;

public sealed partial class MainViewModel
{
    private PixelBuffer? _clipboard;

    // ---- Undo ----

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => StepHistory(undo: true);

    private bool CanUndo() => History.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => StepHistory(undo: false);

    private bool CanRedo() => History.CanRedo;

    /// <summary>Undoes or redoes one step. Returns the entry, or null when there was nothing to step.</summary>
    private IUndoEntry? StepHistory(bool undo)
    {
        FinishPendingEdits();
        var entry = undo ? History.PeekUndo : History.PeekRedo;
        if (!(undo ? History.Undo(Document) : History.Redo(Document))) return null;
        AfterHistoryStep(entry, undo ? "Undo" : "Redo");
        return entry;
    }

    // ---- Tools and colours ----

    [RelayCommand]
    private void SetTool(ToolKind tool) => CurrentTool = tool;

    [RelayCommand]
    private void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    [RelayCommand]
    private void BrushBigger() => BrushSize++;

    [RelayCommand]
    private void BrushSmaller() => BrushSize--;

    /// <summary>Click selects one slot and picks it as the primary colour; Shift+click extends the selected range.</summary>
    [RelayCommand]
    private void PickSwatch(SwatchViewModel? swatch)
    {
        if (swatch == null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _swatchAnchor >= 0)
        {
            SelectSwatches(_swatchAnchor, swatch.Index, keepAnchor: true);
            int count = _swatchLast - _swatchFirst + 1;
            StatusMessage = $"Palette slots {_swatchFirst}-{_swatchLast} selected ({count} colours)." +
                            (count >= 2 ? " Add a colour cycle to animate them." : "");
            return;
        }
        SelectSwatches(swatch.Index, swatch.Index);
        PrimaryColor = swatch.Color;
    }

    [RelayCommand]
    private void PickSwatchSecondary(SwatchViewModel? swatch)
    {
        if (swatch != null) SecondaryColor = swatch.Color;
    }

    [RelayCommand]
    private void AddToPalette()
    {
        uint color = _primary | 0xFF000000;
        if (Document.Palette.Colors.Contains(color)) return;
        ApplyStructural("Add palette colour", d => d.Palette.Colors.Add(color), thumbnails: false, pixels: false);
        SelectSwatches(Document.Palette.Colors.Count - 1, Document.Palette.Colors.Count - 1);
    }

    [RelayCommand]
    private void RemoveFromPalette()
    {
        int slot = _swatchFirst >= 0 && _swatchFirst == _swatchLast ? _swatchFirst : Document.Palette.Colors.IndexOf(_primary | 0xFF000000);
        if (slot < 0)
        {
            StatusMessage = "Pick a palette colour first, then remove it.";
            return;
        }
        if (Document.Palette.Colors.Count <= 1)
        {
            StatusMessage = "The palette needs at least one colour.";
            return;
        }
        ClearSwatchSelection();
        ApplyStructural("Remove palette colour", d =>
        {
            d.Palette.Colors.RemoveAt(slot);
            // Cycles after the removed slot move down with their colours.
            foreach (var c in d.Cycles)
            {
                if (c.First > slot) c.First--;
                if (c.Last >= slot) c.Last--;
            }
        }, thumbnails: false, pixels: false);
        WarnAboutCycles();
    }

    [RelayCommand]
    private void ResetPalette()
    {
        ClearSwatchSelection();
        ApplyStructural("Reset palette", d => d.Palette = Palette.CreateDefault(), thumbnails: false, pixels: false);
        WarnAboutCycles();
    }

    /// <summary>
    /// Sets the selected palette slot to the primary colour. With <see cref="RecolourPixels"/>, every pixel of the
    /// old colour is recoloured too (a static palette swap), in the same undo step.
    /// </summary>
    [RelayCommand]
    private void ReplaceSwatch()
    {
        if (_swatchFirst < 0)
        {
            StatusMessage = "Click a palette swatch first, then set the primary colour to replace it with.";
            return;
        }
        int slot = _swatchFirst;
        uint oldColor = Document.Palette.Colors[slot], newColor = _primary | 0xFF000000;
        if (oldColor == newColor)
        {
            StatusMessage = "The swatch already has the primary colour. Change the primary colour first.";
            return;
        }

        if (RecolourPixels)
        {
            int changed = 0;
            ApplyStructural("Replace colour", d =>
            {
                changed = d.ReplaceColor(oldColor, newColor);
                d.Palette.Colors[slot] = newColor;
            });
            StatusMessage = $"Replaced {ColorUtil.ToHex(oldColor)} with {ColorUtil.ToHex(newColor)} in the palette and {changed} pixel(s).";
        }
        else
        {
            ApplyStructural("Replace palette colour", d => d.Palette.Colors[slot] = newColor, thumbnails: false, pixels: false);
            StatusMessage = $"Palette slot {slot} is now {ColorUtil.ToHex(newColor)}. Tick Recolour pixels to change the art as well.";
        }
        SelectSwatches(slot, slot);
        WarnAboutCycles();
    }

    [RelayCommand]
    private void MoveSwatchLeft() => MoveSwatch(-1);

    [RelayCommand]
    private void MoveSwatchRight() => MoveSwatch(1);

    private void MoveSwatch(int delta)
    {
        if (_swatchFirst < 0 || _swatchFirst != _swatchLast)
        {
            StatusMessage = "Click one palette swatch to move it.";
            return;
        }
        int from = _swatchFirst, to = from + delta;
        if (to < 0 || to >= Document.Palette.Colors.Count) return;
        ApplyStructural("Move palette colour", d =>
        {
            var colors = d.Palette.Colors;
            (colors[from], colors[to]) = (colors[to], colors[from]);
        }, thumbnails: false, pixels: false);
        SelectSwatches(to, to);
    }

    [RelayCommand]
    private void AddCycleFromSelection()
    {
        if (_swatchFirst < 0 || _swatchLast - _swatchFirst < 1)
        {
            StatusMessage = "Select at least two palette swatches first: click one, then Shift+click another.";
            return;
        }
        if (Document.Cycles.Count >= SpriteDocument.MaxCycles)
        {
            StatusMessage = $"A sprite can have at most {SpriteDocument.MaxCycles} colour cycles.";
            return;
        }
        int first = _swatchFirst, last = _swatchLast;
        var cycle = new ColorCycle { Name = $"Cycle {Document.Cycles.Count + 1}", First = first, Last = last };
        ApplyStructural("Add colour cycle", d => d.Cycles.Add(cycle), thumbnails: false, pixels: false);
        SelectedCycle = Cycles.LastOrDefault();
        StatusMessage = $"Added {cycle.Name} over palette slots {first}-{last}." + (CyclePreview ? "" : " Press F6 or Play cycles to see it.");
        WarnAboutCycles();
    }

    [RelayCommand]
    private void DeleteCycle()
    {
        if (SelectedCycle is not { } selected || selected.Index >= Document.Cycles.Count)
        {
            StatusMessage = "Select a colour cycle in the list first.";
            return;
        }
        int index = selected.Index;
        ApplyStructural("Delete colour cycle", d => d.Cycles.RemoveAt(index), thumbnails: false, pixels: false);
        SelectedCycle = null;
    }

    [RelayCommand]
    private void ToggleCyclePreview() => CyclePreview = !CyclePreview;

    /// <summary>Changes one colour cycle as an undoable step. Called by <see cref="CycleViewModel"/>.</summary>
    internal void EditCycle(int index, string name, Action<ColorCycle> change, string? mergeKey = null)
    {
        if (_syncingLists || index >= Document.Cycles.Count) return;
        ApplyStructural(name, d => change(d.Cycles[index]), mergeKey, syncLists: false, thumbnails: false, pixels: false);
        Cycles[index].Sync(index, Document.Cycles[index]);
        UpdateCycleTimer();
    }

    /// <summary>The selected row of the colour cycle list; selecting a cycle also selects its swatches.</summary>
    public CycleViewModel? SelectedCycle
    {
        get => _selectedCycle;
        set
        {
            if (!SetProperty(ref _selectedCycle, value) || value == null) return;
            SelectSwatches(value.First, value.Last);
        }
    }

    /// <summary>Replace also recolours every pixel of the old colour.</summary>
    [ObservableProperty]
    private bool _recolourPixels = true;

    private CycleViewModel? _selectedCycle;
    private int _swatchAnchor = -1, _swatchFirst = -1, _swatchLast = -1;

    private void SelectSwatches(int anchor, int other, bool keepAnchor = false)
    {
        int count = Document.Palette.Colors.Count;
        if (count == 0)
        {
            ClearSwatchSelection();
            return;
        }
        anchor = Math.Clamp(anchor, 0, count - 1);
        other = Math.Clamp(other, 0, count - 1);
        if (!keepAnchor || _swatchAnchor < 0) _swatchAnchor = anchor;
        (_swatchFirst, _swatchLast) = (Math.Min(anchor, other), Math.Max(anchor, other));
        foreach (var s in PaletteColors) s.IsSelected = s.Index >= _swatchFirst && s.Index <= _swatchLast;
    }

    private void ClearSwatchSelection()
    {
        _swatchAnchor = _swatchFirst = _swatchLast = -1;
        foreach (var s in PaletteColors) s.IsSelected = false;
    }

    /// <summary>Rebuilds the swatches when the palette changed, and refreshes selection and cycle markers.</summary>
    private void SyncPalette()
    {
        var colors = Document.Palette.Colors;
        if (!PaletteColors.Select(s => s.Color).SequenceEqual(colors))
        {
            PaletteColors.Clear();
            for (int i = 0; i < colors.Count; i++) PaletteColors.Add(new SwatchViewModel(i, colors[i]));
        }
        if (_swatchLast >= colors.Count) ClearSwatchSelection();
        foreach (var s in PaletteColors)
        {
            s.IsSelected = s.Index >= _swatchFirst && s.Index <= _swatchLast && _swatchFirst >= 0;
            s.InCycle = Document.Cycles.Any(c => s.Index >= c.First && s.Index <= c.Last);
        }
    }

    /// <summary>Warns in the status bar when a cycle range holds the same colour twice (only its first slot can cycle).</summary>
    private void WarnAboutCycles()
    {
        foreach (var c in Document.Cycles)
        {
            var colors = Document.Palette.Colors.Skip(c.First).Take(c.Length).ToList();
            if (colors.Distinct().Count() == colors.Count) continue;
            StatusMessage = $"{c.Name} has the same colour in more than one slot; pixels of that colour follow its first slot only.";
            return;
        }
    }

    [RelayCommand]
    private void LoadPalette()
    {
        string? path = _dialogs.OpenFile(DialogService.PaletteFilter, "Load Palette");
        if (path == null) return;
        try
        {
            var palette = Palette.Load(path);
            if (palette.Colors.Count == 0) throw new FormatException("The file has no colours.");
            ClearSwatchSelection();
            ApplyStructural("Load palette", d => d.Palette = palette, thumbnails: false, pixels: false);
            WarnAboutCycles();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Could not load the palette.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void SavePalette()
    {
        string? path = _dialogs.SaveFile(DialogService.PaletteFilter, "Save Palette", "palette.hex");
        if (path == null) return;
        try
        {
            Document.Palette.Save(path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Could not save the palette.\n\n{ex.Message}");
        }
    }

    // ---- Frames ----

    [RelayCommand]
    private void AddFrame()
    {
        if (!CheckBudget(Document.Width, Document.Height, Document.Layers.Count, Document.Frames.Count + 1)) return;
        int index = 0;
        ApplyStructural("Add frame", d => index = d.AddFrame(_activeFrame));
        ActiveFrameIndex = index;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
        if (!CheckBudget(Document.Width, Document.Height, Document.Layers.Count, Document.Frames.Count + 1)) return;
        int index = 0;
        ApplyStructural("Duplicate frame", d => index = d.DuplicateFrame(_activeFrame));
        ActiveFrameIndex = index;
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        if (Document.Frames.Count <= 1)
        {
            StatusMessage = "A sprite needs at least one frame.";
            return;
        }
        ApplyStructural("Delete frame", d => d.DeleteFrame(_activeFrame));
    }

    [RelayCommand]
    private void MoveFrameLeft() => MoveFrame(-1);

    [RelayCommand]
    private void MoveFrameRight() => MoveFrame(1);

    private void MoveFrame(int delta)
    {
        FinishPendingEdits();
        int from = _activeFrame, to = from + delta;
        if (to < 0 || to >= Document.Frames.Count) return;
        _activeFrame = to;
        ApplyStructural("Move frame", d => d.MoveFrame(from, to));
    }

    [RelayCommand]
    private void PreviousFrame() =>
        ActiveFrameIndex = (_activeFrame - 1 + Document.Frames.Count) % Document.Frames.Count;

    [RelayCommand]
    private void NextFrame() => ActiveFrameIndex = (_activeFrame + 1) % Document.Frames.Count;

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying) StopPlayback();
        else StartPlayback();
    }

    [RelayCommand]
    private void SetAllFps()
    {
        if (!double.TryParse(FpsText, out double fps) || fps <= 0 || fps > 100)
        {
            StatusMessage = "FPS must be a number between 1 and 100.";
            return;
        }
        int ms = Math.Clamp((int)Math.Round(1000 / fps), Frame.MinDurationMs, Frame.MaxDurationMs);
        ApplyStructural("Set frame rate", d => d.Frames.ForEach(f => f.DurationMs = ms), thumbnails: false, pixels: false);
        StatusMessage = $"All frames set to {ms} ms";
    }

    // ---- Layers ----

    [RelayCommand]
    private void AddLayer()
    {
        if (!CheckBudget(Document.Width, Document.Height, Document.Layers.Count + 1, Document.Frames.Count)) return;
        int index = 0;
        ApplyStructural("Add layer", d => index = d.AddLayer(_activeLayer), thumbnails: false);
        ActiveLayerIndex = index;
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
        if (!CheckBudget(Document.Width, Document.Height, Document.Layers.Count + 1, Document.Frames.Count)) return;
        int index = 0;
        ApplyStructural("Duplicate layer", d => index = d.DuplicateLayer(_activeLayer));
        ActiveLayerIndex = index;
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        if (Document.Layers.Count <= 1)
        {
            StatusMessage = "A sprite needs at least one layer.";
            return;
        }
        ApplyStructural("Delete layer", d => d.DeleteLayer(_activeLayer));
    }

    [RelayCommand]
    private void MoveLayerUp() => MoveLayer(1);

    [RelayCommand]
    private void MoveLayerDown() => MoveLayer(-1);

    private void MoveLayer(int delta)
    {
        FinishPendingEdits();
        int from = _activeLayer, to = from + delta;
        if (to < 0 || to >= Document.Layers.Count) return;
        _activeLayer = to;
        ApplyStructural("Move layer", d => d.MoveLayer(from, to));
    }

    [RelayCommand]
    private void MergeDown()
    {
        if (_activeLayer == 0)
        {
            StatusMessage = "There is no layer below to merge into.";
            return;
        }
        FinishPendingEdits();
        int layer = _activeLayer;
        _activeLayer = layer - 1;
        ApplyStructural("Merge down", d => d.MergeDown(layer));
    }

    internal void SetLayerName(int index, string name)
    {
        if (_syncingLists || index >= Document.Layers.Count) return;
        ApplyStructural("Rename layer", d => d.Layers[index].Name = name, syncLists: false, thumbnails: false, pixels: false);
    }

    internal void SetLayerVisible(int index, bool visible)
    {
        if (_syncingLists || index >= Document.Layers.Count) return;
        ApplyStructural(visible ? "Show layer" : "Hide layer", d => d.Layers[index].Visible = visible, syncLists: false, pixels: false);
    }

    // ---- Image ----

    [RelayCommand]
    private void ResizeCanvas()
    {
        FinishPendingEdits();
        if (_dialogs.AskResize(Document.Width, Document.Height) is not { } size) return;
        if (!CheckBudget(size.Width, size.Height, Document.Layers.Count, Document.Frames.Count)) return;
        Selection = null;
        ApplyStructural("Resize canvas", d => d.Resize(size.Width, size.Height, size.Anchor));
        Zoom = Zoom;   // re-clamp for the new size
    }

    [RelayCommand]
    private void FlipHorizontal() => ApplyStructural("Flip horizontal", d => d.FlipHorizontal());

    [RelayCommand]
    private void FlipVertical() => ApplyStructural("Flip vertical", d => d.FlipVertical());

    [RelayCommand]
    private void RotateClockwise() => Rotate(true);

    [RelayCommand]
    private void RotateCounterClockwise() => Rotate(false);

    private void Rotate(bool clockwise)
    {
        FinishPendingEdits();
        Selection = null;
        ApplyStructural(clockwise ? "Rotate right" : "Rotate left", d => d.Rotate90(clockwise));
    }

    // ---- Selection and clipboard ----

    [RelayCommand]
    private void SelectAll()
    {
        FinishPendingEdits();
        CurrentTool = ToolKind.Select;
        Selection = ActiveCel.Bounds;
    }

    [RelayCommand]
    private void Deselect()
    {
        _strokeTool?.End(this);
        _strokeTool = null;
        CommitFloating();
        Selection = null;
    }

    [RelayCommand]
    private void DeleteSelection()
    {
        if (_floating is { } floating)
        {
            // The lifted area was already cleared; dropping the floating pixels deletes them.
            _floating = null;
            var cel = Document.GetCel(floating.FrameIndex, floating.LayerIndex);
            var edit = new CelEdit("Delete", floating.FrameIndex, floating.LayerIndex, floating.CelBefore, cel);
            if (!edit.IsEmpty) History.Push(edit);
            Selection = null;
            RefreshThumbnail(floating.FrameIndex);
            UpdateDisplayOverride();
            RaiseCanvasChanged(null);
            return;
        }

        if (_selection is not { } rect) return;
        FinishPendingEdits();
        var before = ActiveCel.Clone();
        Sel.Clear(ActiveCel, rect);
        Commit("Delete", before);
        RaiseCanvasChanged(null);
    }

    [RelayCommand]
    private void Copy()
    {
        var pixels = _floating?.Pixels.Clone() ?? Sel.Extract(ActiveCel, _selection ?? ActiveCel.Bounds);
        _clipboard = pixels;
        StatusMessage = ClipboardService.TrySetImage(pixels)
            ? $"Copied {pixels.Width} x {pixels.Height}"
            : "Copied (the Windows clipboard was busy, so only PixelArtStudio can paste it)";
    }

    [RelayCommand]
    private void Cut()
    {
        Copy();
        if (_floating == null && _selection == null)
        {
            var before = ActiveCel.Clone();
            ActiveCel.Clear();
            Commit("Cut", before);
            RaiseCanvasChanged(null);
            return;
        }
        DeleteSelection();
    }

    [RelayCommand]
    private void Paste()
    {
        FinishPendingEdits();
        var pixels = ClipboardService.TryGetImage() ?? _clipboard?.Clone();
        if (pixels == null)
        {
            StatusMessage = "There is no image on the clipboard.";
            return;
        }

        int x = _selection?.X ?? 0, y = _selection?.Y ?? 0;
        _floating = new FloatingSelection(pixels, x, y, _activeFrame, _activeLayer, ActiveCel.Clone(), "Paste");
        CurrentTool = ToolKind.Select;
        Selection = _floating.Bounds;
        UpdateDisplayOverride();
        RaiseCanvasChanged(null);
        StatusMessage = "Pasted. Drag to move; press Enter or pick another tool to place it.";
    }

    [RelayCommand]
    private void CommitSelection() => CommitFloating();

    // ---- View ----

    [RelayCommand]
    private void ZoomIn() => Zoom = ZoomLevels.FirstOrDefault(z => z > Zoom + 1e-9, ZoomLevels[^1]);

    [RelayCommand]
    private void ZoomOut() => Zoom = ZoomLevels.LastOrDefault(z => z < Zoom - 1e-9, ZoomLevels[0]);

    [RelayCommand]
    private void ZoomFit() => Zoom = FitZoom();

    [RelayCommand]
    private void ZoomActual() => Zoom = 1;

    [RelayCommand]
    private void ToggleGrid() => ShowGrid = !ShowGrid;

    [RelayCommand]
    private void SetTileGrid(string? size) => TileGridSize = int.TryParse(size, out int n) ? n : 0;
}
