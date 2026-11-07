using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpriteLens.Core;

namespace SpriteLens.Controls;

/// <summary>动画预览小窗：只绘制当前帧，按整数倍缩放保持像素锐利。</summary>
public sealed class FramePreview : FrameworkElement
{
    private BitmapSource? _source;
    private Int32Rect _frameRect;
    private DrawingBrush? _checker;
    private SolidColorBrush _checkerA = new(Color.FromRgb(26, 31, 41));
    private SolidColorBrush _checkerB = new(Color.FromRgb(35, 41, 54));

    public FramePreview()
    {
        SnapsToDevicePixels = true;
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var a = TryFindResource("B.CheckerA") as SolidColorBrush;
        var b = TryFindResource("B.CheckerB") as SolidColorBrush;
        _checkerA = new SolidColorBrush(a?.Color ?? Color.FromRgb(26, 31, 41));
        _checkerB = new SolidColorBrush(b?.Color ?? Color.FromRgb(35, 41, 54));
        _checker = BuildChecker(_checkerA.Color, _checkerB.Color);
        InvalidateVisual();
    }

    public void SetFrame(BitmapSource? source, Int32Rect frameRect)
    {
        _source = source;
        _frameRect = frameRect;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (_checker != null)
        {
            dc.DrawRectangle(_checker, null, bounds);
        }
        else
        {
            dc.DrawRectangle(_checkerA, null, bounds);
        }

        if (_source == null) return;

        var frame = _frameRect;
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            frame = new Int32Rect(0, 0, _source.PixelWidth, _source.PixelHeight);
        }

        frame = EditPipeline.ClampRect(frame, _source.PixelWidth, _source.PixelHeight);
        if (frame.Width <= 0 || frame.Height <= 0) return;

        const double padding = 10;
        double availableW = Math.Max(1, bounds.Width - padding * 2);
        double availableH = Math.Max(1, bounds.Height - padding * 2);
        double scale = Math.Min(availableW / frame.Width, availableH / frame.Height);
        if (scale <= 0) return;

        bool sharp = scale >= 1;
        if (sharp) scale = Math.Floor(scale);
        RenderOptions.SetBitmapScalingMode(this, sharp ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        double width = frame.Width * scale;
        double height = frame.Height * scale;
        var destination = new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);

        dc.PushClip(new RectangleGeometry(destination));
        dc.DrawImage(_source, new Rect(
            destination.X - frame.X * scale,
            destination.Y - frame.Y * scale,
            _source.PixelWidth * scale,
            _source.PixelHeight * scale));
        dc.Pop();
    }

    private static DrawingBrush BuildChecker(Color a, Color b)
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
