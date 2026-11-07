using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpriteLens.Core;

/// <summary>导出与打印：帧裁切、带网格标记的 PNG、打印版面。</summary>
public static class ImageExport
{
    public static void SaveFrame(BitmapSource source, Int32Rect frameRect, string path)
    {
        var rect = EditPipeline.ClampRect(frameRect, source.PixelWidth, source.PixelHeight);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new InvalidOperationException("帧区域无效。");
        }

        var cropped = new CroppedBitmap(source, rect);
        cropped.Freeze();
        ImageLoader.SavePng(cropped, path);
    }

    public static void SaveWithGrid(BitmapSource source, GridSpec grid, string path)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
            double core = Math.Max(1, Math.Round(Math.Max(width, height) / 1024.0) * Math.Max(1, grid.Thickness));
            DrawGrid(dc, new Rect(0, 0, width, height), width, height, grid, core, snap: true);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        ImageLoader.SavePng(bitmap, path);
    }

    public static DrawingVisual CreatePrintVisual(BitmapSource source, GridSpec? grid, string title,
        double pageWidth, double pageHeight)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));

            double margin = 48;
            double titleHeight = 30;
            double availableWidth = Math.Max(1, pageWidth - margin * 2);
            double availableHeight = Math.Max(1, pageHeight - margin * 2 - titleHeight);
            double scale = Math.Min(availableWidth / source.PixelWidth, availableHeight / source.PixelHeight);
            double width = source.PixelWidth * scale;
            double height = source.PixelHeight * scale;
            var destination = new Rect(
                (pageWidth - width) / 2,
                margin + titleHeight + (availableHeight - height) / 2,
                width,
                height);

            dc.DrawImage(source, destination);

            if (grid is { IsActive: true })
            {
                DrawGrid(dc, destination, source.PixelWidth, source.PixelHeight, grid, 1.2, snap: false);
            }

            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(222, 226, 234)), 0.8);
            borderPen.Freeze();
            dc.DrawRectangle(null, borderPen, destination);

            var titleText = new FormattedText(
                string.IsNullOrWhiteSpace(title) ? "精灵镜导出" : title,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                11,
                new SolidColorBrush(Color.FromRgb(90, 98, 112)),
                1.0);
            dc.DrawText(titleText, new Point(margin, margin));

            string footerText = grid is { IsActive: true }
                ? $"精灵镜 · 帧 {grid.FrameWidth} × {grid.FrameHeight} · {grid.Columns(source.PixelWidth)} × {grid.Rows(source.PixelHeight)}"
                : "精灵镜 · SpriteLens";
            var footer = new FormattedText(
                footerText,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                9,
                new SolidColorBrush(Color.FromRgb(140, 147, 160)),
                1.0);
            dc.DrawText(footer, new Point(pageWidth - margin - footer.Width, pageHeight - margin + 10));
        }

        return visual;
    }

    private static void DrawGrid(DrawingContext dc, Rect area, int imageWidth, int imageHeight, GridSpec grid,
        double core, bool snap)
    {
        int cols = grid.Columns(imageWidth);
        int rows = grid.Rows(imageHeight);
        if (cols <= 0 || rows <= 0) return;

        double sx = area.Width / imageWidth;
        double sy = area.Height / imageHeight;

        var xs = new List<double>(Math.Min(cols + 2, 256)) { area.Left, area.Right };
        var ys = new List<double>(Math.Min(rows + 2, 256)) { area.Top, area.Bottom };

        for (int k = 1; k < cols; k++)
        {
            double x = area.Left + k * grid.FrameWidth * sx;
            if (x > area.Left + 0.5 && x < area.Right - 0.5) xs.Add(Align(x, core, snap));
        }

        for (int k = 1; k < rows; k++)
        {
            double y = area.Top + k * grid.FrameHeight * sy;
            if (y > area.Top + 0.5 && y < area.Bottom - 0.5) ys.Add(Align(y, core, snap));
        }

        xs[0] = Align(area.Left, core, snap);
        xs[1] = Align(area.Right, core, snap);
        ys[0] = Align(area.Top, core, snap);
        ys[1] = Align(area.Bottom, core, snap);

        var haloBrush = new SolidColorBrush(Colors.White);
        var coreBrush = new SolidColorBrush(Color.FromRgb(8, 10, 14));
        var accentBrush = new SolidColorBrush(Color.FromRgb(70, 226, 203));

        dc.PushClip(new RectangleGeometry(area));

        switch (grid.Style)
        {
            case GridLineStyle.Dual:
                DrawLineSet(dc, new Pen(haloBrush, core + 2), xs, ys, area);
                DrawLineSet(dc, new Pen(coreBrush, core), xs, ys, area);
                break;
            case GridLineStyle.Dash:
                DrawLineSet(dc, new Pen(coreBrush, core), xs, ys, area);
                var dash = new Pen(haloBrush, Math.Max(core, 0.8))
                {
                    DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0),
                    DashCap = PenLineCap.Flat
                };
                dash.Freeze();
                DrawLineSet(dc, dash, xs, ys, area);
                break;
            case GridLineStyle.Accent:
                DrawLineSet(dc, new Pen(coreBrush, core + 2), xs, ys, area);
                DrawLineSet(dc, new Pen(accentBrush, core), xs, ys, area);
                break;
        }

        dc.Pop();
    }

    private static void DrawLineSet(DrawingContext dc, Pen pen, List<double> xs, List<double> ys, Rect area)
    {
        foreach (double x in xs)
        {
            dc.DrawLine(pen, new Point(x, area.Top), new Point(x, area.Bottom));
        }

        foreach (double y in ys)
        {
            dc.DrawLine(pen, new Point(area.Left, y), new Point(area.Right, y));
        }
    }

    private static double Align(double value, double thickness, bool snap)
    {
        if (!snap) return value;
        double rounded = Math.Round(value);
        return (int)Math.Round(thickness) % 2 == 1 ? rounded + 0.5 : rounded;
    }
}
