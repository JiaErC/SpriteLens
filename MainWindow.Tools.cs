using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SpriteLens.Core;
using SpriteLens.UI;

namespace SpriteLens;

public partial class MainWindow
{
    // =====================================================================
    // 裁剪
    // =====================================================================

    private void Crop_Click(object sender, RoutedEventArgs e) => EnterCropMode();

    private void EnterCropMode()
    {
        if (_document == null)
        {
            ShowToast("先打开一张图片。", ToastKind.Warn);
            return;
        }

        if (_cropMode) return;

        _cropMode = true;
        Int32Rect? initial = null;
        if (_selectedFrame is int index && Sprite.FrameGrid.IsActive)
        {
            var rect = Sprite.FrameGrid.GetFrameRect(index, _document.Width, _document.Height);
            if (rect.Width > 0 && rect.Height > 0) initial = rect;
        }

        Sprite.BeginCrop(initial);
        Sprite.FitToView();
        FloatBar.Visibility = Visibility.Collapsed;
        CropBar.Visibility = Visibility.Visible;
        _suppressCropRatio = true;
        CropFreeRadio.IsChecked = true;
        _suppressCropRatio = false;
        if (_adjustOpen) AdjustCard.Visibility = Visibility.Collapsed;
        UpdateInfoPanel();
    }

    private void ExitCropMode(bool apply)
    {
        if (!_cropMode) return;
        _cropMode = false;
        CropBar.Visibility = Visibility.Collapsed;
        if (_adjustOpen && _document != null) AdjustCard.Visibility = Visibility.Visible;

        if (_document == null)
        {
            Sprite.EndCrop();
            return;
        }

        if (apply)
        {
            var rect = Sprite.GetCropPixelRect();
            Sprite.EndCrop();
            _document.ApplyEdit(new EditOp(EditKind.Crop, rect));
            _selectedFrame = null;
            Sprite.SelectedFrame = null;
            Sprite.SetImage(_document.Display);
            FloatBar.Visibility = Visibility.Visible;
            UpdateFrameBadge();
            UpdateStatus();
            UpdateFrameStatus(null);
            UpdateInfoPanel();
            UpdatePreviewFrame();
            ShowToast($"已裁剪为 {rect.Width} × {rect.Height} px。", ToastKind.Success);
        }
        else
        {
            Sprite.EndCrop();
            Sprite.FitToView();
            if (_document != null) FloatBar.Visibility = Visibility.Visible;
        }
    }

