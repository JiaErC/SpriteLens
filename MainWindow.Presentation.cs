using System.Windows;

namespace SpriteLens;

public partial class MainWindow
{
    private Rect _savedWindowBounds;
    private WindowState _savedWindowState;
    private ResizeMode _savedResizeMode;
    private double _savedMaxWidth = double.PositiveInfinity;
    private double _savedMaxHeight = double.PositiveInfinity;
    private bool _immersiveSaved;
    private bool _slideshowImmersive;

    /// <summary>无边框全屏：窗口置顶并覆盖任务栏，退出时恢复原尺寸与状态。</summary>
    private void EnterImmersiveMode()
    {
        if (!_immersiveSaved)
        {
            _savedWindowState = WindowState;
            _savedWindowBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            _savedResizeMode = ResizeMode;
            _savedMaxWidth = MaxWidth;
            _savedMaxHeight = MaxHeight;
            _immersiveSaved = true;
        }

        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;
        WindowBorder.BorderThickness = new Thickness(0);
        MaxWidth = SystemParameters.PrimaryScreenWidth;
        MaxHeight = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Topmost = true;
    }

    private void ExitImmersiveMode()
    {
        if (!_immersiveSaved) return;

        Topmost = false;
        ResizeMode = _savedResizeMode;
        MaxWidth = _savedMaxWidth;
        MaxHeight = _savedMaxHeight;
        WindowBorder.BorderThickness = new Thickness(1);
        WindowState = WindowState.Normal;
        Left = _savedWindowBounds.Left;
        Top = _savedWindowBounds.Top;
        Width = Math.Max(MinWidth, _savedWindowBounds.Width);
        Height = Math.Max(MinHeight, _savedWindowBounds.Height);
        WindowState = _savedWindowState;
        _immersiveSaved = false;
    }
}
