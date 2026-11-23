using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PixelArtStudio.Core.Drawing;
using PixelArtStudio.Core.History;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Services;
using PixelArtStudio.Tools;
using Sel = PixelArtStudio.Core.Drawing.Selection;

namespace PixelArtStudio.ViewModels;

/// <summary>
/// Editor state: the document, history, active frame/layer, colours and tool settings. It is also the
/// <see cref="IToolContext"/> tools draw through, and feeds the canvas via <see cref="ComposeInto"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IToolContext
{
    public static readonly double[] ZoomLevels = [0.125, 0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    /// <summary>Largest on-screen canvas size in DIPs; limits zoom on big sprites.</summary>
    private const int MaxDisplaySize = 32768;

    /// <summary>Space kept around the canvas in the workspace (the canvas margin on both sides).</summary>
    private const double CanvasMargin = 96;

    /// <summary>Long side of timeline thumbnails and the preview; bigger frames are shrunk.</summary>
    private const int ThumbnailSize = 256;

    private readonly DialogService _dialogs;
    private readonly Dictionary<ToolKind, ITool> _tools;
    private readonly DispatcherTimer _playTimer = new();
    private readonly DispatcherTimer _previewTimer = new();
    private readonly DispatcherTimer _cycleTimer = new();
    private readonly Stopwatch _cycleClock = new();

    /// <summary>Full-size flattened frames for onion skin, built on demand.</summary>
    private readonly Dictionary<int, BitmapSource> _onionCache = [];

    private int _activeFrame;
    private int _activeLayer;
    private double _zoom = 8;
    private int _brushSize = 1;
    private uint _primary = 0xFF000000;
    private uint _secondary = 0xFFFFFFFF;
    private string _documentName = "Untitled";
    private bool _syncingLists;
    private int _previewFrame;

    private ITool? _strokeTool;
    private PointerButton _strokeButton;
    private PixelBuffer? _preview;
    private FloatingSelection? _floating;
    private PixelBuffer? _displayOverride;
    private RectI? _selection;

    // While a selection floats: the active cel with the floating pixels pasted on, and where they were pasted.
    private PixelBuffer? _overlay;
    private RectI? _overlayBounds;

    private ColorCycleMap? _cycleMap;
    private int[]? _cycleSteps;

    public MainViewModel(DialogService dialogs)
    {
        _dialogs = dialogs;
        _tools = new ITool[]
        {
            new PencilTool(), new EraserTool(), new FillTool(), new EyedropperTool(),
            new LineTool(), new RectangleTool(), new EllipseTool(), new SelectTool(),
        }.ToDictionary(t => t.Kind);

        History.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        _playTimer.Tick += OnPlayTick;
        _previewTimer.Tick += OnPreviewTick;
        _cycleTimer.Tick += OnCycleTick;
        foreach (string path in RecentFiles.Load()) RecentFilePaths.Add(path);

        Document = SpriteDocument.Create(32, 32);
        LoadDocument(Document, null, "Untitled");
        _previewTimer.Interval = TimeSpan.FromMilliseconds(100);
        _previewTimer.Start();
    }

    /// <summary>Raised when canvas pixels change; the region is null when everything (or the size) changed.</summary>
    public event Action<RectI?>? CanvasChanged;

    /// <summary>Raised when only overlays (selection marquee) changed.</summary>
    public event Action? OverlayChanged;

    public SpriteDocument Document { get; private set; }
    public UndoStack History { get; } = new(100);

    public ObservableCollection<FrameViewModel> Frames { get; } = [];

    /// <summary>Layers in display order: top layer first.</summary>
    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    public ObservableCollection<SwatchViewModel> PaletteColors { get; } = [];
    public ObservableCollection<CycleViewModel> Cycles { get; } = [];
    public ObservableCollection<string> RecentFilePaths { get; } = [];

    // ---- Document / status ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _filePath;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _cursorText = "";

    [ObservableProperty]
    private bool _isBusy;

    public bool IsDirty => History.IsDirty;
    public string DocumentName => _documentName;
    public string Title => $"{_documentName}{(IsDirty ? " *" : "")} - PixelArtStudio";
    public string SizeText => $"{Document.Width} x {Document.Height} px";
    public string ZoomText => $"{Zoom * 100:0.#}%";
    public string FrameText => $"Frame {_activeFrame + 1} / {Document.Frames.Count}";
    public string SelectionText => _selection is { } s ? $"Selection {s.Width} x {s.Height}" : "";

    // ---- Tool settings ----

    [ObservableProperty]
    private ToolKind _currentTool = ToolKind.Pencil;

    [ObservableProperty]
    private bool _mirrorX;

    [ObservableProperty]
    private bool _mirrorY;

    [ObservableProperty]
    private bool _fillContiguous = true;

    [ObservableProperty]
    private bool _shapeFilled;

    [ObservableProperty]
    private bool _showGrid = true;

    /// <summary>Spacing of the tile grid in pixels; 0 hides it.</summary>
    [ObservableProperty]
    private int _tileGridSize;

    [ObservableProperty]
    private bool _onionSkin;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Colour cycles are animating on the canvas.</summary>
    [ObservableProperty]
    private bool _cyclePreview;

    [ObservableProperty]
    private string _fpsText = "10";

    partial void OnCurrentToolChanged(ToolKind oldValue, ToolKind newValue)
    {
        _strokeTool?.End(this);
        _strokeTool = null;
        if (newValue != ToolKind.Select) CommitFloating();
        StatusMessage = _tools[newValue].Name;
    }

    partial void OnOnionSkinChanged(bool value) => RaiseCanvasChanged(null);
    partial void OnShowGridChanged(bool value) => OverlayChanged?.Invoke();
    partial void OnTileGridSizeChanged(int value) => OverlayChanged?.Invoke();

    partial void OnCyclePreviewChanged(bool value)
    {
        if (value)
        {
            if (!Document.Cycles.Any(c => c.Enabled))
                StatusMessage = "There are no enabled colour cycles. Shift+click palette swatches to pick a range, then add a cycle.";
            _cycleClock.Restart();
            UpdateCycleTimer();
            _cycleTimer.Start();
        }
        else
        {
            _cycleTimer.Stop();
            _cycleClock.Reset();
        }
        _cycleSteps = null;
        RefreshCycleMap();
        RaiseCanvasChanged(null);
    }

    public int BrushSize
    {
        get => _brushSize;
        set => SetProperty(ref _brushSize, Math.Clamp(value, 1, Raster.MaxBrushSize));
    }

    /// <summary>Screen pixels per sprite pixel. Below 1 the canvas is shown shrunk.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double max = Math.Max(ZoomLevels[0], Math.Min(ZoomLevels[^1], MaxDisplaySize / (double)Math.Max(Document.Width, Document.Height)));
            if (SetProperty(ref _zoom, Math.Clamp(value, ZoomLevels[0], max))) OnPropertyChanged(nameof(ZoomText));
        }
    }

    /// <summary>Size of the workspace around the canvas, set by the window; used by Fit.</summary>
    public Size ViewportSize { get; set; }

    // ---- Colours ----

    public uint PrimaryColor
    {
        get => _primary;
        set
        {
            _primary = value;
            OnPropertyChanged(nameof(PrimaryColor));
            OnPropertyChanged(nameof(PrimaryBrush));
            OnPropertyChanged(nameof(Red));
            OnPropertyChanged(nameof(Green));
            OnPropertyChanged(nameof(Blue));
            OnPropertyChanged(nameof(Alpha));
            OnPropertyChanged(nameof(PrimaryHex));
        }
    }

    public uint SecondaryColor
    {
        get => _secondary;
        set
        {
            _secondary = value;
            OnPropertyChanged(nameof(SecondaryColor));
            OnPropertyChanged(nameof(SecondaryBrush));
        }
    }

    public Brush PrimaryBrush => BitmapHelper.ToBrush(_primary);
    public Brush SecondaryBrush => BitmapHelper.ToBrush(_secondary);

    public int Red
    {
        get => ColorUtil.R(_primary);
        set => PrimaryColor = (_primary & 0xFF00FFFF) | (uint)Math.Clamp(value, 0, 255) << 16;
    }

    public int Green
    {
        get => ColorUtil.G(_primary);
        set => PrimaryColor = (_primary & 0xFFFF00FF) | (uint)Math.Clamp(value, 0, 255) << 8;
    }

    public int Blue
    {
        get => ColorUtil.B(_primary);
        set => PrimaryColor = (_primary & 0xFFFFFF00) | (uint)Math.Clamp(value, 0, 255);
    }

    public int Alpha
    {
        get => ColorUtil.A(_primary);
        set => PrimaryColor = (_primary & 0x00FFFFFF) | (uint)Math.Clamp(value, 0, 255) << 24;
    }

    /// <summary>Primary colour as <c>#RRGGBB</c> or <c>#RRGGBBAA</c>. Invalid input is ignored.</summary>
    public string PrimaryHex
    {
        get => ColorUtil.ToHex(_primary);
        set
        {
            if (ColorUtil.TryParseHex(value, out uint c)) PrimaryColor = c;
            else OnPropertyChanged(nameof(PrimaryHex));
        }
    }

    // ---- Active frame and layer ----

    public int ActiveFrameIndex
    {
        get => _activeFrame;
        set
        {
            if (_syncingLists || value < 0 || value >= Document.Frames.Count || value == _activeFrame) return;
            FinishPendingEdits();
            _activeFrame = value;
            foreach (var frame in Frames) frame.IsActive = frame.Index == value;
            OnPropertyChanged(nameof(ActiveFrameIndex));
            OnPropertyChanged(nameof(ActiveFrameDuration));
            OnPropertyChanged(nameof(FrameText));
            RaiseCanvasChanged(null);
        }
    }

    public int ActiveLayerIndex
    {
        get => _activeLayer;
        set
        {
            if (_syncingLists || value < 0 || value >= Document.Layers.Count || value == _activeLayer) return;
            FinishPendingEdits();
            _activeLayer = value;
            OnPropertyChanged(nameof(ActiveLayerIndex));
            OnPropertyChanged(nameof(SelectedLayer));
            OnPropertyChanged(nameof(ActiveLayerOpacity));
            RaiseCanvasChanged(null);
        }
    }

    /// <summary>The selected row of the layer list.</summary>
    public LayerViewModel? SelectedLayer
    {
        get => Layers.FirstOrDefault(l => l.Index == _activeLayer);
        set
        {
            if (value != null) ActiveLayerIndex = value.Index;
        }
    }

    public int ActiveFrameDuration
    {
        get => Document.Frames[_activeFrame].DurationMs;
        set
        {
            int frame = _activeFrame;
            int ms = Math.Clamp(value, Frame.MinDurationMs, Frame.MaxDurationMs);
            if (ms == Document.Frames[frame].DurationMs) return;
            ApplyStructural("Frame duration", d => d.Frames[frame].DurationMs = ms, mergeKey: $"duration:{frame}",
                syncLists: false, thumbnails: false, pixels: false);
            Frames[frame].DurationMs = ms;
            OnPropertyChanged();
        }
    }

    public int ActiveLayerOpacity
    {
        get => (int)Math.Round(Document.Layers[_activeLayer].Opacity * 100);
        set
        {
            int layer = _activeLayer;
            double opacity = Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(opacity - Document.Layers[layer].Opacity) < 0.001) return;
            ApplyStructural("Layer opacity", d => d.Layers[layer].Opacity = opacity, mergeKey: $"opacity:{layer}",
                syncLists: false, thumbnails: true, pixels: false);
            SelectedLayer?.Sync(layer, Document.Layers[layer]);
            OnPropertyChanged();
        }
    }

    public BitmapSource? PreviewImage => _previewFrame < Frames.Count ? Frames[_previewFrame].Thumbnail : null;

    // ---- IToolContext ----

    public PixelBuffer ActiveCel => Document.GetCel(_activeFrame, _activeLayer);
    public PaintOptions PaintOptions => new(BrushSize, MirrorX, MirrorY, _selection);
    public FloatingSelection? Floating => _floating;
    public bool HasSelection => _selection != null;

    public RectI? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionText));
            OverlayChanged?.Invoke();
        }
    }

    public uint ColorFor(PointerButton button) => button == PointerButton.Left ? _primary : _secondary;

    public void PickColor(uint color, PointerButton button)
    {
        if (button == PointerButton.Left) PrimaryColor = color;
        else SecondaryColor = color;
    }

    public uint SampleComposite(int x, int y)
    {
        uint result = 0;
        var frame = Document.Frames[_activeFrame];
        for (int l = 0; l < Document.Layers.Count; l++)
        {
            var layer = Document.Layers[l];
            if (layer.Visible) result = Compositor.BlendOver(result, frame.Cels[l].Get(x, y), layer.Opacity);
        }
        return result;
    }

    public void SetPreview(PixelBuffer? preview, RectI? dirty = null)
    {
        _preview = preview;
        UpdateDisplayOverride(rebuild: _floating != null);
        RaiseCanvasChanged(_floating != null ? null : dirty);
    }

    public void Invalidate(RectI? region) => RaiseCanvasChanged(region);

    public void Commit(string name, PixelBuffer before)
    {
        var edit = new CelEdit(name, _activeFrame, _activeLayer, before, ActiveCel);
        if (edit.IsEmpty) return;
        History.Push(edit);
        RefreshThumbnail(_activeFrame);
    }

    public void LiftSelection()
    {
        if (_selection is not { } rect || _floating != null) return;
        var cel = ActiveCel;
        var before = cel.Clone();
        var pixels = Sel.Extract(cel, rect);
        Sel.Clear(cel, rect);
        _floating = new FloatingSelection(pixels, rect.X, rect.Y, _activeFrame, _activeLayer, before, "Move selection");
        UpdateDisplayOverride(rebuild: true);
        RaiseCanvasChanged(rect);
    }

    public void MoveFloating(int dx, int dy)
    {
        if (_floating == null) return;
        _floating.X += dx;
        _floating.Y += dy;
        _selection = _floating.Bounds;
        RaiseCanvasChanged(UpdateDisplayOverride());
        OverlayChanged?.Invoke();
    }

    public void CommitFloating()
    {
        if (_floating is not { } floating) return;
        _floating = null;
        var cel = Document.GetCel(floating.FrameIndex, floating.LayerIndex);
        Sel.Paste(cel, floating.Pixels, floating.X, floating.Y, skipTransparent: true);
        var edit = new CelEdit(floating.Name, floating.FrameIndex, floating.LayerIndex, floating.CelBefore, cel);
        if (!edit.IsEmpty) History.Push(edit);

        var bounds = floating.Bounds.Intersect(cel.Bounds);
        _selection = bounds.IsEmpty ? null : bounds;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        RefreshThumbnail(floating.FrameIndex);
        UpdateDisplayOverride();
        RaiseCanvasChanged(bounds);
        OverlayChanged?.Invoke();
    }

    /// <summary>Moves the selected pixels by one step, lifting them first if only a marquee is selected.</summary>
    public bool NudgeSelection(int dx, int dy)
    {
        if (_selection == null) return false;
        if (_floating == null)
        {
            if (!Document.Layers[_activeLayer].Visible)
            {
                StatusMessage = "The active layer is hidden. Show it to move pixels on it.";
                return true;
            }
            CurrentTool = ToolKind.Select;
            LiftSelection();
        }
        MoveFloating(dx, dy);
        return true;
    }

    // ---- Canvas input and rendering ----

    /// <summary>Starts a tool stroke. Returns false if the tool cannot be used right now.</summary>
    public bool PointerDown(int x, int y, PointerButton button, ModifierKeys modifiers)
    {
        StopPlayback();
        var tool = _tools[CurrentTool];
        if (tool.Edits && !Document.Layers[_activeLayer].Visible)
        {
            StatusMessage = "The active layer is hidden. Show it to draw on it.";
            return false;
        }
        _strokeTool = tool;
        _strokeButton = button;
        tool.OnDown(this, new ToolInput(x, y, button, modifiers));
        return true;
    }

    public void PointerMove(int x, int y, ModifierKeys modifiers) =>
        _strokeTool?.OnMove(this, new ToolInput(x, y, _strokeButton, modifiers));

    public void PointerUp(int x, int y, ModifierKeys modifiers)
    {
        var tool = _strokeTool;
        _strokeTool = null;
        tool?.OnUp(this, new ToolInput(x, y, _strokeButton, modifiers));
    }

    /// <summary>Shows the pixel under the cursor and its colour (the stored colour, not the cycled one).</summary>
    public void SetCursor(int? x, int? y)
    {
        if (x is not { } px || y is not { } py || px < 0 || py < 0 || px >= Document.Width || py >= Document.Height)
        {
            CursorText = "";
            return;
        }
        uint color = SampleComposite(px, py);
        CursorText = color >> 24 == 0 ? $"{px}, {py}" : $"{px}, {py}  {ColorUtil.ToHex(color)}";
    }

    /// <summary>Writes the composited active frame (with tool preview, floating selection and colour cycling) into <paramref name="target"/>.</summary>
    public void ComposeInto(PixelBuffer target, RectI region) =>
        Compositor.FlattenInto(target, Document, _activeFrame, region, _activeLayer, _displayOverride, _cycleMap);

    /// <summary>Previous and next frames for onion skinning, when enabled.</summary>
    public IEnumerable<BitmapSource> OnionSkinFrames()
    {
        if (!OnionSkin || IsPlaying) yield break;
        if (_activeFrame > 0) yield return OnionFrame(_activeFrame - 1);
        if (_activeFrame < Document.Frames.Count - 1) yield return OnionFrame(_activeFrame + 1);
    }

    private BitmapSource OnionFrame(int index)
    {
        if (!_onionCache.TryGetValue(index, out var bitmap))
            _onionCache[index] = bitmap = BitmapHelper.ToBitmapSource(Compositor.Flatten(Document, index));
        return bitmap;
    }

    // ---- Internals ----

    /// <summary>Finishes the current stroke and merges any floating selection, before anything else changes the document.</summary>
    private void FinishPendingEdits()
    {
        _strokeTool?.End(this);
        _strokeTool = null;
        CommitFloating();
    }

    /// <summary>
    /// Updates what the canvas shows in place of the active cel. While a selection floats, the overlay buffer is
    /// reused: only the area under the previous position is restored. Returns the area that changed, or null for all.
    /// </summary>
    private RectI? UpdateDisplayOverride(bool rebuild = false)
    {
        if (_floating is not { } floating)
        {
            _overlay = null;
            _overlayBounds = null;
            _displayOverride = _preview;
            return null;
        }

        var source = _preview ?? ActiveCel;
        RectI? dirty;
        if (_overlay == null || _overlay.Width != source.Width || _overlay.Height != source.Height)
        {
            _overlay = source.Clone();
            dirty = null;
        }
        else if (rebuild || _overlayBounds == null)
        {
            _overlay.CopyFrom(source);
            dirty = null;
        }
        else
        {
            var old = _overlayBounds.Value;
            _overlay.CopyFrom(source, old);
            dirty = old.Union(floating.Bounds);
        }

        Sel.Paste(_overlay, floating.Pixels, floating.X, floating.Y, skipTransparent: true);
        _overlayBounds = floating.Bounds;
        _displayOverride = _overlay;
        return dirty;
    }

    private void RaiseCanvasChanged(RectI? region) => CanvasChanged?.Invoke(region);

    /// <summary>Shows a status message and returns false when a document of this shape would be too large.</summary>
    private bool CheckBudget(int width, int height, int layers, int frames)
    {
        if (SpriteDocument.FitsBudget(width, height, layers, frames)) return true;
        StatusMessage = SpriteDocument.BudgetMessage(width, height, layers, frames);
        return false;
    }

    /// <summary>
    /// Applies a change as one undo step. With <paramref name="pixels"/> it keeps whole-document snapshots (sharing
    /// unchanged cels); without, it records only palette, cycles, layer properties and frame durations.
    /// </summary>
    /// <exception cref="InvalidOperationException">The change would make the document too large; it is undone.</exception>
    private void ApplyStructural(string name, Action<SpriteDocument> change, string? mergeKey = null,
        bool syncLists = true, bool thumbnails = true, bool pixels = true)
    {
        FinishPendingEdits();
        if (pixels)
        {
            var before = Document.Clone();
            change(Document);
            if (!Document.FitsBudget())
            {
                string message = SpriteDocument.BudgetMessage(Document.Width, Document.Height, Document.Layers.Count, Document.Frames.Count);
                Document.CopyStateFrom(before);
                SyncLists(thumbnails: true);
                throw new InvalidOperationException(message);
            }
            Document.NormalizeCycles();
            History.Push(DocumentSnapshot.Capture(name, before, Document, mergeKey));
        }
        else
        {
            var before = DocumentMeta.Capture(Document);
            change(Document);
            Document.NormalizeCycles();
            History.Push(new MetaEdit(name, before, DocumentMeta.Capture(Document), mergeKey));
        }

        if (syncLists)
        {
            SyncLists(thumbnails);
            return;
        }
        if (thumbnails) RefreshAllThumbnails();
        _cycleSteps = null;
        RefreshCycleMap();
        RaiseCanvasChanged(null);
    }

    /// <summary>Brings the frame, layer, palette and cycle lists in line with the document.</summary>
    private void SyncLists(bool thumbnails)
    {
        _syncingLists = true;
        try
        {
            _activeFrame = Math.Clamp(_activeFrame, 0, Document.Frames.Count - 1);
            _activeLayer = Math.Clamp(_activeLayer, 0, Document.Layers.Count - 1);
            if (thumbnails) _onionCache.Clear();

            while (Frames.Count > Document.Frames.Count) Frames.RemoveAt(Frames.Count - 1);
            while (Frames.Count < Document.Frames.Count) Frames.Add(new FrameViewModel());
            for (int i = 0; i < Frames.Count; i++)
            {
                var frame = Frames[i];
                frame.Index = i;
                frame.DurationMs = Document.Frames[i].DurationMs;
                frame.IsActive = i == _activeFrame;
                if (thumbnails || frame.Thumbnail == null) RefreshThumbnail(i);
            }

            while (Layers.Count > Document.Layers.Count) Layers.RemoveAt(Layers.Count - 1);
            while (Layers.Count < Document.Layers.Count) Layers.Add(new LayerViewModel(this));
            for (int row = 0; row < Layers.Count; row++)
            {
                int index = Document.Layers.Count - 1 - row;
                Layers[row].Sync(index, Document.Layers[index]);
            }

            SyncPalette();

            while (Cycles.Count > Document.Cycles.Count) Cycles.RemoveAt(Cycles.Count - 1);
            while (Cycles.Count < Document.Cycles.Count) Cycles.Add(new CycleViewModel(this));
            for (int i = 0; i < Cycles.Count; i++) Cycles[i].Sync(i, Document.Cycles[i]);

            _previewFrame = Math.Min(_previewFrame, Frames.Count - 1);
        }
        finally
        {
            _syncingLists = false;
        }

        _cycleSteps = null;
        RefreshCycleMap();
        UpdateCycleTimer();
        OnPropertyChanged(string.Empty);
        RaiseCanvasChanged(null);
    }

    private void RefreshThumbnail(int frameIndex)
    {
        _onionCache.Remove(frameIndex);
        if (frameIndex < Frames.Count)
            Frames[frameIndex].Thumbnail = BitmapHelper.ToBitmapSource(Compositor.FlattenScaled(Document, frameIndex, ThumbnailSize));
        if (frameIndex == _previewFrame) OnPropertyChanged(nameof(PreviewImage));
    }

    private void RefreshAllThumbnails()
    {
        for (int i = 0; i < Frames.Count; i++) RefreshThumbnail(i);
    }

    private void AfterHistoryStep(IUndoEntry? entry, string verb)
    {
        _strokeTool = null;
        _preview = null;
        _displayOverride = null;
        if (entry is CelEdit edit && edit.FrameIndex < Document.Frames.Count && edit.LayerIndex < Document.Layers.Count)
        {
            _activeFrame = edit.FrameIndex;
            _activeLayer = edit.LayerIndex;
            SyncLists(thumbnails: false);
            RefreshThumbnail(edit.FrameIndex);
        }
        else
        {
            if (entry is not MetaEdit) Selection = null;
            SyncLists(thumbnails: true);
        }
        StatusMessage = entry != null ? $"{verb}: {entry.Name}" : verb;
    }

    private void LoadDocument(SpriteDocument doc, string? path, string name)
    {
        StopPlayback();
        CyclePreview = false;
        _strokeTool?.End(this);
        _strokeTool = null;
        _floating = null;
        _preview = null;
        _displayOverride = null;
        _overlay = null;
        _overlayBounds = null;
        _selection = null;
        _onionCache.Clear();
        ClearSwatchSelection();

        Document = doc;
        _documentName = name;
        FilePath = path;
        _activeFrame = 0;
        _activeLayer = doc.Layers.Count - 1;
        _previewFrame = 0;
        History.Clear();
        Zoom = FitZoom();
        SyncLists(thumbnails: true);
    }

    /// <summary>The largest zoom level at which the whole canvas fits the workspace.</summary>
    private double FitZoom()
    {
        int w = Document.Width, h = Document.Height;
        if (ViewportSize.Width > CanvasMargin && ViewportSize.Height > CanvasMargin)
            return ZoomLevels.LastOrDefault(z => w * z + CanvasMargin <= ViewportSize.Width && h * z + CanvasMargin <= ViewportSize.Height, ZoomLevels[0]);

        // Before the window is laid out: aim for about 640 px.
        int largest = Math.Max(w, h);
        return ZoomLevels.LastOrDefault(z => largest * z <= 640, ZoomLevels[0]);
    }

    // ---- Colour cycling ----

    /// <summary>Recomputes the cycling remap for the current time. Returns true when it changed.</summary>
    private bool RefreshCycleMap()
    {
        if (!CyclePreview)
        {
            bool had = _cycleMap != null;
            _cycleMap = null;
            _cycleSteps = null;
            return had;
        }
        var steps = ColorCycleMap.StepsAt(Document.Palette, Document.Cycles, _cycleClock.ElapsedMilliseconds);
        if (_cycleSteps != null && steps.AsSpan().SequenceEqual(_cycleSteps)) return false;
        _cycleSteps = steps;
        _cycleMap = ColorCycleMap.Build(Document.Palette, Document.Cycles, steps);
        return true;
    }

    /// <summary>Ticks as fast as the quickest enabled cycle needs, between 33 and 250 ms.</summary>
    private void UpdateCycleTimer()
    {
        int step = Document.Cycles.Where(c => c.Enabled).Select(c => c.StepMs).DefaultIfEmpty(250).Min();
        _cycleTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(step, 33, 250));
    }

    private void OnCycleTick(object? sender, EventArgs e)
    {
        if (RefreshCycleMap()) RaiseCanvasChanged(null);
    }

    // ---- Playback ----

    private void StartPlayback()
    {
        FinishPendingEdits();
        IsPlaying = true;
        _playTimer.Interval = TimeSpan.FromMilliseconds(Document.Frames[_activeFrame].DurationMs);
        _playTimer.Start();
        RaiseCanvasChanged(null);
    }

    private void StopPlayback()
    {
        if (!IsPlaying) return;
        _playTimer.Stop();
        IsPlaying = false;
        RaiseCanvasChanged(null);
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        ActiveFrameIndex = (_activeFrame + 1) % Document.Frames.Count;
        _playTimer.Interval = TimeSpan.FromMilliseconds(Document.Frames[_activeFrame].DurationMs);
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (Frames.Count == 0) return;
        _previewFrame = (_previewFrame + 1) % Frames.Count;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(Document.Frames[_previewFrame].DurationMs);
        OnPropertyChanged(nameof(PreviewImage));
    }

    private static string StripExtension(string name) => Path.GetFileNameWithoutExtension(name);
}
