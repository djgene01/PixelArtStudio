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
    private void Undo()
    {
        FinishPendingEdits();
        var entry = History.PeekUndo;
        if (History.Undo(Document)) AfterHistoryStep(entry, "Undo");
    }

    private bool CanUndo() => History.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        FinishPendingEdits();
        var entry = History.PeekRedo;
        if (History.Redo(Document)) AfterHistoryStep(entry, "Redo");
    }

    private bool CanRedo() => History.CanRedo;

    // ---- Tools and colours ----

    [RelayCommand]
    private void SetTool(ToolKind tool) => CurrentTool = tool;

    [RelayCommand]
    private void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    [RelayCommand]
    private void BrushBigger() => BrushSize++;

    [RelayCommand]
    private void BrushSmaller() => BrushSize--;

    [RelayCommand]
    private void PickSwatch(SwatchViewModel? swatch)
    {
        if (swatch != null) PrimaryColor = swatch.Color;
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
        ApplyStructural("Add palette colour", d => d.Palette.Colors.Add(color), thumbnails: false);
    }

    [RelayCommand]
    private void RemoveFromPalette()
    {
        uint color = _primary | 0xFF000000;
        if (!Document.Palette.Colors.Contains(color))
        {
            StatusMessage = "Pick a palette colour first, then remove it.";
            return;
        }
        ApplyStructural("Remove palette colour", d => d.Palette.Colors.Remove(color), thumbnails: false);
    }

    [RelayCommand]
    private void ResetPalette() =>
        ApplyStructural("Reset palette", d => d.Palette = Palette.CreateDefault(), thumbnails: false);

    [RelayCommand]
    private void LoadPalette()
    {
        string? path = _dialogs.OpenFile(DialogService.PaletteFilter, "Load Palette");
        if (path == null) return;
        try
        {
            var palette = Palette.Load(path);
            if (palette.Colors.Count == 0) throw new FormatException("The file has no colours.");
            ApplyStructural("Load palette", d => d.Palette = palette, thumbnails: false);
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
        int index = 0;
        ApplyStructural("Add frame", d => index = d.AddFrame(_activeFrame));
        ActiveFrameIndex = index;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
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
        ApplyStructural("Set frame rate", d => d.Frames.ForEach(f => f.DurationMs = ms), thumbnails: false);
        StatusMessage = $"All frames set to {ms} ms";
    }

    // ---- Layers ----

    [RelayCommand]
    private void AddLayer()
    {
        int index = 0;
        ApplyStructural("Add layer", d => index = d.AddLayer(_activeLayer), thumbnails: false);
        ActiveLayerIndex = index;
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
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
        ApplyStructural("Rename layer", d => d.Layers[index].Name = name, syncLists: false, thumbnails: false);
    }

    internal void SetLayerVisible(int index, bool visible)
    {
        if (_syncingLists || index >= Document.Layers.Count) return;
        ApplyStructural(visible ? "Show layer" : "Hide layer", d => d.Layers[index].Visible = visible, syncLists: false);
    }

    // ---- Image ----

    [RelayCommand]
    private void ResizeCanvas()
    {
        FinishPendingEdits();
        if (_dialogs.AskResize(Document.Width, Document.Height) is not { } size) return;
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
            if (!cel.ContentEquals(floating.CelBefore))
                History.Push(new CelEdit("Delete", floating.FrameIndex, floating.LayerIndex, floating.CelBefore, cel.Clone()));
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
    private void ZoomIn() => Zoom = ZoomLevels.FirstOrDefault(z => z > Zoom, ZoomLevels[^1]);

    [RelayCommand]
    private void ZoomOut() => Zoom = ZoomLevels.LastOrDefault(z => z < Zoom, ZoomLevels[0]);

    [RelayCommand]
    private void ZoomFit() => Zoom = FitZoom();
}
