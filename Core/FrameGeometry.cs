using System.Windows;

namespace SpriteLens.Core;

public enum GridLineStyle
{
    Dual,
    Dash,
    Accent
}

public enum ImageBackground
{
    Checker,
    Dark,
    Light,
    Black,
    White
}

/// <summary>
/// 帧网格参数与几何计算。所有尺寸都以图像像素为单位。
/// </summary>
public sealed class GridSpec
{
    public bool Enabled { get; set; } = true;
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public GridLineStyle Style { get; set; } = GridLineStyle.Dual;
    public int Thickness { get; set; } = 1;
    public bool Labels { get; set; } = true;
    public bool Highlight { get; set; } = true;

    public bool IsActive => Enabled && FrameWidth > 0 && FrameHeight > 0;

    public int Columns(int imageWidth) => IsActive ? (int)((imageWidth + (long)FrameWidth - 1) / FrameWidth) : 0;

    public int Rows(int imageHeight) => IsActive ? (int)((imageHeight + (long)FrameHeight - 1) / FrameHeight) : 0;

    public int FrameCount(int imageWidth, int imageHeight)
    {
        if (!IsActive) return 0;
        return Columns(imageWidth) * Rows(imageHeight);
    }

    public int RemainderX(int imageWidth) => IsActive && FrameWidth > 0 ? imageWidth % FrameWidth : 0;

    public int RemainderY(int imageHeight) => IsActive && FrameHeight > 0 ? imageHeight % FrameHeight : 0;

    /// <summary>返回帧在图像坐标系中的矩形（最后一列/行会被裁剪到图像边缘）。</summary>
    public Int32Rect GetFrameRect(int index, int imageWidth, int imageHeight)
    {
        if (!IsActive || index < 0) return Int32Rect.Empty;
        int cols = Columns(imageWidth);
        if (cols <= 0) return Int32Rect.Empty;
        int row = index / cols;
        int col = index % cols;
        int x = col * FrameWidth;
        int y = row * FrameHeight;
        int w = Math.Min(FrameWidth, imageWidth - x);
        int h = Math.Min(FrameHeight, imageHeight - y);
        if (w <= 0 || h <= 0) return Int32Rect.Empty;
        return new Int32Rect(x, y, w, h);
    }

    /// <summary>把图像坐标映射到帧序号；不在图像内返回 null。</summary>
    public int? FrameIndexAt(double x, double y, int imageWidth, int imageHeight)
    {
        if (!IsActive) return null;
        if (x < 0 || y < 0 || x >= imageWidth || y >= imageHeight) return null;
        int col = (int)Math.Floor(x / FrameWidth);
        int row = (int)Math.Floor(y / FrameHeight);
        if (col < 0 || row < 0) return null;
        int cols = Columns(imageWidth);
        int rows = Rows(imageHeight);
        if (col >= cols || row >= rows) return null;
        return row * cols + col;
    }
}

/// <summary>裁剪框的比例约束。</summary>
public enum CropRatioKind
{
    Free,
    Square,
    Frame,
    FourThree,
    SixteenNine
}
