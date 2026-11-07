using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpriteLens.Core;

public enum EditKind
{
    RotateCw,
    RotateCcw,
    FlipHorizontal,
    FlipVertical,
    Crop
}

public readonly record struct EditOp(EditKind Kind, Int32Rect Rect = default);

public readonly record struct Adjustments(int Brightness, int Contrast, int Saturation)
{
    public bool IsIdentity => Brightness == 0 && Contrast == 0 && Saturation == 0;

    public static Adjustments None => default;
}

/// <summary>
/// 非破坏式编辑：所有操作可重放，任何时候都能回到原图。</summary>
public static class EditPipeline
{
    public static BitmapSource Apply(BitmapSource source, IReadOnlyList<EditOp> ops)
    {
        BitmapSource current = source;
        foreach (var op in ops)
        {
            current = ApplyOne(current, op);
        }

        return current;
    }

    private static BitmapSource ApplyOne(BitmapSource source, EditOp op)
    {
        BitmapSource result;
        switch (op.Kind)
        {
            case EditKind.RotateCw:
                result = new TransformedBitmap(source, new RotateTransform(90));
                break;
            case EditKind.RotateCcw:
                result = new TransformedBitmap(source, new RotateTransform(270));
                break;
            case EditKind.FlipHorizontal:
                result = new TransformedBitmap(source, new ScaleTransform(-1, 1));
                break;
            case EditKind.FlipVertical:
                result = new TransformedBitmap(source, new ScaleTransform(1, -1));
                break;
            case EditKind.Crop:
            {
                var rect = ClampRect(op.Rect, source.PixelWidth, source.PixelHeight);
                result = rect.Width <= 0 || rect.Height <= 0
                    ? source
                    : new CroppedBitmap(source, rect);
                break;
            }
            default:
                result = source;
                break;
        }

        if (!ReferenceEquals(result, source) && result.CanFreeze)
        {
            result.Freeze();
        }

        return result;
    }

    public static Int32Rect ClampRect(Int32Rect rect, int width, int height)
    {
        int x = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
        int y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
        int w = Math.Min(rect.Width, width - x);
        int h = Math.Min(rect.Height, height - y);
        return w <= 0 || h <= 0 ? Int32Rect.Empty : new Int32Rect(x, y, w, h);
    }

    /// <summary>亮度 / 对比度 / 饱和度，纯 CPU 处理，输出冻结的 Bgra32 位图。</summary>
    public static BitmapSource ApplyAdjustments(BitmapSource source, Adjustments adjustments)
    {
        if (adjustments.IsIdentity) return source;

        BitmapSource bgra = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            bgra = converted;
        }

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[(long)stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        double factor = (100 + adjustments.Contrast) / 100.0;
        double saturation = (100 + adjustments.Saturation) / 100.0;
        double add = adjustments.Brightness * 1.2;
        bool adjustSaturation = Math.Abs(saturation - 1.0) > 0.0001;

        Parallel.For(0, height, y =>
        {
            int i = y * stride;
            for (int x = 0; x < width; x++, i += 4)
            {
                double b = pixels[i];
                double g = pixels[i + 1];
                double r = pixels[i + 2];

                r = (r - 128) * factor + 128 + add;
                g = (g - 128) * factor + 128 + add;
                b = (b - 128) * factor + 128 + add;

                if (adjustSaturation)
                {
                    double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = luma + (r - luma) * saturation;
                    g = luma + (g - luma) * saturation;
                    b = luma + (b - luma) * saturation;
                }

                pixels[i] = ToByte(b);
                pixels[i + 1] = ToByte(g);
                pixels[i + 2] = ToByte(r);
            }
        });

        var result = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static byte ToByte(double value) => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5);
}
