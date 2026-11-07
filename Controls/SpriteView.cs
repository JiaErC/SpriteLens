using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpriteLens.Core;

namespace SpriteLens.Controls;

public sealed class PointerInfoEventArgs : EventArgs
{
    public bool Inside { get; init; }
    public double ImageX { get; init; }
    public double ImageY { get; init; }
    public int PixelX { get; init; }
    public int PixelY { get; init; }
    public int? FrameIndex { get; init; }
}

/// <summary>
/// 精灵图画布：图像与网格分成两层绘制，悬停时只重绘网格层，保持流畅。
/// </summary>
public sealed class SpriteView : Grid
{
    private const double MinZoom = 0.02;
    private const double MaxZoom = 64;

    private readonly ImageLayer _imageLayer;
    private readonly OverlayLayer _overlayLayer;

    private BitmapSource? _source;
    private double _zoom = 1;
    private Point _offset;
    private bool _fitMode = true;
    private bool _pixelSharp = true;
    private ImageBackground _background = ImageBackground.Checker;

    private int? _hoveredFrame;
    private int? _selectedFrame;

    private bool _dragging;
    private bool _dragMoved;
    private Point _dragLastScreen;
    private Point _dragStartScreen;
    private Point _dragStartOffset;
    private int? _dragStartedFrame;

    private bool _applyThemeQueued;

    // 主题缓存
    private SolidColorBrush _gridHaloBrush = Brushes.White;
    private SolidColorBrush _gridCoreBrush = Brushes.Black;
    private SolidColorBrush _gridAccentBrush = Brushes.Cyan;
    private SolidColorBrush _labelBgBrush = Brushes.Black;
    private SolidColorBrush _labelBorderBrush = Brushes.Gray;
    private SolidColorBrush _labelTextBrush = Brushes.White;
    private SolidColorBrush _frameHoverFill = Brushes.Transparent;
    private SolidColorBrush _frameHoverLine = Brushes.Cyan;
    private SolidColorBrush _frameSelFill = Brushes.Transparent;
    private SolidColorBrush _frameSelLine = Brushes.Cyan;
    private SolidColorBrush _imageBorderBrush = Brushes.Gray;
    private SolidColorBrush _whiteBrush = Brushes.White;
    private SolidColorBrush _cropShadeBrush = Brushes.Black;
    private SolidColorBrush _checkerA = Brushes.DarkGray;
    private SolidColorBrush _checkerB = Brushes.Gray;
    private SolidColorBrush _darkBackground = Brushes.Black;
    private SolidColorBrush _lightBackground = Brushes.White;
    private DrawingBrush _checkerBrush = new();
    private Typeface _labelTypeface = new("Consolas");
    private readonly Dictionary<int, FormattedText> _labelCache = new();
    private double _labelCacheDpi = -1;

    // 裁剪
    private bool _cropActive;
    private Rect _cropRect;
    private double? _cropRatio;
    private CropRatioKind _cropRatioKind = CropRatioKind.Free;
    private CropHandle _cropHandle;
    private bool _cropDragging;
    private Point _cropDragStart;
    private Rect _cropStartRect;

    private enum CropHandle
    {
        None,
        New,
        Move,
        NW,
        N,
        NE,
        E,
        SE,
        S,
        SW,
        W
    }

    public SpriteView()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _imageLayer = new ImageLayer(this) { IsHitTestVisible = false };
        _overlayLayer = new OverlayLayer(this) { IsHitTestVisible = false };
        Children.Add(_imageLayer);
        Children.Add(_overlayLayer);

