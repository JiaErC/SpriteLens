using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpriteLens.Core;

/// <summary>
/// 当前图片的编辑状态：原图、操作栈、调整参数、显示位图与取色缓存。
/// </summary>
public sealed class ImageDocument
{
    private readonly List<EditOp> _ops = new();
    private byte[]? _pixelBuffer;
    private int _pixelWidth;
    private int _pixelHeight;
    private bool _pixelBufferTried;

    public ImageDocument(string path, BitmapSource original)
    {
        Path = path;
        Original = original;
        Working = original;
        Display = original;
    }

    public string Path { get; }

    public BitmapSource Original { get; }

    public BitmapSource Working { get; private set; }

    public BitmapSource Display { get; private set; }

    public Adjustments Adjustments { get; private set; } = Adjustments.None;

    public IReadOnlyList<EditOp> Ops => _ops;

    public EditOp? LastUndoneOp { get; private set; }

    public int Width => Working.PixelWidth;

    public int Height => Working.PixelHeight;

    public bool HasEdits => _ops.Count > 0 || !Adjustments.IsIdentity;

    public event EventHandler? Changed;

    public void ApplyEdit(EditOp op)
    {
        _ops.Add(op);
        Rebuild();
    }

    public bool Undo()
    {
        if (_ops.Count == 0) return false;
        LastUndoneOp = _ops[^1];
        _ops.RemoveAt(_ops.Count - 1);
        Rebuild();
        return true;
    }

    public void ResetEdits()
    {
        LastUndoneOp = null;
        if (_ops.Count == 0) return;
        _ops.Clear();
        Rebuild();
    }

    public void SetAdjustments(Adjustments adjustments)
    {
        if (Adjustments == adjustments) return;
        Adjustments = adjustments;
        Display = adjustments.IsIdentity ? Working : EditPipeline.ApplyAdjustments(Working, adjustments);
        if (Display.CanFreeze && !Display.IsFrozen) Display.Freeze();
        InvalidatePixels();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyAdjustmentsChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void Rebuild()
    {
        Working = EditPipeline.Apply(Original, _ops);
        Display = Adjustments.IsIdentity ? Working : EditPipeline.ApplyAdjustments(Working, Adjustments);
        InvalidatePixels();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void InvalidatePixels()
    {
        _pixelBuffer = null;
        _pixelBufferTried = false;
    }

    public Color? GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Display.PixelWidth || y >= Display.PixelHeight) return null;
        if (!_pixelBufferTried)
        {
            _pixelBufferTried = true;
            _pixelBuffer = ImageLoader.TryCreatePixelBuffer(Display, out _pixelWidth, out _pixelHeight);
        }

        return _pixelBuffer == null
            ? null
            : ImageLoader.Sample(_pixelBuffer, _pixelWidth, _pixelHeight, x, y);
    }

    public Int32Rect ClampFrameRect(Int32Rect rect) => EditPipeline.ClampRect(rect, Display.PixelWidth, Display.PixelHeight);
}