    private void CropRatio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _suppressCropRatio || !_cropMode) return;
        string? tag = (sender as FrameworkElement)?.Tag as string;
        var kind = tag switch
        {
            "1:1" => CropRatioKind.Square,
            "frame" => CropRatioKind.Frame,
            "4:3" => CropRatioKind.FourThree,
            "16:9" => CropRatioKind.SixteenNine,
            _ => CropRatioKind.Free
        };
        Sprite.SetCropRatioKind(kind);
    }

    private void CropApply_Click(object sender, RoutedEventArgs e) => ExitCropMode(apply: true);

    private void CropCancel_Click(object sender, RoutedEventArgs e) => ExitCropMode(apply: false);

    // =====================================================================
    // 颜色调整
    // =====================================================================

    private void Adjust_Click(object sender, RoutedEventArgs e) => ShowAdjustCard();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        var op = _document.LastUndoneOp;
        if (!_document.Undo())
        {
            ShowToast("没有可撤销的编辑。", ToastKind.Warn);
            return;
        }

        if (op is { Kind: EditKind.RotateCw or EditKind.RotateCcw } && Sprite.FrameGrid.IsActive)
        {
            int width = Sprite.FrameGrid.FrameWidth;
            int height = Sprite.FrameGrid.FrameHeight;
            if (width > 0 && height > 0)
            {
                SetFrameBoxes(height, width);
            }
        }

        _selectedFrame = null;
        Sprite.SelectedFrame = null;
        Sprite.SetImage(_document.Display);
        UpdateFrameBadge();
        UpdateStatus();
        UpdateFrameStatus(null);
        UpdateInfoPanel();
        UpdatePreviewFrame();
        UpdateAdjustButtonState();
        ShowToast("已撤销上一步编辑。", ToastKind.Success);
    }

    private void ShowAdjustCard()
    {
        if (_document == null)
        {
            ShowToast("先打开一张图片。", ToastKind.Warn);
            return;
        }

        if (_cropMode)
        {
            ShowToast("请先应用或取消裁剪。", ToastKind.Warn);
            return;
        }

        _adjustOpen = true;
        AdjustCard.Visibility = Visibility.Visible;
        SetAdjustSliders(_document.Adjustments.Brightness, _document.Adjustments.Contrast, _document.Adjustments.Saturation);
        UpdateInfoPanel();
    }

    private void AdjustClose_Click(object sender, RoutedEventArgs e)
    {
        _adjustOpen = false;
        AdjustCard.Visibility = Visibility.Collapsed;
    }

    private void AdjustReset_Click(object sender, RoutedEventArgs e)
    {
        SetAdjustSliders(0, 0, 0);
        ApplyAdjustmentsFromSliders();
    }

    private void Adjustment_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isReady || _suppressAdjust) return;
        UpdateAdjustOutputs();
        _adjustTimer.Stop();
        _adjustTimer.Start();
    }

    private void UpdateAdjustOutputs()
    {
        BrightnessValue.Text = ((int)Math.Round(BrightnessSlider.Value)).ToString();
        ContrastValue.Text = ((int)Math.Round(ContrastSlider.Value)).ToString();
        SaturationValue.Text = ((int)Math.Round(SaturationSlider.Value)).ToString();
    }

    private void SetAdjustSliders(int brightness, int contrast, int saturation)
    {
        _suppressAdjust = true;
        BrightnessSlider.Value = Math.Clamp(brightness, -100, 100);
        ContrastSlider.Value = Math.Clamp(contrast, -100, 100);
        SaturationSlider.Value = Math.Clamp(saturation, -100, 100);
        _suppressAdjust = false;
        UpdateAdjustOutputs();
    }

    private void ApplyAdjustmentsFromSliders()
    {
        if (_document == null) return;

        var adjustments = new Adjustments(
            (int)Math.Round(BrightnessSlider.Value),
            (int)Math.Round(ContrastSlider.Value),
            (int)Math.Round(SaturationSlider.Value));

        _document.SetAdjustments(adjustments);
        Sprite.UpdateBitmap(_document.Display);
        UpdateAdjustButtonState();
        UpdateFileMeta();
        UpdateInfoPanel();
        UpdatePreviewFrame();
    }

    private void UpdateAdjustButtonState()
    {
        bool active = _document != null && !_document.Adjustments.IsIdentity;
        if (active)
        {
            AdjustButton.SetResourceReference(Control.ForegroundProperty, "B.Accent");
            AdjustButton.Background = (Brush)FindResource("B.AccentSoft");
        }
        else
        {
            AdjustButton.ClearValue(Control.ForegroundProperty);
            AdjustButton.ClearValue(Control.BackgroundProperty);
        }
    }

    private void ResetAdjustForNewImage()
    {
        _adjustTimer.Stop();
        SetAdjustSliders(0, 0, 0);
        UpdateAdjustButtonState();
    }

    private void ResetTransientToolsForNewImage()
    {
        _cropMode = false;
        CropBar.Visibility = Visibility.Collapsed;
        _adjustTimer.Stop();
        StopPreviewPlayback();
        _previewFrame = 0;
        if (_adjustOpen) AdjustCard.Visibility = Visibility.Visible;
    }

    // =====================================================================
    // 帧预览
    // =====================================================================

    private void Preview_Click(object sender, RoutedEventArgs e) => ShowPreviewCard();

    private void ShowPreviewCard()
    {
        if (_document == null)
        {
            ShowToast("先打开一张图片。", ToastKind.Warn);
            return;
        }

        _previewOpen = true;
        PreviewCard.Visibility = Visibility.Visible;
        PreviewCanvas.ApplyTheme();
        UpdatePreviewFrame();
        UpdatePreviewPlayState();
    }

    private void PreviewClose_Click(object sender, RoutedEventArgs e)
    {
        StopPreviewPlayback();
        _previewOpen = false;
        PreviewCard.Visibility = Visibility.Collapsed;
    }

    private void UpdatePreviewFrame(int? frameOverride = null)
    {
        if (!_previewOpen && frameOverride == null) return;
        if (_document == null)
        {
            PreviewCanvas.SetFrame(null, Int32Rect.Empty);
            PreviewFrameLabel.Text = "—";
            return;
        }

        var grid = Sprite.FrameGrid;
        if (!grid.IsActive)
        {
            PreviewCanvas.SetFrame(_document.Display, new Int32Rect(0, 0, _document.Width, _document.Height));
            PreviewFrameLabel.Text = "设置帧尺寸后可预览";
            PreviewPlayButton.IsEnabled = false;
            return;
        }

        int total = grid.FrameCount(_document.Width, _document.Height);
        if (total <= 0) return;

        int index = frameOverride ?? (_selectedFrame ?? _previewFrame);
        index = Math.Clamp(index, 0, total - 1);
        _previewFrame = index;

        var rect = grid.GetFrameRect(index, _document.Width, _document.Height);
        PreviewCanvas.SetFrame(_document.Display, rect);

        int cols = grid.Columns(_document.Width);
        PreviewFrameLabel.Text = $"帧 {index + 1} / {total} · 列 {index % cols + 1} 行 {index / cols + 1}";
        PreviewPlayButton.IsEnabled = true;
    }

    private void PreviewPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPlaying) StopPreviewPlayback();
        else StartPreviewPlayback();
    }

    private void StartPreviewPlayback()
    {
        if (_document == null || !Sprite.FrameGrid.IsActive)
        {
            ShowToast("设置帧尺寸后才能播放。", ToastKind.Warn);
            return;
        }

        int total = Sprite.FrameGrid.FrameCount(_document.Width, _document.Height);
        if (total <= 0) return;

        int start = Math.Clamp(_selectedFrame ?? _previewFrame, 0, total - 1);
        int cols = Sprite.FrameGrid.Columns(_document.Width);

        if (_previewScopeAll)
        {
            _previewRangeStart = 0;
            _previewRangeEnd = total - 1;
        }
        else
        {
            int row = start / cols;
            _previewRangeStart = row * cols;
            _previewRangeEnd = Math.Min(total - 1, row * cols + cols - 1);
        }

        if (start < _previewRangeStart || start > _previewRangeEnd) start = _previewRangeStart;
        _previewFrame = start;
        _selectedFrame = start;
        Sprite.SelectedFrame = start;
        UpdateFrameStatus(start);
        UpdatePreviewFrame(start);

        _previewTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / ParsePreviewFps());
        _previewTimer.Start();
        _previewPlaying = true;
        UpdatePreviewPlayState();
    }

    private void StopPreviewPlayback()
    {
        if (!_previewPlaying) return;
        _previewTimer.Stop();
        _previewPlaying = false;
        UpdatePreviewPlayState();
    }

    private void AdvancePreviewFrame()
    {
        if (!_previewPlaying || _document == null) return;
        int count = _previewRangeEnd - _previewRangeStart + 1;
        if (count <= 0)
        {
            StopPreviewPlayback();
            return;
        }

        int next = _previewFrame + 1;
        if (next > _previewRangeEnd) next = _previewRangeStart;
        _previewFrame = next;
        _selectedFrame = next;
        Sprite.SelectedFrame = next;
        UpdateFrameStatus(next);
        UpdatePreviewFrame(next);
        UpdateInfoPanel();
    }

    private void UpdatePreviewPlayState()
    {
        if (PreviewPlayIcon == null || PreviewPlayText == null || PreviewPlayButton == null) return;
        PreviewPlayIcon.Geometry = FindGeometry(_previewPlaying ? "Ico.Pause" : "Ico.Play");
        PreviewPlayIcon.Filled = true;
        PreviewPlayText.Text = _previewPlaying ? "暂停" : "播放";
        PreviewPlayButton.IsEnabled = _document != null && Sprite.FrameGrid.IsActive;
    }

    private void PreviewFps_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_isReady || !_previewPlaying) return;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / ParsePreviewFps());
    }

    private int ParsePreviewFps()
    {
        int fps = int.TryParse(PreviewFpsBox.Text, out int value) ? value : 10;
        return Math.Clamp(fps, 1, 60);
    }

    private void PreviewScope_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        _previewScopeAll = (sender as FrameworkElement)?.Tag as string == "all";
        if (_previewPlaying)
        {
            StopPreviewPlayback();
            StartPreviewPlayback();
        }
    }

    // =====================================================================
    // 幻灯片
    // =====================================================================

    private void Slideshow_Click(object sender, RoutedEventArgs e) => StartSlideshow();

    private void StartSlideshow()
    {
        if (_document == null || _files.Count == 0)
        {
            ShowToast("先打开图片再播放幻灯片。", ToastKind.Warn);
            return;
        }

        if (_cropMode) ExitCropMode(apply: false);
        StopPreviewPlayback();

        _slideshowActive = true;
        _slideshowPaused = false;
        if (!_isFullscreen)
        {
            EnterImmersiveMode();
            _slideshowImmersive = true;
        }
        SlideshowLayer.Visibility = Visibility.Visible;
        SlideImageA.BeginAnimation(OpacityProperty, null);
        SlideImageB.BeginAnimation(OpacityProperty, null);
        SlideImageA.Source = null;
        SlideImageB.Source = null;
        SlideImageA.Opacity = 1;
        SlideImageB.Opacity = 0;
        var scaling = _settings.PixelSharp ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        RenderOptions.SetBitmapScalingMode(SlideImageA, scaling);
        RenderOptions.SetBitmapScalingMode(SlideImageB, scaling);
        SlideLoading.Visibility = Visibility.Visible;
        SlideIntervalText.Text = $"{Math.Max(1, _settings.SlideshowInterval / 1000)} 秒";
        _slideIndex = Math.Max(0, FilmList.SelectedIndex);
        UpdateSlideCounter();
        UpdateSlidePlayState();
        ShowSlideControls();
        _ = ShowSlideAsync(_slideIndex, first: true);
        RestartSlideshowTimer();
    }

    private void ExitSlideshow()
    {
        if (!_slideshowActive) return;

        _slideshowActive = false;
        _slideshowPaused = false;
        _slideshowTimer.Stop();
        _slideIdleTimer.Stop();
        _slideCts?.Cancel();
        SlideshowLayer.Visibility = Visibility.Collapsed;
        if (_slideshowImmersive)
        {
            ExitImmersiveMode();
            _slideshowImmersive = false;
        }
        SlideImageA.BeginAnimation(OpacityProperty, null);
        SlideImageB.BeginAnimation(OpacityProperty, null);
        SlideImageA.Source = null;
        SlideImageB.Source = null;
        SlideImageA.Opacity = 1;
        SlideImageB.Opacity = 0;
        SlideLoading.Visibility = Visibility.Collapsed;
        Focus();
    }

    private async Task ShowSlideAsync(int index, bool first)
    {
        if (!_slideshowActive || index < 0 || index >= _files.Count) return;

        _slideCts?.Cancel();
        var cts = new CancellationTokenSource();
        _slideCts = cts;
        SlideLoading.Visibility = Visibility.Visible;

        try
        {
            BitmapSource? bitmap;
            if (index == FilmList.SelectedIndex && _document != null)
            {
                bitmap = _document.Display;
            }
            else
            {
                bitmap = (await ImageLoader.LoadAsync(_files[index].Path, cts.Token)).Bitmap;
            }

            if (cts.IsCancellationRequested || !_slideshowActive) return;

            if (first)
            {
                SlideImageA.Source = bitmap;
                SlideImageA.Opacity = 1;
                SlideImageB.Opacity = 0;
            }
            else
            {
                var target = _slideFrontA ? SlideImageB : SlideImageA;
                var current = _slideFrontA ? SlideImageA : SlideImageB;
                target.Source = bitmap;
                target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
                current.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320)));
                _slideFrontA = !_slideFrontA;
            }

            SlideCounter.Text = $"{index + 1} / {_files.Count}";
            SlideLoading.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // 切换到其他幻灯片。
        }
        catch (Exception ex)
        {
            SlideLoading.Visibility = Visibility.Collapsed;
            ShowToast("无法加载幻灯片：" + ex.Message, ToastKind.Error);
        }
    }

    private void NextSlide()
    {
        if (!_slideshowActive || _files.Count == 0) return;
        _slideIndex = (_slideIndex + 1) % _files.Count;
        UpdateSlideCounter();
        _ = ShowSlideAsync(_slideIndex, first: false);
        if (!_slideshowPaused) RestartSlideshowTimer();
    }

    private void PrevSlide()
    {
        if (!_slideshowActive || _files.Count == 0) return;
        _slideIndex = (_slideIndex - 1 + _files.Count) % _files.Count;
        UpdateSlideCounter();
        _ = ShowSlideAsync(_slideIndex, first: false);
        if (!_slideshowPaused) RestartSlideshowTimer();
    }

    private void RestartSlideshowTimer()
    {
        _slideshowTimer.Stop();
        _slideshowTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, _settings.SlideshowInterval));
        _slideshowTimer.Start();
    }

    private void UpdateSlideCounter()
    {
        SlideCounter.Text = _files.Count == 0 ? "0 / 0" : $"{_slideIndex + 1} / {_files.Count}";
    }

    private void UpdateSlidePlayState()
    {
        SlidePlayIcon.Geometry = FindGeometry(_slideshowPaused ? "Ico.Play" : "Ico.Pause");
        SlidePlayIcon.Filled = true;
    }

    private void SlidePrev_Click(object sender, RoutedEventArgs e) => PrevSlide();

    private void SlideNext_Click(object sender, RoutedEventArgs e) => NextSlide();

    private void SlidePlay_Click(object sender, RoutedEventArgs e)
    {
        if (!_slideshowActive) return;
        _slideshowPaused = !_slideshowPaused;
        UpdateSlidePlayState();
        if (_slideshowPaused) _slideshowTimer.Stop();
        else RestartSlideshowTimer();
    }

    private void SlideExit_Click(object sender, RoutedEventArgs e) => ExitSlideshow();

    private void SlideInterval_Click(object sender, RoutedEventArgs e)
    {
        var items = new[]
        {
            MenuItemModel.Item("3 秒", () => SetSlideshowInterval(3000), isChecked: _settings.SlideshowInterval == 3000),
            MenuItemModel.Item("5 秒", () => SetSlideshowInterval(5000), isChecked: _settings.SlideshowInterval == 5000),
            MenuItemModel.Item("10 秒", () => SetSlideshowInterval(10000), isChecked: _settings.SlideshowInterval == 10000)
        };
        PopupMenu.Show(SlideIntervalButton, items, above: true);
    }

    private void SetSlideshowInterval(int milliseconds)
    {
        _settings.SlideshowInterval = milliseconds;
        SlideIntervalText.Text = $"{milliseconds / 1000} 秒";
        if (_slideshowActive && !_slideshowPaused) RestartSlideshowTimer();
        QueueSave();
    }

    private void Slideshow_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_slideshowActive) return;
        ShowSlideControls();
    }

    private void ShowSlideControls()
    {
        SlideControls.IsHitTestVisible = true;
        SlideControls.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        _slideIdleTimer.Stop();
        _slideIdleTimer.Start();
    }

    private void HideSlideControls()
    {
        SlideControls.IsHitTestVisible = false;
        SlideControls.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)));
    }

    // =====================================================================
    // 导出 / 打印 / 系统集成
    // =====================================================================

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null)
        {
            ShowToast("先打开一张图片。", ToastKind.Warn);
            return;
        }

        int? frame = _selectedFrame ?? Sprite.HoveredFrame;
        var items = new List<MenuItemModel>
        {
            MenuItemModel.Item("导出选中帧 PNG", ExportSelectedFrame, FindGeometry("Ico.Crop"), isEnabled: frame != null),
            MenuItemModel.Item("导出当前图像 PNG", ExportImage, FindGeometry("Ico.Image")),
            MenuItemModel.Item("导出带网格标记 PNG", ExportImageWithGrid, FindGeometry("Ico.Grid"), isEnabled: Sprite.FrameGrid.IsActive),
            MenuItemModel.SeparatorItem(),
            MenuItemModel.Item("复制图像到剪贴板", CopyImageToClipboard, FindGeometry("Ico.Copy")),
            MenuItemModel.Item("复制光标色值", CopyCurrentColor, FindGeometry("Ico.Eyedropper"), isEnabled: _lastColor != null)
        };

        PopupMenu.Show(FloatBar, items, above: true);
    }

    private string SuggestedFolder => _currentItem != null
        ? Path.GetDirectoryName(_currentItem.Path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    private string BaseName => _currentItem != null
        ? Path.GetFileNameWithoutExtension(_currentItem.Name)
        : "sprite";

    private SaveFileDialog CreatePngDialog(string suggestedName) => new()
    {
        Title = "导出 PNG",
        Filter = "PNG 图像|*.png",
        DefaultExt = ".png",
        AddExtension = true,
        FileName = suggestedName,
        InitialDirectory = SuggestedFolder
    };

    private void ExportSelectedFrame()
    {
        if (_document == null || !Sprite.FrameGrid.IsActive) return;
        int? frame = _selectedFrame ?? Sprite.HoveredFrame;
        if (frame is not int index)
        {
            ShowToast("先在画面上点选一帧。", ToastKind.Warn);
            return;
        }

        var rect = Sprite.FrameGrid.GetFrameRect(index, _document.Width, _document.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var dialog = CreatePngDialog($"{BaseName}_frame{index + 1}.png");
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImageExport.SaveFrame(_document.Display, rect, dialog.FileName);
            ShowToast($"已导出第 {index + 1} 帧。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("导出失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void ExportImage()
    {
        if (_document == null) return;
        var dialog = CreatePngDialog($"{BaseName}_export.png");
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImageLoader.SavePng(_document.Display, dialog.FileName);
            ShowToast("已导出当前图像。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("导出失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void ExportImageWithGrid()
    {
        if (_document == null || !Sprite.FrameGrid.IsActive) return;
        var dialog = CreatePngDialog($"{BaseName}_grid.png");
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImageExport.SaveWithGrid(_document.Display, Sprite.FrameGrid, dialog.FileName);
            ShowToast("已导出带网格标记的图像。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("导出失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void CopyImageToClipboard()
    {
        if (_document == null) return;
        try
        {
            Clipboard.SetImage(_document.Display);
            ShowToast("已复制图像到剪贴板。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("复制失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null)
        {
            ShowToast("先打开一张图片。", ToastKind.Warn);
            return;
        }

        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            var visual = ImageExport.CreatePrintVisual(
                _document.Display,
                Sprite.FrameGrid.IsActive ? Sprite.FrameGrid : null,
                _currentItem?.Name ?? "精灵镜",
                dialog.PrintableAreaWidth,
                dialog.PrintableAreaHeight);
            dialog.PrintVisual(visual, "精灵镜 - " + (_currentItem?.Name ?? "图片"));
            ShowToast("已发送到打印机。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("打印失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void ShowCurrentInExplorer()
    {
        if (_currentItem == null) return;
        NativeMethods.ShowInExplorer(_currentItem.Path);
    }

    private void SetCurrentAsWallpaper()
    {
        if (_document == null || _currentItem == null) return;
        try
        {
            string path = Path.Combine(SettingsStore.Folder, "wallpaper.png");
            Directory.CreateDirectory(SettingsStore.Folder);
            ImageLoader.SavePng(_document.Display, path);
            bool applied = NativeMethods.SetWallpaper(path);
            ShowToast(applied ? "已设为桌面背景（使用当前预览效果）。" : "设置桌面背景失败。",
                applied ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            ShowToast("设置壁纸失败：" + ex.Message, ToastKind.Error);
        }
    }

    private async void RenameCurrentFile()
    {
        if (_currentItem == null) return;

        string oldPath = _currentItem.Path;
        string oldName = Path.GetFileName(oldPath);
        string? input = await PromptAsync("重命名", "输入新的文件名（可省略扩展名）", oldName);
        if (string.IsNullOrWhiteSpace(input)) return;

        string newName = input.Trim();
        if (!Path.HasExtension(newName)) newName += Path.GetExtension(oldPath);
        if (newName == oldName) return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowToast("文件名包含无效字符。", ToastKind.Warn);
            return;
        }

        string? directory = Path.GetDirectoryName(oldPath);
        if (directory == null) return;
        string newPath = Path.Combine(directory, newName);
        if (File.Exists(newPath))
        {
            ShowToast("同名文件已存在。", ToastKind.Warn);
            return;
        }

        try
        {
            File.Move(oldPath, newPath);
            _settings.FrameMemory.Remove(oldPath);
            _currentItem.SetPath(newPath);
            await LoadCurrentAsync();
            UpdateInfoPanel();
            ShowToast("已重命名。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("重命名失败：" + ex.Message, ToastKind.Error);
        }
    }
}