        ApplyTheme();
        Loaded += (_, _) =>
        {
            if (_source != null && _fitMode) FitToView(raiseEvent: false);
            InvalidateLayers();
        };
    }

    public GridSpec FrameGrid { get; } = new();

    public BitmapSource? Source => _source;

    public int ImageWidth => _source?.PixelWidth ?? 0;

    public int ImageHeight => _source?.PixelHeight ?? 0;

    public double Zoom => _zoom;

    public int ZoomPercent => (int)Math.Round(_zoom * 100);

    public bool IsFitMode => _fitMode;

    public bool PixelSharp
    {
        get => _pixelSharp;
        set
        {
            if (_pixelSharp == value) return;
            _pixelSharp = value;
            UpdateScalingMode();
            InvalidateImage();
        }
    }

    public ImageBackground BackgroundMode
    {
        get => _background;
        set
        {
            if (_background == value) return;
            _background = value;
            InvalidateImage();
        }
    }

    public int? HoveredFrame => _hoveredFrame;

    public int? SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            if (_selectedFrame == value) return;
            _selectedFrame = value;
            InvalidateOverlay();
        }
    }

    public bool IsCropActive => _cropActive;

    public Rect CropRect => _cropRect;

    public bool HasImage => _source != null;

    public Rect ImageScreenRect
    {
        get
        {
            var offset = EffectiveOffset;
            return new Rect(offset.X, offset.Y, ImageWidth * _zoom, ImageHeight * _zoom);
        }
    }

    public event EventHandler? ViewChanged;

    public event EventHandler<PointerInfoEventArgs>? PointerChanged;

    public event EventHandler<int?>? FrameClicked;

    public event EventHandler<int?>? FrameActivated;

    public event EventHandler? CropChanged;

    // =====================================================================
    // 公开操作
    // =====================================================================

    public void SetImage(BitmapSource? source, bool resetView = true)
    {
        _source = source;
        _hoveredFrame = null;
        _selectedFrame = null;
        _cropActive = false;
        _cropDragging = false;

        if (resetView)
        {
            _fitMode = true;
            if (ActualWidth > 1 && ActualHeight > 1) FitToView(raiseEvent: false);
        }
        else
        {
            ClampOffset();
        }

        UpdateScalingMode();
        InvalidateLayers();
        RaiseViewChanged();
    }

    /// <summary>仅更新位图（调整颜色等不改变尺寸的场景）。</summary>
    public void UpdateBitmap(BitmapSource? source)
    {
        _source = source;
        UpdateScalingMode();
        InvalidateImage();
        RaiseViewChanged();
    }

    public void FitToView(bool raiseEvent = true)
    {
        if (_source == null || ImageWidth <= 0 || ImageHeight <= 0) return;
        double vw = ActualWidth;
        double vh = ActualHeight;
        if (vw <= 1 || vh <= 1) return;

        // 底部为悬浮工具栏留出空间，避免图片藏在工具栏下面。
        double paddingX = 44;
        double paddingTop = 34;
        double paddingBottom = Math.Clamp(vh * 0.12, 64, 96);
        double availableW = Math.Max(16, vw - paddingX * 2);
        double availableH = Math.Max(16, vh - paddingTop - paddingBottom);
        double scale = Math.Min(availableW / ImageWidth, availableH / ImageHeight);
        scale = Math.Clamp(scale, MinZoom, 32);
        _zoom = scale;
        _offset = new Point(
            (vw - ImageWidth * scale) / 2,
            paddingTop + (availableH - ImageHeight * scale) / 2);
        _fitMode = true;
        _hoveredFrame = null;
        UpdateScalingMode();
        InvalidateLayers();
        if (raiseEvent) RaiseViewChanged();
    }

    public void ZoomToActualSize()
    {
        if (_source == null) return;
        SetZoom(1.0, new Point(ActualWidth / 2, ActualHeight / 2));
    }

    public void SetFill()
    {
        if (_source == null) return;
        double scale = Math.Max(ActualWidth / ImageWidth, ActualHeight / ImageHeight);
        SetZoom(scale, new Point(ActualWidth / 2, ActualHeight / 2));
    }

    public void SetZoom(double zoom, Point anchorScreen)
    {
        if (_source == null) return;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(zoom - _zoom) < 0.00001) return;

        var anchorImage = ScreenToImage(anchorScreen);
        _zoom = zoom;
        _offset = new Point(anchorScreen.X - anchorImage.X * zoom, anchorScreen.Y - anchorImage.Y * zoom);
        _fitMode = false;
        ClampOffset();
        UpdateScalingMode();
        InvalidateLayers();
        UpdateHover(Mouse.GetPosition(this));
        RaiseViewChanged();
    }

    public void ZoomBy(double factor, Point? anchor = null)
    {
        if (_source == null) return;
        var point = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        SetZoom(_zoom * factor, point);
    }

    public void ZoomToFrame(int frameIndex, double margin = 26)
    {
        if (_source == null || !FrameGrid.IsActive) return;
        var rect = FrameGrid.GetFrameRect(frameIndex, ImageWidth, ImageHeight);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        double areaTop = 28;
        double areaBottom = Math.Clamp(ActualHeight * 0.12, 64, 96);
        double availableW = Math.Max(16, ActualWidth - 52);
        double availableH = Math.Max(16, ActualHeight - areaTop - areaBottom);
        double scale = Math.Min(availableW / (rect.Width + margin), availableH / (rect.Height + margin));
        scale = Math.Clamp(scale, MinZoom, 32);
        _zoom = scale;
        _offset = new Point(
            (ActualWidth - rect.Width * scale) / 2 - rect.X * scale,
            areaTop + (availableH - rect.Height * scale) / 2 - rect.Y * scale);
        _fitMode = false;
        ClampOffset();
        UpdateScalingMode();
        InvalidateLayers();
        RaiseViewChanged();
    }

    public void PanBy(double dx, double dy)
    {
        _offset.X += dx;
        _offset.Y += dy;
        _fitMode = false;
        ClampOffset();
        InvalidateLayers();
        RaiseViewChanged();
    }

    public Point ScreenToImage(Point screen)
    {
        var offset = EffectiveOffset;
        return new Point((screen.X - offset.X) / _zoom, (screen.Y - offset.Y) / _zoom);
    }

    public Point ImageToScreen(Point image)
    {
        var offset = EffectiveOffset;
        return new Point(offset.X + image.X * _zoom, offset.Y + image.Y * _zoom);
    }

    public int? GetFrameAt(Point screen)
    {
        if (_source == null || !FrameGrid.IsActive) return null;
        var p = ScreenToImage(screen);
        return FrameGrid.FrameIndexAt(p.X, p.Y, ImageWidth, ImageHeight);
    }

    public void RefreshGrid()
    {
        UpdateHover(Mouse.GetPosition(this));
        InvalidateOverlay();
    }

    public void RefreshDpi()
    {
        _labelCache.Clear();
        _labelCacheDpi = -1;
        InvalidateLayers();
    }

    public void ApplyTheme()
    {
        SolidColorBrush Get(string key, Color fallback)
        {
            var found = TryFindResource(key) as SolidColorBrush;
            var brush = new SolidColorBrush(found?.Color ?? fallback);
            brush.Freeze();
            return brush;
        }

        _gridHaloBrush = Get("B.GridHalo", Colors.White);
        _gridCoreBrush = Get("B.GridCore", Colors.Black);
        _gridAccentBrush = Get("B.GridAccent", Colors.Cyan);
        _labelBgBrush = Get("B.LabelBg", Color.FromArgb(184, 10, 14, 21));
        _labelBorderBrush = Get("B.LabelBorder", Color.FromArgb(46, 255, 255, 255));
        _labelTextBrush = Get("B.LabelText", Colors.White);
        _frameHoverFill = Get("B.FrameHoverFill", Color.FromArgb(31, 139, 151, 255));
        _frameHoverLine = Get("B.FrameHoverLine", Colors.RoyalBlue);
        _frameSelFill = Get("B.FrameSelFill", Color.FromArgb(46, 139, 151, 255));
        _frameSelLine = Get("B.FrameSelLine", Colors.RoyalBlue);
        _imageBorderBrush = Get("B.ImageBorder", Color.FromArgb(38, 255, 255, 255));
        _whiteBrush = Get("B.White", Colors.White);
        _cropShadeBrush = Get("B.CropShade", Color.FromArgb(166, 6, 9, 16));
        _checkerA = Get("B.CheckerA", Color.FromRgb(26, 31, 41));
        _checkerB = Get("B.CheckerB", Color.FromRgb(35, 41, 54));
        _darkBackground = Get("B.Black", Colors.Black);
        _lightBackground = Get("B.White", Colors.White);

        _checkerBrush = CreateCheckerBrush(_checkerA.Color, _checkerB.Color);
        _labelTypeface = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        _labelCache.Clear();
        _labelCacheDpi = -1;

        if (!_applyThemeQueued)
        {
            _applyThemeQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _applyThemeQueued = false;
                InvalidateLayers();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    // =====================================================================
    // 几何
    // =====================================================================

    private Point EffectiveOffset
    {
        get
        {
            if (!_pixelSharp || _zoom < 1) return _offset;
            var dpi = VisualTreeHelper.GetDpi(this);
            return new Point(
                Math.Round(_offset.X * dpi.DpiScaleX) / dpi.DpiScaleX,
                Math.Round(_offset.Y * dpi.DpiScaleY) / dpi.DpiScaleY);
        }
    }

    private void ClampOffset()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        double iw = ImageWidth * _zoom;
        double ih = ImageHeight * _zoom;
        double x = _offset.X;
        double y = _offset.Y;

        x = iw <= ActualWidth ? (ActualWidth - iw) / 2 : Math.Min(0, Math.Max(ActualWidth - iw, x));
        y = ih <= ActualHeight ? (ActualHeight - ih) / 2 : Math.Min(0, Math.Max(ActualHeight - ih, y));

        _offset = new Point(x, y);
    }

    private void UpdateScalingMode()
    {
        var mode = (!_pixelSharp || _zoom < 1)
            ? BitmapScalingMode.HighQuality
            : BitmapScalingMode.NearestNeighbor;
        RenderOptions.SetBitmapScalingMode(_imageLayer, mode);
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    private void InvalidateImage() => _imageLayer.InvalidateVisual();

    private void InvalidateOverlay() => _overlayLayer.InvalidateVisual();

    public void InvalidateLayers()
    {
        _imageLayer.InvalidateVisual();
        _overlayLayer.InvalidateVisual();
    }

    // =====================================================================
    // 绘制
    // =====================================================================

    internal void DrawImageLayer(DrawingContext dc)
    {
        if (_source == null) return;

        var rect = ImageScreenRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        switch (_background)
        {
            case ImageBackground.Checker:
                dc.PushTransform(new TranslateTransform(rect.X, rect.Y));
                dc.DrawRectangle(_checkerBrush, null, new Rect(0, 0, rect.Width, rect.Height));
                dc.Pop();
                break;
            case ImageBackground.Dark:
                dc.DrawRectangle(_darkBackground, null, rect);
                break;
            case ImageBackground.Light:
                dc.DrawRectangle(_lightBackground, null, rect);
                break;
            case ImageBackground.Black:
                dc.DrawRectangle(Brushes.Black, null, rect);
                break;
            case ImageBackground.White:
                dc.DrawRectangle(Brushes.White, null, rect);
                break;
        }

        dc.DrawImage(_source, rect);

        var borderPen = new Pen(_imageBorderBrush, 1);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, rect);
    }

    internal void DrawOverlayLayer(DrawingContext dc)
    {
        if (_source == null) return;
        var imageRect = ImageScreenRect;
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        double dpiScale = dpi.DpiScaleX;

        if (FrameGrid.IsActive)
        {
            DrawFrameFills(dc, imageRect);
            DrawGridLines(dc, imageRect, dpiScale);
            DrawFrameBorders(dc);
        }

        if (FrameGrid.IsActive && FrameGrid.Labels)
        {
            DrawFrameLabels(dc, imageRect, dpiScale);
        }

        if (_cropActive)
        {
            DrawCrop(dc, imageRect, dpiScale);
        }
    }

    private void DrawFrameFills(DrawingContext dc, Rect imageRect)
    {
        if (!FrameGrid.Highlight) return;
        dc.PushClip(new RectangleGeometry(imageRect));

        if (_selectedFrame is int selected)
        {
            var rect = FrameToScreen(selected);
            if (rect.Width > 0) dc.DrawRectangle(_frameSelFill, null, rect);
        }

        if (_hoveredFrame is int hovered && hovered != _selectedFrame)
        {
            var rect = FrameToScreen(hovered);
            if (rect.Width > 0) dc.DrawRectangle(_frameHoverFill, null, rect);
        }

        dc.Pop();
    }

    private void DrawGridLines(DrawingContext dc, Rect imageRect, double dpiScale)
    {
        double cellW = FrameGrid.FrameWidth * _zoom;
        double cellH = FrameGrid.FrameHeight * _zoom;
        bool showVerticals = cellW >= 4;
        bool showHorizontals = cellH >= 4;

        int cols = FrameGrid.Columns(ImageWidth);
        int rows = FrameGrid.Rows(ImageHeight);

        var xs = new List<double>(Math.Min(cols + 2, 64));
        var ys = new List<double>(Math.Min(rows + 2, 64));
        double deviceThickness = Math.Max(1, FrameGrid.Thickness);
        double core = deviceThickness / dpiScale;
        double halo = (deviceThickness + 2) / dpiScale;

        xs.Add(AlignDevice(imageRect.Left, deviceThickness, dpiScale));
        xs.Add(AlignDevice(imageRect.Right, deviceThickness, dpiScale));
        ys.Add(AlignDevice(imageRect.Top, deviceThickness, dpiScale));
        ys.Add(AlignDevice(imageRect.Bottom, deviceThickness, dpiScale));

        if (showVerticals)
        {
            int k0 = Math.Max(1, (int)Math.Floor((0 - imageRect.Left) / cellW));
            int k1 = Math.Min(cols - 1, (int)Math.Ceiling((ActualWidth - imageRect.Left) / cellW));
            k0 = Math.Max(k0, 1);
            for (int k = k0; k <= k1; k++)
            {
                double x = imageRect.Left + k * cellW;
                if (x > imageRect.Left + 0.5 && x < imageRect.Right - 0.5)
                {
                    xs.Add(AlignDevice(x, deviceThickness, dpiScale));
                }
            }
        }

        if (showHorizontals)
        {
            int k0 = Math.Max(1, (int)Math.Floor((0 - imageRect.Top) / cellH));
            int k1 = Math.Min(rows - 1, (int)Math.Ceiling((ActualHeight - imageRect.Top) / cellH));
            k0 = Math.Max(k0, 1);
            for (int k = k0; k <= k1; k++)
            {
                double y = imageRect.Top + k * cellH;
                if (y > imageRect.Top + 0.5 && y < imageRect.Bottom - 0.5)
                {
                    ys.Add(AlignDevice(y, deviceThickness, dpiScale));
                }
            }
        }

        dc.PushClip(new RectangleGeometry(imageRect));
        var top = imageRect.Top;
        var bottom = imageRect.Bottom;
        var left = imageRect.Left;
        var right = imageRect.Right;

        switch (FrameGrid.Style)
        {
            case GridLineStyle.Dual:
            {
                var haloPen = CreatePen(_gridHaloBrush, halo);
                var corePen = CreatePen(_gridCoreBrush, core);
                DrawLineSet(dc, haloPen, xs, ys, left, right, top, bottom);
                DrawLineSet(dc, corePen, xs, ys, left, right, top, bottom);
                break;
            }
            case GridLineStyle.Dash:
            {
                var basePen = CreatePen(_gridCoreBrush, core);
                var dashPen = new Pen(_whiteBrush, Math.Max(core, 1.0 / dpiScale))
                {
                    DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0),
                    DashCap = PenLineCap.Flat
                };
                dashPen.Freeze();
                DrawLineSet(dc, basePen, xs, ys, left, right, top, bottom);
                DrawLineSet(dc, dashPen, xs, ys, left, right, top, bottom);
                break;
            }
            case GridLineStyle.Accent:
            {
                var haloPen = CreatePen(_gridCoreBrush, halo);
                var accentPen = CreatePen(_gridAccentBrush, core);
                DrawLineSet(dc, haloPen, xs, ys, left, right, top, bottom);
                DrawLineSet(dc, accentPen, xs, ys, left, right, top, bottom);
                break;
            }
        }

        dc.Pop();
    }

    private static void DrawLineSet(DrawingContext dc, Pen pen, List<double> xs, List<double> ys,
        double left, double right, double top, double bottom)
    {
        foreach (double x in xs)
        {
            dc.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }

        foreach (double y in ys)
        {
            dc.DrawLine(pen, new Point(left, y), new Point(right, y));
        }
    }

    private static double AlignDevice(double value, double deviceThickness, double dpiScale)
    {
        double pixels = Math.Round(value * dpiScale);
        if ((int)Math.Round(deviceThickness) % 2 == 1)
        {
            pixels += 0.5;
        }

        return pixels / dpiScale;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, Math.Max(thickness, 1.0 / 96.0));
        pen.Freeze();
        return pen;
    }

    private void DrawFrameBorders(DrawingContext dc)
    {
        if (!FrameGrid.Highlight) return;
        var rect = ImageScreenRect;
        dc.PushClip(new RectangleGeometry(rect));

        if (_selectedFrame is int selected)
        {
            var r = FrameToScreen(selected);
            if (r.Width > 0)
            {
                var halo = CreatePen(Brushes.White, 3.5);
                var line = CreatePen(_frameSelLine, 2.2);
                dc.DrawRectangle(null, halo, r);
                dc.DrawRectangle(null, line, r);
                var bracket = CreatePen(_frameSelLine, 2.6);
                DrawCornerBrackets(dc, r, 16, bracket);
            }
        }

        if (_hoveredFrame is int hovered && hovered != _selectedFrame)
        {
            var r = FrameToScreen(hovered);
            if (r.Width > 0)
            {
                var underlay = CreatePen(_gridCoreBrush, 3);
                var line = CreatePen(_frameHoverLine, 1.4);
                dc.DrawRectangle(null, underlay, r);
                dc.DrawRectangle(null, line, r);
            }
        }

        dc.Pop();
    }

    private static void DrawCornerBrackets(DrawingContext dc, Rect rect, double length, Pen pen)
    {
        double l = Math.Min(length, Math.Min(rect.Width, rect.Height) / 3);
        double right = rect.Right;
        double bottom = rect.Bottom;
        double left = rect.Left;
        double top = rect.Top;

        dc.DrawLine(pen, new Point(left, top + l), new Point(left, top));
        dc.DrawLine(pen, new Point(left, top), new Point(left + l, top));

        dc.DrawLine(pen, new Point(right - l, top), new Point(right, top));
        dc.DrawLine(pen, new Point(right, top), new Point(right, top + l));

        dc.DrawLine(pen, new Point(left, bottom - l), new Point(left, bottom));
        dc.DrawLine(pen, new Point(left, bottom), new Point(left + l, bottom));

        dc.DrawLine(pen, new Point(right - l, bottom), new Point(right, bottom));
        dc.DrawLine(pen, new Point(right, bottom), new Point(right, bottom - l));
    }

    private Rect FrameToScreen(int frameIndex)
    {
        var r = FrameGrid.GetFrameRect(frameIndex, ImageWidth, ImageHeight);
        if (r.Width <= 0 || r.Height <= 0) return Rect.Empty;
        var origin = ImageToScreen(new Point(r.X, r.Y));
        return new Rect(origin.X, origin.Y, r.Width * _zoom, r.Height * _zoom);
    }

    private void DrawFrameLabels(DrawingContext dc, Rect imageRect, double dpiScale)
    {
        double cellW = FrameGrid.FrameWidth * _zoom;
        double cellH = FrameGrid.FrameHeight * _zoom;
        if (cellW < 30 || cellH < 20) return;

        int cols = FrameGrid.Columns(ImageWidth);
        int rows = FrameGrid.Rows(ImageHeight);
        if (cols <= 0 || rows <= 0) return;

        int colStart = Math.Max(0, (int)Math.Floor((0 - imageRect.Left) / cellW));
        int colEnd = Math.Min(cols - 1, (int)Math.Ceiling((ActualWidth - imageRect.Left) / cellW));
        int rowStart = Math.Max(0, (int)Math.Floor((0 - imageRect.Top) / cellH));
        int rowEnd = Math.Min(rows - 1, (int)Math.Ceiling((ActualHeight - imageRect.Top) / cellH));

        if ((long)(colEnd - colStart + 1) * (rowEnd - rowStart + 1) > 600) return;

        if (Math.Abs(_labelCacheDpi - dpiScale) > 0.001)
        {
            _labelCache.Clear();
            _labelCacheDpi = dpiScale;
        }

        dc.PushClip(new RectangleGeometry(imageRect));

        for (int row = rowStart; row <= rowEnd; row++)
        {
            for (int col = colStart; col <= colEnd; col++)
            {
                double cellX = imageRect.Left + col * cellW;
                double cellY = imageRect.Top + row * cellH;
                if (cellX + 22 > imageRect.Right || cellY + 20 > imageRect.Bottom) continue;

                int index = row * cols + col;
                var text = GetLabelText(index + 1, dpiScale);
                double pillW = text.Width + 9;
                double pillH = Math.Max(14, text.Height + 3);
                var pill = new Rect(cellX + 4, cellY + 4, pillW, pillH);
                bool isSelected = _selectedFrame == index;

                dc.DrawRoundedRectangle(isSelected ? _frameSelLine : _labelBgBrush, null, pill, 4, 4);
                if (!isSelected)
                {
                    var border = new Pen(_labelBorderBrush, 0.8);
                    border.Freeze();
                    dc.DrawRoundedRectangle(null, border, pill, 4, 4);
                }

                dc.DrawText(text, new Point(pill.X + 4.5, pill.Y + (pill.Height - text.Height) / 2));
            }
        }

        dc.Pop();
    }

    private FormattedText GetLabelText(int number, double dpiScale)
    {
        if (_labelCache.TryGetValue(number, out var cached)) return cached;
        var text = new FormattedText(
            number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            9.5,
            _labelTextBrush,
            dpiScale);

        if (_labelCache.Count > 2000) _labelCache.Clear();
        _labelCache[number] = text;
        return text;
    }

    // =====================================================================
    // 裁剪
    // =====================================================================

    public void BeginCrop(Int32Rect? initialFrame = null)
    {
        if (_source == null) return;
        _cropActive = true;
        _cropRatio = null;
        _cropRatioKind = CropRatioKind.Free;
        _cropRect = initialFrame is { Width: > 0, Height: > 0 } frame
            ? new Rect(frame.X, frame.Y, frame.Width, frame.Height)
            : new Rect(0, 0, ImageWidth, ImageHeight);
        _cropRect = ClampCropRect(_cropRect);
        InvalidateOverlay();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EndCrop()
    {
        if (!_cropActive) return;
        _cropActive = false;
        _cropDragging = false;
        Cursor = Cursors.Arrow;
        InvalidateOverlay();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    public Int32Rect GetCropPixelRect()
    {
        var r = ClampCropRect(_cropRect);
        return new Int32Rect(
            (int)Math.Round(r.X),
            (int)Math.Round(r.Y),
            Math.Max(1, (int)Math.Round(r.Width)),
            Math.Max(1, (int)Math.Round(r.Height)));
    }

    public void SetCropRatioKind(CropRatioKind kind)
    {
        _cropRatioKind = kind;
        double? ratio = kind switch
        {
            CropRatioKind.Square => 1.0,
            CropRatioKind.Frame => FrameGrid.IsActive && FrameGrid.FrameHeight > 0
                ? FrameGrid.FrameWidth / (double)FrameGrid.FrameHeight
                : 1.0,
            CropRatioKind.FourThree => 4.0 / 3.0,
            CropRatioKind.SixteenNine => 16.0 / 9.0,
            _ => null
        };
        _cropRatio = ratio;
        if (ratio is double value && _cropActive)
        {
            var r = _cropRect;
            if (r.Width / r.Height > value) r.Width = r.Height * value;
            else r.Height = r.Width / value;
            r.X += (_cropRect.Width - r.Width) / 2;
            r.Y += (_cropRect.Height - r.Height) / 2;
            _cropRect = ClampCropRect(r);
        }

        InvalidateOverlay();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    private Rect ClampCropRect(Rect rect)
    {
        double maxW = Math.Max(1, ImageWidth);
        double maxH = Math.Max(1, ImageHeight);
        double w = Math.Clamp(rect.Width, Math.Min(4, maxW), maxW);
        double h = Math.Clamp(rect.Height, Math.Min(4, maxH), maxH);
        double x = Math.Clamp(rect.X, 0, Math.Max(0, maxW - w));
        double y = Math.Clamp(rect.Y, 0, Math.Max(0, maxH - h));
        return new Rect(x, y, w, h);
    }

    private void DrawCrop(DrawingContext dc, Rect imageRect, double dpiScale)
    {
        var crop = CropScreenRect;
        if (crop.Width <= 0 || crop.Height <= 0) return;

        var x0 = Math.Max(imageRect.Left, crop.Left);
        var x1 = Math.Min(imageRect.Right, crop.Right);
        var y0 = Math.Max(imageRect.Top, crop.Top);
        var y1 = Math.Min(imageRect.Bottom, crop.Bottom);

        dc.DrawRectangle(_cropShadeBrush, null, new Rect(imageRect.Left, imageRect.Top, imageRect.Width, Math.Max(0, y0 - imageRect.Top)));
        dc.DrawRectangle(_cropShadeBrush, null, new Rect(imageRect.Left, y1, imageRect.Width, Math.Max(0, imageRect.Bottom - y1)));
        dc.DrawRectangle(_cropShadeBrush, null, new Rect(imageRect.Left, y0, Math.Max(0, x0 - imageRect.Left), Math.Max(0, y1 - y0)));
        dc.DrawRectangle(_cropShadeBrush, null, new Rect(x1, y0, Math.Max(0, imageRect.Right - x1), Math.Max(0, y1 - y0)));

        var darkPen = CreatePen(_gridCoreBrush, 3);
        var whitePen = CreatePen(Brushes.White, 1.2);
        dc.DrawRectangle(null, darkPen, crop);
        dc.DrawRectangle(null, whitePen, crop);

        var thirdsPen = new Pen(new SolidColorBrush(Color.FromArgb(96, 255, 255, 255)), 1);
        thirdsPen.Freeze();
        double thirdW = crop.Width / 3;
        double thirdH = crop.Height / 3;
        dc.DrawLine(thirdsPen, new Point(crop.Left + thirdW, crop.Top), new Point(crop.Left + thirdW, crop.Bottom));
        dc.DrawLine(thirdsPen, new Point(crop.Left + thirdW * 2, crop.Top), new Point(crop.Left + thirdW * 2, crop.Bottom));
        dc.DrawLine(thirdsPen, new Point(crop.Left, crop.Top + thirdH), new Point(crop.Right, crop.Top + thirdH));
        dc.DrawLine(thirdsPen, new Point(crop.Left, crop.Top + thirdH * 2), new Point(crop.Right, crop.Top + thirdH * 2));

        var handlePen = CreatePen(_gridCoreBrush, 1);
        const double handle = 9;
        foreach (var point in CropHandlePoints(crop))
        {
            dc.DrawRectangle(Brushes.White, handlePen,
                new Rect(point.X - handle / 2, point.Y - handle / 2, handle, handle));
        }

        var pixelRect = GetCropPixelRect();
        var labelText = new FormattedText(
            $"{pixelRect.Width} × {pixelRect.Height}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            10.5,
            Brushes.White,
            dpiScale);

        double labelY = crop.Top - labelText.Height - 12;
        if (labelY < imageRect.Top + 2) labelY = crop.Top + 6;
        double labelX = Math.Clamp(crop.Left, imageRect.Left + 2, Math.Max(imageRect.Left + 2, imageRect.Right - labelText.Width - 14));
        var labelBg = new Rect(labelX, labelY, labelText.Width + 12, labelText.Height + 6);
        dc.DrawRoundedRectangle(_labelBgBrush, null, labelBg, 4, 4);
        dc.DrawText(labelText, new Point(labelBg.X + 6, labelBg.Y + 3));
    }

    private Rect CropScreenRect
    {
        get
        {
            var origin = ImageToScreen(new Point(_cropRect.X, _cropRect.Y));
            return new Rect(origin.X, origin.Y, _cropRect.Width * _zoom, _cropRect.Height * _zoom);
        }
    }

    private IEnumerable<Point> CropHandlePoints(Rect rect)
    {
        yield return new Point(rect.Left, rect.Top);
        yield return new Point(rect.Left + rect.Width / 2, rect.Top);
        yield return new Point(rect.Right, rect.Top);
        yield return new Point(rect.Right, rect.Top + rect.Height / 2);
        yield return new Point(rect.Right, rect.Bottom);
        yield return new Point(rect.Left + rect.Width / 2, rect.Bottom);
        yield return new Point(rect.Left, rect.Bottom);
        yield return new Point(rect.Left, rect.Top + rect.Height / 2);
    }

    private CropHandle HitCropHandle(Point p)
    {
        var r = CropScreenRect;
        const double tolerance = 9;
        bool nearLeft = Math.Abs(p.X - r.Left) <= tolerance;
        bool nearRight = Math.Abs(p.X - r.Right) <= tolerance;
        bool nearTop = Math.Abs(p.Y - r.Top) <= tolerance;
        bool nearBottom = Math.Abs(p.Y - r.Bottom) <= tolerance;
        bool withinX = p.X >= r.Left - tolerance && p.X <= r.Right + tolerance;
        bool withinY = p.Y >= r.Top - tolerance && p.Y <= r.Bottom + tolerance;

        if (nearLeft && nearTop) return CropHandle.NW;
        if (nearRight && nearTop) return CropHandle.NE;
        if (nearRight && nearBottom) return CropHandle.SE;
        if (nearLeft && nearBottom) return CropHandle.SW;
        if (nearTop && withinX) return CropHandle.N;
        if (nearBottom && withinX) return CropHandle.S;
        if (nearLeft && withinY) return CropHandle.W;
        if (nearRight && withinY) return CropHandle.E;
        if (r.Contains(p)) return CropHandle.Move;
        if (ImageScreenRect.Contains(p)) return CropHandle.New;
        return CropHandle.None;
    }

    private static Cursor CursorForHandle(CropHandle handle) => handle switch
    {
        CropHandle.NW or CropHandle.SE => Cursors.SizeNWSE,
        CropHandle.NE or CropHandle.SW => Cursors.SizeNESW,
        CropHandle.N or CropHandle.S => Cursors.SizeNS,
        CropHandle.E or CropHandle.W => Cursors.SizeWE,
        CropHandle.Move => Cursors.SizeAll,
        CropHandle.New => Cursors.Cross,
        _ => Cursors.Arrow
    };

    private void UpdateCropFromDrag(Point screen)
    {
        var p = ScreenToImage(screen);
        var delta = p - _cropDragStart;
        var r = _cropStartRect;

        if (_cropHandle == CropHandle.Move)
        {
            r = new Rect(_cropStartRect.X + delta.X, _cropStartRect.Y + delta.Y, _cropStartRect.Width, _cropStartRect.Height);
            _cropRect = ClampCropRect(r);
            return;
        }

        if (_cropHandle == CropHandle.New)
        {
            double x = Math.Min(_cropDragStart.X, p.X);
            double y = Math.Min(_cropDragStart.Y, p.Y);
            double w = Math.Abs(p.X - _cropDragStart.X);
            double h = Math.Abs(p.Y - _cropDragStart.Y);
            r = new Rect(x, y, Math.Max(w, 1), Math.Max(h, 1));

            if (_cropRatio is double ratio)
            {
                if (r.Width / r.Height > ratio) r.Height = r.Width / ratio;
                else r.Width = r.Height * ratio;
                if (p.X < _cropDragStart.X) r.X = _cropDragStart.X - r.Width;
                if (p.Y < _cropDragStart.Y) r.Y = _cropDragStart.Y - r.Height;
            }

            _cropRect = ClampCropRect(r);
            return;
        }

        double left = _cropStartRect.Left;
        double top = _cropStartRect.Top;
        double right = _cropStartRect.Right;
        double bottom = _cropStartRect.Bottom;

        switch (_cropHandle)
        {
            case CropHandle.NW: left = p.X; top = p.Y; break;
            case CropHandle.N: top = p.Y; break;
            case CropHandle.NE: right = p.X; top = p.Y; break;
            case CropHandle.E: right = p.X; break;
            case CropHandle.SE: right = p.X; bottom = p.Y; break;
            case CropHandle.S: bottom = p.Y; break;
            case CropHandle.SW: left = p.X; bottom = p.Y; break;
            case CropHandle.W: left = p.X; break;
        }

        if (right - left < 4)
        {
            if (_cropHandle is CropHandle.NW or CropHandle.W or CropHandle.SW) left = right - 4;
            else right = left + 4;
        }

        if (bottom - top < 4)
        {
            if (_cropHandle is CropHandle.NW or CropHandle.N or CropHandle.NE) top = bottom - 4;
            else bottom = top + 4;
        }

        r = new Rect(left, top, right - left, bottom - top);
        if (_cropRatio is double ratio2)
        {
            r = ConstrainRatio(r, _cropHandle, ratio2);
        }

        _cropRect = ClampCropRect(r);
    }

    private static Rect ConstrainRatio(Rect raw, CropHandle handle, double ratio)
    {
        double w = Math.Max(raw.Width, 4);
        double h = Math.Max(raw.Height, 4);

        if (handle is CropHandle.N or CropHandle.S)
        {
            w = h * ratio;
        }
        else
        {
            h = w / ratio;
        }

        double x = raw.X;
        double y = raw.Y;

        if (handle is CropHandle.E or CropHandle.NE or CropHandle.SE) x = raw.Right - w;
        else if (handle is CropHandle.N or CropHandle.S) x = raw.X + (raw.Width - w) / 2;
        else if (handle is CropHandle.NW or CropHandle.W or CropHandle.SW) x = raw.Right - w;

        if (handle is CropHandle.S or CropHandle.SE or CropHandle.SW) y = raw.Bottom - h;
        else if (handle is CropHandle.E or CropHandle.W) y = raw.Y + (raw.Height - h) / 2;
        else if (handle is CropHandle.NW or CropHandle.N or CropHandle.NE) y = raw.Bottom - h;

        return new Rect(x, y, w, h);
    }

    // =====================================================================
    // 指针
    // =====================================================================

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_source == null) return;
        if (_fitMode) FitToView();
        else
        {
            ClampOffset();
            InvalidateLayers();
            RaiseViewChanged();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (_source == null) return;

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && !_cropActive)
        {
            FrameActivated?.Invoke(this, GetFrameAt(e.GetPosition(this)));
            e.Handled = true;
            return;
        }

        if (_cropActive)
        {
            var handle = HitCropHandle(e.GetPosition(this));
            if (handle == CropHandle.None) return;
            _cropHandle = handle;
            _cropDragging = true;
            _cropDragStart = ScreenToImage(e.GetPosition(this));
            _cropStartRect = _cropRect;
            if (handle == CropHandle.New)
            {
                _cropRect = new Rect(_cropDragStart, new Size(1, 1));
            }

            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            _dragging = true;
            _dragMoved = false;
            _dragStartScreen = e.GetPosition(this);
            _dragLastScreen = _dragStartScreen;
            _dragStartOffset = _offset;
            _dragStartedFrame = GetFrameAt(_dragStartScreen);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_source == null) return;
        var position = e.GetPosition(this);

        if (_cropActive)
        {
            if (_cropDragging)
            {
                UpdateCropFromDrag(position);
                InvalidateOverlay();
                CropChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Cursor = CursorForHandle(HitCropHandle(position));
            }

            return;
        }

        if (_dragging)
        {
            var delta = position - _dragLastScreen;
            if (Math.Abs(position.X - _dragStartScreen.X) > 3 || Math.Abs(position.Y - _dragStartScreen.Y) > 3)
            {
                _dragMoved = true;
            }

            _offset = new Point(_dragStartOffset.X + (position.X - _dragStartScreen.X),
                _dragStartOffset.Y + (position.Y - _dragStartScreen.Y));
            ClampOffset();
            InvalidateLayers();
            RaiseViewChanged();
            _dragLastScreen = position;
        }
        else
        {
            UpdateHover(position);
            Cursor = ImageScreenRect.Contains(position) ? Cursors.Hand : Cursors.Arrow;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_cropActive)
        {
            if (_cropDragging)
            {
                _cropDragging = false;
                ReleaseMouseCapture();
                CropChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Cursor = ImageScreenRect.Contains(e.GetPosition(this)) ? Cursors.Hand : Cursors.Arrow;

        if (!_dragMoved && e.ChangedButton == MouseButton.Left)
        {
            FrameClicked?.Invoke(this, GetFrameAt(e.GetPosition(this)));
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_source == null || _cropActive) return;
        double factor = Math.Pow(1.2, e.Delta / 120.0);
        SetZoom(_zoom * factor, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_cropActive || _dragging) return;
        if (_hoveredFrame != null)
        {
            _hoveredFrame = null;
            InvalidateOverlay();
        }

        Cursor = Cursors.Arrow;
        PointerChanged?.Invoke(this, new PointerInfoEventArgs { Inside = false });
    }

    private void UpdateHover(Point screen)
    {
        if (_source == null)
        {
            return;
        }

        var image = ScreenToImage(screen);
        bool inside = image.X >= 0 && image.Y >= 0 && image.X < ImageWidth && image.Y < ImageHeight;
        int px = (int)Math.Floor(image.X);
        int py = (int)Math.Floor(image.Y);
        int? frame = inside && FrameGrid.IsActive ? FrameGrid.FrameIndexAt(image.X, image.Y, ImageWidth, ImageHeight) : null;

        if (frame != _hoveredFrame)
        {
            _hoveredFrame = frame;
            InvalidateOverlay();
        }

        PointerChanged?.Invoke(this, new PointerInfoEventArgs
        {
            Inside = inside,
            ImageX = image.X,
            ImageY = image.Y,
            PixelX = px,
            PixelY = py,
            FrameIndex = frame
        });
    }

    // =====================================================================
    // 图层
    // =====================================================================

    private sealed class ImageLayer : FrameworkElement
    {
        private readonly SpriteView _owner;

        public ImageLayer(SpriteView owner) => _owner = owner;

        protected override void OnRender(DrawingContext dc) => _owner.DrawImageLayer(dc);
    }

    private sealed class OverlayLayer : FrameworkElement
    {
        private readonly SpriteView _owner;

        public OverlayLayer(SpriteView owner) => _owner = owner;

        protected override void OnRender(DrawingContext dc) => _owner.DrawOverlayLayer(dc);
    }

    private static DrawingBrush CreateCheckerBrush(Color a, Color b)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        var second = new SolidColorBrush(b);
        group.Children.Add(new GeometryDrawing(second, null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        group.Children.Add(new GeometryDrawing(second, null, new RectangleGeometry(new Rect(10, 10, 10, 10))));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 20, 20),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 20, 20),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }
}
