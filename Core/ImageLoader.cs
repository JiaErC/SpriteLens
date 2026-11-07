using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpriteLens.Core;

public sealed record LoadedImage(BitmapSource Bitmap, int Width, int Height);

public static class ImageLoader
{
    public const int MaxSamplePixels = 24_000_000;

    private static readonly string[] Extensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico", ".wdp", ".jfif"
    };

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static async Task<LoadedImage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Load(path, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public static LoadedImage Load(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            cancellationToken.ThrowIfCancellationRequested();

            if (decoder.Frames.Count == 0)
            {
                throw new InvalidOperationException("文件中没有可读取的图像帧。");
            }

            BitmapSource frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 80_000_000L)
            {
                throw new InvalidOperationException($"图片尺寸过大（{frame.PixelWidth} × {frame.PixelHeight}）。");
            }

            // BitmapFrame 可能仍依赖后台线程上的解码器；复制成独立的冻结位图，
            // 之后的旋转、裁剪、取色都能在 UI 线程安全使用。
            BitmapSource bitmap = CreateFrozenCopy(frame);
            bitmap = ApplyExifOrientation(bitmap);
            if (!bitmap.IsFrozen && bitmap.CanFreeze) bitmap.Freeze();
            return new LoadedImage(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException("文件不存在或已被移动。");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException("没有权限读取该文件。");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"无法读取图像。{(Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase) ? "WebP 需要系统解码器支持。" : string.Empty)}",
                ex);
        }
    }

    private static BitmapSource CreateFrozenCopy(BitmapSource source)
    {
        BitmapSource converted = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            var format = new FormatConvertedBitmap();
            format.BeginInit();
            format.Source = source;
            format.DestinationFormat = PixelFormats.Bgra32;
            format.EndInit();
            converted = format;
        }

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[(long)stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var copy = BitmapSource.Create(width, height, converted.DpiX, converted.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }

    /// <summary>按 EXIF 方向标记旋转/镜像，手机照片与部分截图会用到。</summary>
    private static BitmapSource ApplyExifOrientation(BitmapSource source)
    {
        try
        {
            if (source.Metadata is not BitmapMetadata metadata) return source;

            ushort orientation = 1;
            foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try
                {
                    if (!metadata.ContainsQuery(query)) continue;
                    object? value = metadata.GetQuery(query);
                    if (value != null) orientation = Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                    break;
                }
                catch
                {
                    // 某些格式不支持该查询，忽略即可。
                }
            }

            if (orientation <= 1 || orientation > 8) return source;

            BitmapSource result = orientation switch
            {
                2 => new TransformedBitmap(source, new ScaleTransform(-1, 1)),
                3 => new TransformedBitmap(source, new RotateTransform(180)),
                4 => new TransformedBitmap(source, new ScaleTransform(1, -1)),
                5 => new TransformedBitmap(new TransformedBitmap(source, new RotateTransform(90)), new ScaleTransform(-1, 1)),
                6 => new TransformedBitmap(source, new RotateTransform(90)),
                7 => new TransformedBitmap(new TransformedBitmap(source, new RotateTransform(270)), new ScaleTransform(-1, 1)),
                8 => new TransformedBitmap(source, new RotateTransform(270)),
                _ => source
            };

            if (!ReferenceEquals(result, source)) result.Freeze();
            return result;
        }
        catch
        {
            return source;
        }
    }

    public static async Task<ImageSource?> LoadThumbnailAsync(string path, int decodeWidth, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = Math.Max(32, decodeWidth);
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return (ImageSource?)image;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把位图解码为 BGRA32 数组，用于快速取色。过大的图像返回 null。</summary>
    public static byte[]? TryCreatePixelBuffer(BitmapSource source, out int width, out int height,
        int maxPixels = MaxSamplePixels)
    {
        width = source.PixelWidth;
        height = source.PixelHeight;
        long area = (long)width * height;
        if (area <= 0 || area > maxPixels) return null;

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

        int stride = width * 4;
        var data = new byte[(long)stride * height];
        bgra.CopyPixels(data, stride, 0);
        return data;
    }

    public static Color? Sample(byte[] pixels, int width, int height, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        int i = (y * width + x) * 4;
        if (i < 0 || i + 3 >= pixels.Length) return null;
        return Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    public static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    public static string GetFormatName(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "JPG" or "JPEG" or "JPE" or "JFIF" => "JPEG 图像",
            "PNG" => "PNG 图像",
            "BMP" => "BMP 图像",
            "GIF" => "GIF 图像",
            "WEBP" => "WebP 图像",
            "TIF" or "TIFF" => "TIFF 图像",
            "ICO" => "图标",
            "" => "图像",
            _ => ext + " 图像"
        };
    }
}
