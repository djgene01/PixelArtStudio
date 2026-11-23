using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelArtStudio.Core.Model;
using PixelArtStudio.Tools;
using PixelArtStudio.ViewModels;

namespace PixelArtStudio.Controls;

/// <summary>
/// Shows the active frame scaled by <see cref="Zoom"/> (nearest-neighbour when enlarging, smoothed when shrinking),
/// over a checkerboard, with pixel and tile grids, onion skin, selection and brush overlays. Mouse input is converted
/// to pixel coordinates and forwarded to the editor. Must be hosted in a <see cref="ScrollViewer"/> for panning.
/// </summary>
public sealed class PixelCanvas : FrameworkElement
{
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(PixelCanvas),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnZoomChanged));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(PixelCanvas),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TileGridSizeProperty = DependencyProperty.Register(
        nameof(TileGridSize), typeof(int), typeof(PixelCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush CheckerBrush = CreateCheckerBrush();
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(48, 128, 128, 128)), 1));
    private static readonly Pen TilePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(150, 96, 165, 250)), 1));
    private static readonly Pen MarqueeLight = Freeze(new Pen(Brushes.White, 1));
    private static readonly Pen MarqueeDark = Freeze(new Pen(Brushes.Black, 1) { DashStyle = new DashStyle([4, 4], 0) });
    private static readonly Pen HoverPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1));
    private const double OnionOpacity = 0.3;

    /// <summary>The pixel grid is drawn from this zoom up.</summary>
    private const double MinGridZoom = 8;

    /// <summary>Tile grid lines closer than this on screen are not drawn.</summary>
    private const double MinTileSpacing = 4;

    private MainViewModel? _editor;
    private WriteableBitmap? _bitmap;
    private PixelBuffer? _composite;
    private ScrollViewer? _scroller;
    private bool _zoomingAround;

    private bool _drawing;
    private MouseButton _drawButton;
    private int _lastX, _lastY;
    private (int X, int Y)? _hover;

    private bool _panning;
    private Point _panStart;
    private double _panStartH, _panStartV;

    public PixelCanvas()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Loaded += (_, _) => AttachScroller();
        Unloaded += (_, _) => DetachScroller();
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>Spacing of the tile grid in sprite pixels; 0 hides it.</summary>
    public int TileGridSize
    {
        get => (int)GetValue(TileGridSizeProperty);
        set => SetValue(TileGridSizeProperty, value);
    }

    /// <summary>Set by the host while the space bar is held, so left-drag pans.</summary>
    public bool SpaceHeld { get; set; }

    public MainViewModel? Editor
    {
        get => _editor;
        set
        {
            if (_editor != null)
            {
                _editor.CanvasChanged -= OnCanvasChanged;
                _editor.OverlayChanged -= InvalidateVisual;
            }
            _editor = value;
            if (_editor != null)
            {
                _editor.CanvasChanged += OnCanvasChanged;
                _editor.OverlayChanged += InvalidateVisual;
            }
            OnCanvasChanged(null);
        }
    }

    private ScrollViewer? Scroller
    {
        get
        {
            if (_scroller != null) return _scroller;
            DependencyObject? node = this;
            while (node != null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
            return node as ScrollViewer;
        }
    }

    /// <summary>Zooms one step in or out, keeping the document point under <paramref name="anchor"/> in place.</summary>
    public void ZoomAround(Point anchor, bool zoomIn)
    {
        if (_editor == null) return;
        double oldZoom = Zoom;
        double newZoom = zoomIn
            ? MainViewModel.ZoomLevels.FirstOrDefault(z => z > oldZoom + 1e-9, MainViewModel.ZoomLevels[^1])
            : MainViewModel.ZoomLevels.LastOrDefault(z => z < oldZoom - 1e-9, MainViewModel.ZoomLevels[0]);
        if (Math.Abs(newZoom - oldZoom) < 1e-9) return;

        var scroller = Scroller;
        var docPoint = new Point(anchor.X / oldZoom, anchor.Y / oldZoom);
        Point viewportBefore = scroller != null ? TranslatePoint(anchor, scroller) : default;

        _zoomingAround = true;
        try
        {
            _editor.Zoom = newZoom;
        }
        finally
        {
            _zoomingAround = false;
        }
        if (scroller == null) return;
        scroller.UpdateLayout();
        var viewportAfter = TranslatePoint(new Point(docPoint.X * Zoom, docPoint.Y * Zoom), scroller);
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + viewportAfter.X - viewportBefore.X);
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + viewportAfter.Y - viewportBefore.Y);
    }

    /// <summary>Toolbar and keyboard zoom keep the document point at the centre of the view in place.</summary>
    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (PixelCanvas)d;
        double oldZoom = (double)e.OldValue, newZoom = (double)e.NewValue;
        RenderOptions.SetBitmapScalingMode(canvas, newZoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        if (canvas._zoomingAround || canvas.Scroller is not { } scroller || oldZoom <= 0 || !canvas.IsLoaded) return;

        var centre = new Point(scroller.ViewportWidth / 2, scroller.ViewportHeight / 2);
        var onCanvas = scroller.TranslatePoint(centre, canvas);
        var docPoint = new Point(onCanvas.X / oldZoom, onCanvas.Y / oldZoom);
        canvas.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var after = canvas.TranslatePoint(new Point(docPoint.X * canvas.Zoom, docPoint.Y * canvas.Zoom), scroller);
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + after.X - centre.X);
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset + after.Y - centre.Y);
        });
    }

    private void AttachScroller()
    {
        DetachScroller();
        _scroller = Scroller;
        if (_scroller != null) _scroller.ScrollChanged += OnScrollChanged;
    }

    private void DetachScroller()
    {
        if (_scroller != null) _scroller.ScrollChanged -= OnScrollChanged;
        _scroller = null;
    }

    /// <summary>Grids are drawn for the visible area only, so they must follow scrolling.</summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (GridVisible || TileGridVisible) InvalidateVisual();
    }

    private bool GridVisible => ShowGrid && Zoom >= MinGridZoom;
    private bool TileGridVisible => TileGridSize > 0 && TileGridSize * Zoom >= MinTileSpacing;

    // ---- Rendering ----

    private void OnCanvasChanged(RectI? region)
    {
        if (_editor == null) return;
        var doc = _editor.Document;

        if (_bitmap == null || _bitmap.PixelWidth != doc.Width || _bitmap.PixelHeight != doc.Height)
        {
            _bitmap = new WriteableBitmap(doc.Width, doc.Height, 96, 96, PixelFormats.Bgra32, null);
            _composite = new PixelBuffer(doc.Width, doc.Height);
            region = null;
            InvalidateMeasure();
        }

        var area = (region ?? _composite!.Bounds).Intersect(_composite!.Bounds);
        if (!area.IsEmpty)
        {
            _editor.ComposeInto(_composite, area);
            _bitmap.WritePixels(new Int32Rect(area.X, area.Y, area.Width, area.Height),
                _composite.Pixels, _composite.Width * 4, area.X, area.Y);
        }

        // The bitmap updates itself; overlays (onion skin, size) need a full redraw.
        if (region == null) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        _editor == null ? default : new Size(_editor.Document.Width * Zoom, _editor.Document.Height * Zoom);

    protected override void OnRender(DrawingContext dc)
    {
        if (_editor == null || _bitmap == null) return;
        double zoom = Zoom;
        var doc = _editor.Document;
        var rect = new Rect(0, 0, doc.Width * zoom, doc.Height * zoom);

        dc.DrawRectangle(CheckerBrush, null, rect);
        dc.DrawImage(_bitmap, rect);

        foreach (var frame in _editor.OnionSkinFrames())
        {
            dc.PushOpacity(OnionOpacity);
            dc.DrawImage(frame, rect);
            dc.Pop();
        }

        if (GridVisible || TileGridVisible)
        {
            var visible = VisiblePixels(doc.Width, doc.Height, zoom);
            if (GridVisible) dc.DrawGeometry(null, GridPen, BuildGrid(visible, 1, doc.Width, doc.Height, zoom));
            if (TileGridVisible) dc.DrawGeometry(null, TilePen, BuildGrid(visible, TileGridSize, doc.Width, doc.Height, zoom));
        }

        if (_editor.Selection is { } sel)
        {
            var r = new Rect(sel.X * zoom + 0.5, sel.Y * zoom + 0.5, Math.Max(0, sel.Width * zoom - 1), Math.Max(0, sel.Height * zoom - 1));
            dc.DrawRectangle(null, MarqueeLight, r);
            dc.DrawRectangle(null, MarqueeDark, r);
        }

        if (_hover is { } h && !_panning && _editor.CurrentTool is not (ToolKind.Select or ToolKind.Eyedropper or ToolKind.Fill))
        {
            int size = _editor.BrushSize;
            int left = h.X - (size - 1) / 2, top = h.Y - (size - 1) / 2;
            dc.DrawRectangle(null, HoverPen, new Rect(left * zoom + 0.5, top * zoom + 0.5, Math.Max(0, size * zoom - 1), Math.Max(0, size * zoom - 1)));
        }
    }

    /// <summary>The sprite pixels currently on screen (the whole sprite when not in a scroll viewer).</summary>
    private RectI VisiblePixels(int width, int height, double zoom)
    {
        if (Scroller is not { } scroller || scroller.ViewportWidth <= 0) return new RectI(0, 0, width, height);
        var topLeft = scroller.TranslatePoint(new Point(0, 0), this);
        var bottomRight = scroller.TranslatePoint(new Point(scroller.ViewportWidth, scroller.ViewportHeight), this);
        int left = Math.Max(0, (int)Math.Floor(topLeft.X / zoom));
        int top = Math.Max(0, (int)Math.Floor(topLeft.Y / zoom));
        int right = Math.Min(width, (int)Math.Ceiling(bottomRight.X / zoom) + 1);
        int bottom = Math.Min(height, (int)Math.Ceiling(bottomRight.Y / zoom) + 1);
        return new RectI(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>Grid lines every <paramref name="step"/> pixels, limited to the <paramref name="visible"/> area.</summary>
    private static StreamGeometry BuildGrid(RectI visible, int step, int width, int height, double zoom)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double top = visible.Y * zoom, bottom = visible.Bottom * zoom;
            double left = visible.X * zoom, right = visible.Right * zoom;
            int firstX = Math.Max(step, (visible.X + step - 1) / step * step);
            for (int x = firstX; x <= visible.Right && x < width; x += step)
            {
                ctx.BeginFigure(new Point(x * zoom + 0.5, top), false, false);
                ctx.LineTo(new Point(x * zoom + 0.5, bottom), true, false);
            }
            int firstY = Math.Max(step, (visible.Y + step - 1) / step * step);
            for (int y = firstY; y <= visible.Bottom && y < height; y += step)
            {
                ctx.BeginFigure(new Point(left, y * zoom + 0.5), false, false);
                ctx.LineTo(new Point(right, y * zoom + 0.5), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // ---- Input ----

    private (int X, int Y) ToPixel(Point p) => ((int)Math.Floor(p.X / Zoom), (int)Math.Floor(p.Y / Zoom));

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_editor == null || _drawing || _panning) return;

        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && SpaceHeld))
        {
            if (Scroller is not { } scroller) return;
            _panning = true;
            _panStart = e.GetPosition(scroller);
            (_panStartH, _panStartV) = (scroller.HorizontalOffset, scroller.VerticalOffset);
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        var (x, y) = ToPixel(e.GetPosition(this));
        var button = e.ChangedButton == MouseButton.Left ? PointerButton.Left : PointerButton.Right;
        if (_editor.PointerDown(x, y, button, Keyboard.Modifiers))
        {
            _drawing = true;
            _drawButton = e.ChangedButton;
            (_lastX, _lastY) = (x, y);
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_editor == null) return;

        if (_panning)
        {
            if (Scroller is { } scroller)
            {
                var p = e.GetPosition(scroller);
                scroller.ScrollToHorizontalOffset(_panStartH - (p.X - _panStart.X));
                scroller.ScrollToVerticalOffset(_panStartV - (p.Y - _panStart.Y));
            }
            return;
        }

        var (x, y) = ToPixel(e.GetPosition(this));
        _editor.SetCursor(x, y);
        if (_hover != (x, y))
        {
            _hover = (x, y);
            InvalidateVisual();
        }
        if (_drawing)
        {
            (_lastX, _lastY) = (x, y);
            _editor.PointerMove(x, y, Keyboard.Modifiers);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            Cursor = Cursors.Cross;
            ReleaseMouseCapture();
            return;
        }
        if (_drawing && e.ChangedButton == _drawButton)
        {
            var (x, y) = ToPixel(e.GetPosition(this));
            _drawing = false;
            ReleaseMouseCapture();
            _editor?.PointerUp(x, y, Keyboard.Modifiers);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Capture lost mid-stroke (e.g. Alt+Tab): finish the stroke where it was.
        if (_drawing)
        {
            _drawing = false;
            _editor?.PointerUp(_lastX, _lastY, Keyboard.Modifiers);
        }
        if (_panning)
        {
            _panning = false;
            Cursor = Cursors.Cross;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        _editor?.SetCursor(null, null);
        InvalidateVisual();
    }

    private static Brush CreateCheckerBrush()
    {
        var light = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
        var dark = new SolidColorBrush(Color.FromRgb(0x46, 0x46, 0x46));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(dark, null, new GeometryGroup
        {
            Children = { new RectangleGeometry(new Rect(0, 0, 8, 8)), new RectangleGeometry(new Rect(8, 8, 8, 8)) },
        }));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static Pen Freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
