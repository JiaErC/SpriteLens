using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SpriteLens.Controls;
using SpriteLens.Core;
using SpriteLens.UI;

namespace SpriteLens;

public partial class MainWindow : Window
{
    private static readonly Regex FrameNamePattern = new(
        @"(?<!\d)(\d{1,4})\s*[x×]\s*(\d{1,4})(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex DigitsOnly = new(@"^\d+$", RegexOptions.Compiled);

    private readonly AppSettings _settings;
    private readonly ObservableCollection<ImageFileItem> _files = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _loadingTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly SolidColorBrush _swatchBrush = new(Colors.Transparent);

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _thumbCts;
    private ImageDocument? _document;
    private ImageFileItem? _currentItem;
    private int _loadRequest;
    private bool _isReady;
    private bool _suppressFrameBoxes;
    private bool _suppressFilmSelection;
    private bool _isFullscreen;
    private bool _isClosing;
    private int? _selectedFrame;
    private Color? _lastColor;
    private int _lastPixelX;
    private int _lastPixelY;

    private readonly DispatcherTimer _adjustTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _previewTimer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _slideshowTimer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _slideIdleTimer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(2.5) };

    private readonly Dictionary<string, TextBlock> _info = new();
    private bool _infoOpen;
    private bool _cropMode;
    private bool _adjustOpen;
    private bool _previewOpen;
    private bool _previewPlaying;
    private bool _previewScopeAll;
    private int _previewFrame;
    private int _previewRangeStart;
    private int _previewRangeEnd;
    private bool _slideshowActive;
    private bool _slideshowPaused;
    private bool _modalOpen;
    private bool _suppressAdjust;
    private bool _suppressCropRatio;
    private bool _suppressSharpToggle;
    private int _slideIndex;
    private bool _slideFrontA = true;
    private CancellationTokenSource? _slideCts;
    private TaskCompletionSource<bool>? _confirmTcs;
    private TaskCompletionSource<string?>? _inputTcs;
    private TextBlock? _infoPixelHex;
    private TextBlock? _infoPixelRgba;
    private Border? _infoPixelSwatch;
    private Button? _infoCopyPixelButton;
    private Button? _infoFrameExportButton;
    private Button? _infoResetButton;
    private Button? _infoBgButton;
    private Button? _infoExplorerButton;
    private Button? _infoWallpaperButton;
    private Button? _infoRenameButton;
    private Button? _infoDeleteButton;
    private ToggleButton? _infoSharpToggle;
    private TextBlock? _infoBgText;

    private enum ToastKind
    {
        Info,
        Success,
        Warn,
        Error
    }

    public MainWindow(AppSettings settings, string[] args)
    {
        _settings = settings;

        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _frameTimer.Tick += (_, _) =>
        {
            _frameTimer.Stop();
            ApplyFrameInputs();
        };

        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _loadingTimer.Tick += (_, _) =>
        {
            _loadingTimer.Stop();
            LoadingPill.Visibility = Visibility.Visible;
        };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save(_settings);
        };

        _adjustTimer.Tick += (_, _) =>
        {
            _adjustTimer.Stop();
            ApplyAdjustmentsFromSliders();
        };
        _previewTimer.Tick += (_, _) => AdvancePreviewFrame();
        _slideshowTimer.Tick += (_, _) =>
        {
            if (!_slideshowPaused) NextSlide();
        };
        _slideIdleTimer.Tick += (_, _) =>
        {
            _slideIdleTimer.Stop();
            HideSlideControls();
        };

        InitializeComponent();
        BuildInfoPanel();

        FilmList.ItemsSource = _files;
        Sprite.ApplyTheme();
        Sprite.FrameGrid.Style = ParseGridStyle(_settings.GridStyle);
        Sprite.FrameGrid.Thickness = Math.Clamp(_settings.GridThickness, 1, 3);
        Sprite.FrameGrid.Labels = _settings.GridLabels;
        Sprite.FrameGrid.Highlight = _settings.GridHighlight;
        Sprite.FrameGrid.Enabled = _settings.GridEnabled;
        Sprite.BackgroundMode = ParseBackground(_settings.Background);
        Sprite.PixelSharp = _settings.PixelSharp;
        GridToggle.IsChecked = _settings.GridEnabled;

        FrameWidthBox.IsEnabled = false;
        FrameHeightBox.IsEnabled = false;
        PresetButton.IsEnabled = false;

        ApplyWindowSettings();
        UpdateThemeButton();
        UpdateFrameHints();
        UpdateFrameBadge();
        UpdateStatus();
        UpdateZoomLabel();
        SetEmptyState();

        ThemeManager.ThemeChanged += OnThemeChanged;
        Loaded += MainWindow_Loaded;
        _isReady = true;

        if (args.Length > 0)
        {
            Dispatcher.BeginInvoke(new Action(() => OpenStartupArguments(args)), DispatcherPriority.ApplicationIdle);
        }
    }


    // =====================================================================
    // 启动 / 设置
    // =====================================================================

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowOnScreen();
        Sprite.Focus();
    }

    private void ApplyWindowSettings()
    {
        var workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;

        if (_settings.WindowWidth >= MinWidth) Width = Math.Min(_settings.WindowWidth, MaxWidth);
        if (_settings.WindowHeight >= MinHeight) Height = Math.Min(_settings.WindowHeight, MaxHeight);

        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }

        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    private void EnsureWindowOnScreen()
    {
        var work = SystemParameters.WorkArea;
        bool off = Left + 120 < work.Left || Left > work.Right - 240 ||
                   Top + 80 < work.Top || Top > work.Bottom - 160;
        if (!off) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
    }

    private void OpenStartupArguments(string[] args)
    {
        var paths = args
            .Where(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a =>
            {
                try
                {
                    return Path.GetFullPath(a);
                }
                catch
                {
                    return a;
                }
            })
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToList();

        if (paths.Count == 0) return;
        OpenPaths(paths);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.ApplyRoundedCorners(this);
        UpdateMaximizeIcon();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _loadCts?.Cancel();
        _thumbCts?.Cancel();
        _slideCts?.Cancel();
        _adjustTimer.Stop();
        _previewTimer.Stop();
        _slideshowTimer.Stop();
        _slideIdleTimer.Stop();
        _confirmTcs?.TrySetResult(false);
        _inputTcs?.TrySetResult(null);
        SaveWindowSettings();
        SettingsStore.Save(_settings);
    }

    private void SaveWindowSettings()
    {
        var state = _immersiveSaved ? _savedWindowState : WindowState;
        var bounds = _immersiveSaved
            ? _savedWindowBounds
            : WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

        if (bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settings.Maximized = state == WindowState.Maximized;
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        if (MaxIcon == null) return;
        MaxIcon.Geometry = FindGeometry(WindowState == WindowState.Maximized ? "Ico.WindowRestore" : "Ico.WindowMax");
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = ActualWidth;
        BrandSub.Visibility = w >= 1260 ? Visibility.Visible : Visibility.Collapsed;
        FileChip.Visibility = w >= 1150 ? Visibility.Visible : Visibility.Collapsed;
        FrameBadge.Visibility = w >= 1110 ? Visibility.Visible : Visibility.Collapsed;
        StatusHint.Visibility = _document != null && w >= 1180 ? Visibility.Visible : Visibility.Collapsed;
    }

    // =====================================================================
    // 顶栏 / 窗口
    // =====================================================================

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (HasInteractiveAncestor(e.OriginalSource as DependencyObject)) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 拖拽期间窗口状态改变，忽略。
        }
    }

    private static bool HasInteractiveAncestor(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase or TextBoxBase or ScrollBar or ListBoxItem) return true;
            source = source switch
            {
                ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
                Visual => VisualTreeHelper.GetParent(source),
                _ => null
            };
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        PopupMenu.Show(OpenButton, new[]
        {
            MenuItemModel.Item("打开图片…", OpenImageDialog, FindGeometry("Ico.Image"), "Ctrl O"),
            MenuItemModel.Item("打开文件夹…", OpenFolderDialog, FindGeometry("Ico.Folder"), "Ctrl ⇧ O"),
            MenuItemModel.SeparatorItem(),
            MenuItemModel.Item("从剪贴板粘贴", PasteFromClipboard, FindGeometry("Ico.Clipboard"), "Ctrl V"),
            MenuItemModel.Item("载入示例精灵图", LoadSamples, FindGeometry("Ico.Sparkles"))
        });
    }

    private void FileChip_Click(object sender, RoutedEventArgs e) => ShowInfoPanel();

    private void EmptyOpen_Click(object sender, RoutedEventArgs e) => OpenImageDialog();

    private void EmptyFolder_Click(object sender, RoutedEventArgs e) => OpenFolderDialog();

    private void EmptySamples_Click(object sender, RoutedEventArgs e) => LoadSamples();

    // =====================================================================
    // 打开 / 加载
    // =====================================================================

    private void OpenImageDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开精灵图",
            Multiselect = true,
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.jfif|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenPaths(dialog.FileNames);
        }
    }

    private void OpenFolderDialog()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "打开图片文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            OpenPaths(new[] { dialog.FolderName });
        }
    }

    private void OpenPaths(IEnumerable<string> paths, string? selectPath = null)
    {
        var collected = new List<string>();

        foreach (string raw in paths)
        {
            try
            {
                if (Directory.Exists(raw))
                {
                    collected.AddRange(Directory.EnumerateFiles(raw)
                        .Where(ImageLoader.IsSupported));
                }
                else if (File.Exists(raw) && ImageLoader.IsSupported(raw))
                {
                    collected.Add(Path.GetFullPath(raw));
                }
            }
            catch (UnauthorizedAccessException)
            {
                ShowToast("没有权限读取该文件夹。", ToastKind.Warn);
            }
            catch (IOException ex)
            {
                ShowToast(ex.Message, ToastKind.Error);
            }
        }

        collected = collected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => Path.GetFileName(p), Comparer<string>.Create(NativeMethods.NaturalCompare))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (collected.Count == 0)
        {
            ShowToast("没有找到可显示的图片。", ToastKind.Warn);
            return;
        }

        _loadCts?.Cancel();
        _thumbCts?.Cancel();
        _document = null;
        _currentItem = null;
        _selectedFrame = null;
        Sprite.SetImage(null);
        Sprite.SelectedFrame = null;

        _suppressFilmSelection = true;
        _files.Clear();
        for (int i = 0; i < collected.Count; i++)
        {
            _files.Add(new ImageFileItem(collected[i]) { Number = i + 1 });
        }

        _suppressFilmSelection = false;

        int index = 0;
        if (!string.IsNullOrEmpty(selectPath))
        {
            int found = _files.ToList().FindIndex(f => string.Equals(f.Path, selectPath, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) index = found;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        FloatBar.Visibility = Visibility.Collapsed;
        FilmCount.Text = $"1 / {_files.Count}";
        UpdateFilmstripVisibility();
        SelectFile(index);
        _ = LoadThumbnailsAsync();
    }

    private void SelectFile(int index)
    {
        if (index < 0 || index >= _files.Count) return;

        _suppressFilmSelection = true;
        FilmList.SelectedIndex = index;
        _suppressFilmSelection = false;
        FilmList.ScrollIntoView(FilmList.SelectedItem);
        _ = LoadCurrentAsync();
    }

    private void FilmList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || _suppressFilmSelection) return;
        if (FilmList.SelectedItem is ImageFileItem)
        {
            _ = LoadCurrentAsync();
        }
    }

    private async Task LoadCurrentAsync()
    {
        if (FilmList.SelectedItem is not ImageFileItem item) return;

        _currentItem = item;
        int request = ++_loadRequest;
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        BeginLoading();

        try
        {
            LoadedImage loaded = await ImageLoader.LoadAsync(item.Path, cts.Token);
            if (request != _loadRequest || cts.IsCancellationRequested || _isClosing) return;

            _document = new ImageDocument(item.Path, loaded.Bitmap);
            ResetTransientToolsForNewImage();
            ResetAdjustForNewImage();
            _selectedFrame = null;
            Sprite.SelectedFrame = null;
            Sprite.FrameGrid.Enabled = GridToggle.IsChecked == true;
            Sprite.BackgroundMode = ParseBackground(_settings.Background);
            Sprite.PixelSharp = _settings.PixelSharp;
            RestoreFrameSettings(item);

            Sprite.SetImage(_document.Display);
            EmptyState.Visibility = Visibility.Collapsed;
            FloatBar.Visibility = Visibility.Visible;
            FrameWidthBox.IsEnabled = true;
            FrameHeightBox.IsEnabled = true;
            PresetButton.IsEnabled = true;

            FileNameText.Text = item.Name;
            FileMetaText.Text = $"{loaded.Width} × {loaded.Height} · {ImageLoader.GetFormatName(item.Path)}";
            Title = $"{item.Name} — 精灵镜";
            FilmCount.Text = $"{FilmList.SelectedIndex + 1} / {_files.Count}";
            item.RefreshFileInfo();

            UpdateFrameBadge();
            UpdateFrameHints();
            UpdateStatus();
            UpdateZoomLabel();
            UpdateFrameStatus(null);
            UpdateInfoPanel();
            UpdatePreviewFrame();
            UpdateAdjustButtonState();
            EndLoading();
        }
        catch (OperationCanceledException)
        {
            // 打开了另一张图片，忽略。
        }
        catch (Exception ex)
        {
            if (request != _loadRequest) return;
            EndLoading();
            _document = null;
            Sprite.SetImage(null);
            _selectedFrame = null;
            Sprite.SelectedFrame = null;
            EmptyState.Visibility = Visibility.Visible;
            FloatBar.Visibility = Visibility.Collapsed;
            FrameWidthBox.IsEnabled = false;
            FrameHeightBox.IsEnabled = false;
            PresetButton.IsEnabled = false;
            FileNameText.Text = item.Name;
            FileMetaText.Text = "无法打开";
            Title = "精灵镜 SpriteLens";
            _cropMode = false;
            CropBar.Visibility = Visibility.Collapsed;
            AdjustCard.Visibility = Visibility.Collapsed;
            _adjustOpen = false;
            PreviewCard.Visibility = Visibility.Collapsed;
            _previewOpen = false;
            StopPreviewPlayback();
            UpdateFrameBadge();
            UpdateStatus();
            UpdateInfoPanel();
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        _thumbCts?.Cancel();
        var cts = new CancellationTokenSource();
        _thumbCts = cts;
        var token = cts.Token;
        var items = _files.Take(1000).ToList();
        using var gate = new SemaphoreSlim(3);
        var tasks = new List<Task>();

        foreach (var item in items)
        {
            if (item.Thumbnail != null) continue;
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var thumb = await ImageLoader.LoadThumbnailAsync(item.Path, 96, token).ConfigureAwait(false);
                    if (thumb == null || token.IsCancellationRequested) return;
                    await Dispatcher.InvokeAsync(() => item.Thumbnail = thumb);
                }
                catch (OperationCanceledException)
                {
                    // 列表已切换。
                }
                finally
                {
                    gate.Release();
                }
            }, token));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // 忽略。
        }
        catch
        {
            // 缩略图失败不影响主流程。
        }
    }

    private void BeginLoading()
    {
        LoadingPill.Visibility = Visibility.Collapsed;
        _loadingTimer.Stop();
        _loadingTimer.Start();
    }

    private void EndLoading()
    {
        _loadingTimer.Stop();
        LoadingPill.Visibility = Visibility.Collapsed;
    }

    private void UpdateFilmstripVisibility()
    {
        bool show = _files.Count > 1 && !_isFullscreen;
        FilmstripHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FilmCount.Text = _files.Count == 0 ? string.Empty : FilmCount.Text;
    }

    private void RestoreFrameSettings(ImageFileItem item)
    {
        if (_document == null) return;

        int width = _document.Width;
        int height = _document.Height;
        int frameWidth = 0;
        int frameHeight = 0;

        if (_settings.FrameMemory.TryGetValue(item.Path, out var memory) &&
            memory.ImageWidth == width && memory.ImageHeight == height &&
            memory.FrameWidth > 0 && memory.FrameHeight > 0)
        {
            frameWidth = memory.FrameWidth;
            frameHeight = memory.FrameHeight;
        }
        else if (TryFrameFromFileName(item.Name, out int w, out int h) &&
                 w > 0 && h > 0 && w <= width && h <= height &&
                 width % w == 0 && height % h == 0 &&
                 (long)(width / w) * (height / h) >= 2)
        {
            frameWidth = w;
            frameHeight = h;
        }

        SetFrameBoxes(frameWidth, frameHeight);
        ApplyFrameInputs(persist: false);
        PersistFrameMemory();
    }

    private static bool TryFrameFromFileName(string name, out int width, out int height)
    {
        width = 0;
        height = 0;
        string baseName = Path.GetFileNameWithoutExtension(name);
        var match = FrameNamePattern.Match(baseName);
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, out width)) return false;
        if (!int.TryParse(match.Groups[2].Value, out height)) return false;
        return width > 0 && height > 0;
    }

    // =====================================================================
    // 帧尺寸
    // =====================================================================

    private void FrameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isReady) return;
        UpdateFrameHints();
        if (_suppressFrameBoxes) return;
        _frameTimer.Stop();
        _frameTimer.Start();
    }

    private void UpdateFrameHints()
    {
        FrameWidthHint.Visibility = string.IsNullOrEmpty(FrameWidthBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        FrameHeightHint.Visibility = string.IsNullOrEmpty(FrameHeightBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void NumberBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(typeof(string)) as string;
        if (string.IsNullOrEmpty(text) || !DigitsOnly.IsMatch(text))
        {
            e.CancelCommand();
        }
    }

    private void FrameBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
        }
    }

    private void FrameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyFrameInputs();
        if (sender is TextBox box)
        {
            box.SelectAll();
        }

        e.Handled = true;
    }

    private void SetFrameBoxes(int width, int height)
    {
        _suppressFrameBoxes = true;
        FrameWidthBox.Text = width > 0 ? width.ToString() : string.Empty;
        FrameHeightBox.Text = height > 0 ? height.ToString() : string.Empty;
        _suppressFrameBoxes = false;
        UpdateFrameHints();
    }

    private static int ParseDimension(string text) =>
        int.TryParse(text, out int value) ? Math.Clamp(value, 0, 16384) : 0;

    private void ApplyFrameInputs(bool persist = true)
    {
        if (!_isReady) return;

        int frameWidth = ParseDimension(FrameWidthBox.Text);
        int frameHeight = ParseDimension(FrameHeightBox.Text);
        Sprite.FrameGrid.FrameWidth = frameWidth;
        Sprite.FrameGrid.FrameHeight = frameHeight;

        if (_selectedFrame is int selected && _document != null)
        {
            int total = Sprite.FrameGrid.IsActive
                ? Sprite.FrameGrid.FrameCount(_document.Width, _document.Height)
                : 0;
            if (selected >= total)
            {
                _selectedFrame = null;
                Sprite.SelectedFrame = null;
            }
        }

        Sprite.RefreshGrid();
        UpdateFrameBadge();
        UpdateStatus();
        UpdateFrameStatus(_selectedFrame);
        UpdatePreviewFrame();
        UpdateInfoPanel();
        if (persist) PersistFrameMemory();
    }

    private void PersistFrameMemory()
    {
        if (_document == null) return;
        string key = _document.Path;

        if (Sprite.FrameGrid.IsActive)
        {
            _settings.FrameMemory[key] = new FrameMemory
            {
                FrameWidth = Sprite.FrameGrid.FrameWidth,
                FrameHeight = Sprite.FrameGrid.FrameHeight,
                ImageWidth = _document.Width,
                ImageHeight = _document.Height
            };

            if (_settings.FrameMemory.Count > 600)
            {
                var keep = _settings.FrameMemory.Take(400).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                _settings.FrameMemory = keep;
            }
        }
        else
        {
            _settings.FrameMemory.Remove(key);
        }

        QueueSave();
    }

    private void GridToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        bool enabled = GridToggle.IsChecked == true;
        Sprite.FrameGrid.Enabled = enabled;
        _settings.GridEnabled = enabled;
        Sprite.RefreshGrid();
        UpdateFrameBadge();
        UpdateStatus();
        QueueSave();
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;

        int width = _document.Width;
        int height = _document.Height;
        var items = new List<MenuItemModel>
        {
            MenuItemModel.Header($"适配 {width} × {height}")
        };

        var candidates = GetFrameCandidates(width, height);
        foreach (int size in candidates.Take(8))
        {
            int cols = (width + size - 1) / size;
            int rows = (height + size - 1) / size;
            items.Add(MenuItemModel.Item(
                $"{size} × {size}",
                () =>
                {
                    SetFrameBoxes(size, size);
                    ApplyFrameInputs();
                },
                isChecked: FrameWidthBox.Text == size.ToString() && FrameHeightBox.Text == size.ToString(),
                shortcut: $"{cols} × {rows}"));
        }

        if (candidates.Count == 0)
        {
            items.Add(MenuItemModel.Item("没有可整除的方形尺寸", null, isEnabled: false));
        }

        items.Add(MenuItemModel.SeparatorItem());
        items.Add(MenuItemModel.Header("常用尺寸"));

        foreach (int size in new[] { 8, 16, 24, 32, 48, 64, 96, 128, 256 })
        {
            if (candidates.Contains(size) || size > Math.Max(width, height)) continue;
            items.Add(MenuItemModel.Item(
                $"{size} × {size}",
                () =>
                {
                    SetFrameBoxes(size, size);
                    ApplyFrameInputs();
                }));
        }

        items.Add(MenuItemModel.SeparatorItem());
        items.Add(MenuItemModel.Item("交换帧宽与帧高", SwapFrameSizes, FindGeometry("Ico.Swap")));
        items.Add(MenuItemModel.Item("清除帧尺寸", () =>
        {
            SetFrameBoxes(0, 0);
            ApplyFrameInputs();
        }, FindGeometry("Ico.X")));

        PopupMenu.Show(PresetButton, items);
    }

    private void SwapFrameSizes()
    {
        int width = ParseDimension(FrameWidthBox.Text);
        int height = ParseDimension(FrameHeightBox.Text);
        if (width <= 0 || height <= 0) return;
        SetFrameBoxes(height, width);
        ApplyFrameInputs();
    }

    private static List<int> GetFrameCandidates(int imageWidth, int imageHeight)
    {
        int gcd = Gcd(imageWidth, imageHeight);
        var result = new List<int>();
        int max = Math.Min(1024, gcd);
        for (int size = 4; size <= max; size++)
        {
            if (gcd % size != 0) continue;
            long frames = (long)(imageWidth / size) * (imageHeight / size);
            if (frames >= 2 && frames <= 4096) result.Add(size);
        }

        return result;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Abs(a);
    }

    private void UpdateFrameBadge()
    {
        if (!_isReady) return;

        if (_document == null)
        {
            SetBadge("未设置", "empty");
            return;
        }

        int frameWidth = ParseDimension(FrameWidthBox.Text);
        int frameHeight = ParseDimension(FrameHeightBox.Text);
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            SetBadge("输入帧尺寸", "empty");
            return;
        }

        if (frameWidth > _document.Width || frameHeight > _document.Height)
        {
            SetBadge("帧尺寸超出图像", "warn");
            return;
        }

        int cols = Sprite.FrameGrid.Columns(_document.Width);
        int rows = Sprite.FrameGrid.Rows(_document.Height);
        int total = cols * rows;
        int remainderX = _document.Width % frameWidth;
        int remainderY = _document.Height % frameHeight;
        bool warn = remainderX > 0 || remainderY > 0;

        string text = $"{cols} × {rows} · {total} 帧";
        if (warn)
        {
            text += remainderX > 0 && remainderY > 0
                ? $" · 余 {remainderX}×{remainderY}px"
                : $" · 余 {(remainderX > 0 ? remainderX : remainderY)}px";
        }

        SetBadge(text, warn ? "warn" : "normal");
    }

    private void SetBadge(string text, string kind)
    {
        FrameBadgeText.Text = text;
        FrameBadge.Background = (Brush)FindResource(kind switch
        {
            "warn" => "B.WarnSoft",
            "normal" => "B.AccentSoft",
            _ => "B.Hover"
        });
        FrameBadgeText.Foreground = (Brush)FindResource(kind switch
        {
            "warn" => "B.Warn",
            "normal" => "B.Accent",
            _ => "B.Text3"
        });
    }

    private void GridStyleButton_Click(object sender, RoutedEventArgs e)
    {
        var grid = Sprite.FrameGrid;
        var items = new List<MenuItemModel>
        {
            MenuItemModel.Header("线条样式"),
            MenuItemModel.Item("双色描边（推荐）", () => SetGridStyle(GridLineStyle.Dual),
                FindGeometry("Ico.Grid"), isChecked: grid.Style == GridLineStyle.Dual),
            MenuItemModel.Item("黑白虚线", () => SetGridStyle(GridLineStyle.Dash),
                isChecked: grid.Style == GridLineStyle.Dash),
            MenuItemModel.Item("荧光强调", () => SetGridStyle(GridLineStyle.Accent),
                isChecked: grid.Style == GridLineStyle.Accent),
            MenuItemModel.SeparatorItem(),
            MenuItemModel.Header("线条粗细"),
            MenuItemModel.Item("1 px", () => SetGridThickness(1), isChecked: grid.Thickness == 1),
            MenuItemModel.Item("2 px", () => SetGridThickness(2), isChecked: grid.Thickness == 2),
            MenuItemModel.Item("3 px", () => SetGridThickness(3), isChecked: grid.Thickness == 3),
            MenuItemModel.SeparatorItem(),
            MenuItemModel.Item("显示帧编号", () => SetGridLabels(!grid.Labels),
                isChecked: grid.Labels),
            MenuItemModel.Item("高亮当前帧", () => SetGridHighlight(!grid.Highlight),
                isChecked: grid.Highlight)
        };

        PopupMenu.Show(GridStyleButton, items);
    }

    private void SetGridStyle(GridLineStyle style)
    {
        Sprite.FrameGrid.Style = style;
        _settings.GridStyle = style switch
        {
            GridLineStyle.Dash => "dash",
            GridLineStyle.Accent => "accent",
            _ => "dual"
        };
        Sprite.RefreshGrid();
        QueueSave();
    }

    private void SetGridThickness(int thickness)
    {
        Sprite.FrameGrid.Thickness = Math.Clamp(thickness, 1, 3);
        _settings.GridThickness = Sprite.FrameGrid.Thickness;
        Sprite.RefreshGrid();
        QueueSave();
    }

    private void SetGridLabels(bool labels)
    {
        Sprite.FrameGrid.Labels = labels;
        _settings.GridLabels = labels;
        Sprite.RefreshGrid();
        QueueSave();
    }

    private void SetGridHighlight(bool highlight)
    {
        Sprite.FrameGrid.Highlight = highlight;
        _settings.GridHighlight = highlight;
        Sprite.RefreshGrid();
        QueueSave();
    }

    // =====================================================================
    // 视图 / 编辑
    // =====================================================================

    private void Sprite_ViewChanged(object? sender, EventArgs e)
    {
        UpdateZoomLabel();
        if (_infoOpen && _info.TryGetValue("Zoom", out var zoom)) zoom.Text = $"{Sprite.ZoomPercent}%";
    }

    private void UpdateZoomLabel()
    {
        if (ZoomText == null) return;
        string text = _document == null ? "—" : $"{Sprite.ZoomPercent}%";
        ZoomText.Content = text;
        StatusZoom.Text = text;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        Sprite.ZoomBy(1.25);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        Sprite.ZoomBy(1 / 1.25);
    }

    private void ZoomText_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;

        var items = new List<MenuItemModel>();
        foreach (int percent in new[] { 50, 75, 100, 150, 200, 400, 800 })
        {
            items.Add(MenuItemModel.Item($"{percent}%", () => SetZoomPercent(percent),
                isChecked: Math.Abs(Sprite.ZoomPercent - percent) <= 1));
        }

        items.Add(MenuItemModel.SeparatorItem());
        items.Add(MenuItemModel.Item("适应窗口", () => Sprite.FitToView(), FindGeometry("Ico.Fit"), "0"));
        items.Add(MenuItemModel.Item("填充窗口", () => Sprite.SetFill()));
        PopupMenu.Show(ZoomText, items, above: true);
    }

    private void SetZoomPercent(double percent)
    {
        if (_document == null) return;
        var center = new Point(Sprite.ActualWidth / 2, Sprite.ActualHeight / 2);
        Sprite.SetZoom(percent / 100.0, center);
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        Sprite.FitToView();
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        Sprite.ZoomToActualSize();
    }

    private void RotateCw_Click(object sender, RoutedEventArgs e) => ApplyEdit(new EditOp(EditKind.RotateCw));

    private void RotateCcw_Click(object sender, RoutedEventArgs e) => ApplyEdit(new EditOp(EditKind.RotateCcw));

    private void FlipH_Click(object sender, RoutedEventArgs e) => ApplyEdit(new EditOp(EditKind.FlipHorizontal));

    private void FlipV_Click(object sender, RoutedEventArgs e) => ApplyEdit(new EditOp(EditKind.FlipVertical));

    private void ApplyEdit(EditOp op)
    {
        if (_document == null) return;
        _document.ApplyEdit(op);
        if (op.Kind is EditKind.RotateCw or EditKind.RotateCcw)
        {
            int width = ParseDimension(FrameWidthBox.Text);
            int height = ParseDimension(FrameHeightBox.Text);
            if (width > 0 && height > 0)
            {
                SetFrameBoxes(height, width);
                ApplyFrameInputs();
            }
        }

        _selectedFrame = null;
        Sprite.SelectedFrame = null;
        Sprite.SetImage(_document.Display);
        UpdateFrameBadge();
        UpdateStatus();
        UpdateFrameStatus(null);
        UpdatePreviewFrame();
        UpdateInfoPanel();
        ShowToast("已应用编辑，可用「还原所有编辑」恢复。", ToastKind.Info);
    }

    private void Sprite_FrameClicked(object? sender, int? frame)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            CopyCurrentColor();
            return;
        }

        _selectedFrame = frame;
        Sprite.SelectedFrame = frame;
        UpdateFrameStatus(frame);
        UpdatePreviewFrame();
        UpdateInfoPanel();
    }

    private void Sprite_FrameActivated(object? sender, int? frame)
    {
        if (_document == null) return;

        if (frame is int index && Sprite.FrameGrid.IsActive)
        {
            _selectedFrame = index;
            Sprite.SelectedFrame = index;
            Sprite.ZoomToFrame(index);
            UpdateFrameStatus(index);
            UpdatePreviewFrame();
            UpdateInfoPanel();
            return;
        }

        if (Sprite.IsFitMode) Sprite.ZoomToActualSize();
        else Sprite.FitToView();
    }

    private void Sprite_PointerChanged(object? sender, PointerInfoEventArgs e)
    {
        if (_document == null || !e.Inside)
        {
            StatusCursor.Text = "—";
            StatusColor.Visibility = Visibility.Collapsed;
            _lastColor = null;
            if (_selectedFrame == null) UpdateFrameStatus(null);
            if (_infoOpen) UpdateInfoPanel();
            return;
        }

        StatusCursor.Text = $"x {e.PixelX} · y {e.PixelY}";
        var color = _document.GetPixel(e.PixelX, e.PixelY);
        if (color is Color value)
        {
            _lastColor = value;
            _lastPixelX = e.PixelX;
            _lastPixelY = e.PixelY;
            _swatchBrush.Color = value;
            StatusColorSwatch.Background = _swatchBrush;
            StatusColorText.Text = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            StatusColor.Visibility = Visibility.Visible;
        }
        else
        {
            _lastColor = null;
            StatusColor.Visibility = Visibility.Collapsed;
        }

        if (_selectedFrame == null) UpdateFrameStatus(e.FrameIndex);
        if (_infoOpen) UpdateInfoPanel();
    }

    private void UpdateFrameStatus(int? frame)
    {
        if (_document == null || frame is not int index || !Sprite.FrameGrid.IsActive)
        {
            StatusFrame.Visibility = Visibility.Collapsed;
            return;
        }

        int total = Sprite.FrameGrid.FrameCount(_document.Width, _document.Height);
        if (index < 0 || index >= total)
        {
            StatusFrame.Visibility = Visibility.Collapsed;
            return;
        }

        int cols = Sprite.FrameGrid.Columns(_document.Width);
        int row = index / cols;
        int col = index % cols;
        StatusFrame.Text = $"帧 {index + 1} / {total} · 列 {col + 1} 行 {row + 1}";
        StatusFrame.Visibility = Visibility.Visible;
    }

    private void CopyCurrentColor()
    {
        if (_lastColor is not Color color)
        {
            ShowToast("先把鼠标移到图片上再取色。", ToastKind.Warn);
            return;
        }

        string text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        try
        {
            Clipboard.SetText(text);
            ShowToast($"已复制色值 {text}（{_lastPixelX}, {_lastPixelY}）", ToastKind.Success);
        }
        catch
        {
            ShowToast("复制到剪贴板失败。", ToastKind.Error);
        }
    }

    // =====================================================================
    // 状态
    // =====================================================================

    private void UpdateStatus()
    {
        if (_document == null)
        {
            StatusDims.Text = "未打开图片";
            StatusZoom.Text = "—";
            StatusCursor.Text = "—";
            StatusFrame.Visibility = Visibility.Collapsed;
            StatusColor.Visibility = Visibility.Collapsed;
            StatusGrid.Text = string.Empty;
            StatusHint.Visibility = Visibility.Collapsed;
            return;
        }

        StatusHint.Visibility = ActualWidth >= 1180 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFileMeta();
        StatusDims.Text = $"{_document.Width} × {_document.Height} px";
        StatusZoom.Text = $"{Sprite.ZoomPercent}%";

        var grid = Sprite.FrameGrid;
        if (grid.IsActive)
        {
            int cols = grid.Columns(_document.Width);
            int rows = grid.Rows(_document.Height);
            int remainderX = grid.RemainderX(_document.Width);
            int remainderY = grid.RemainderY(_document.Height);
            string text = $"网格 {grid.FrameWidth} × {grid.FrameHeight} · {cols} × {rows} = {cols * rows} 帧";
            if (remainderX > 0 || remainderY > 0)
            {
                text += $" · 余 {remainderX}×{remainderY}px";
            }

            if (!grid.Enabled) text += " · 已隐藏";
            StatusGrid.Text = text;
        }
        else
        {
            StatusGrid.Text = "未设置帧尺寸";
        }
    }

    private void UpdateFileMeta()
    {
        if (_document == null) return;
        string edited = _document.HasEdits ? " · 已编辑" : string.Empty;
        FileMetaText.Text = $"{_document.Width} × {_document.Height} · {ImageLoader.GetFormatName(_document.Path)}{edited}";
    }

    private void SetEmptyState()
    {
        bool empty = _document == null;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        FloatBar.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    // =====================================================================
    // 文件操作
    // =====================================================================

    private void PasteFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                ShowToast("剪贴板里没有图片。", ToastKind.Warn);
                return;
            }

            var image = Clipboard.GetImage();
            if (image == null)
            {
                ShowToast("无法读取剪贴板图片。", ToastKind.Warn);
                return;
            }

            string folder = Path.Combine(SettingsStore.Folder, "Clipboard");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            ImageLoader.SavePng(image, path);
            OpenPaths(new[] { path });
            ShowToast("已从剪贴板打开图片。", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("粘贴失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void LoadSamples()
    {
        try
        {
            string folder = Path.Combine(SettingsStore.Folder, "Samples");
            Directory.CreateDirectory(folder);
            string[] names = { "slime_32x32.png", "crystal_64x64.png", "terrain_16x16.png" };
            var paths = new List<string>();

            foreach (string name in names)
            {
                string target = Path.Combine(folder, name);
                if (!File.Exists(target))
                {
                    var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/samples/{name}"));
                    if (resource?.Stream == null) continue;
                    using var stream = resource.Stream;
                    using var file = File.Create(target);
                    stream.CopyTo(file);
                }

                paths.Add(target);
            }

            if (paths.Count == 0)
            {
                ShowToast("示例文件不可用。", ToastKind.Error);
                return;
            }

            OpenPaths(paths, paths[0]);
        }
        catch (Exception ex)
        {
            ShowToast("载入示例失败：" + ex.Message, ToastKind.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteCurrentFile();

    private async void DeleteCurrentFile()
    {
        if (_currentItem == null) return;
        var item = _currentItem;

        bool confirmed = await ConfirmAsync(
            "删除图片",
            $"将「{item.Name}」移入回收站？\n\n如果系统回收站不可用，文件会被永久删除。",
            "删除",
            danger: true);

        if (!confirmed || _currentItem != item) return;

        if (_cropMode) ExitCropMode(apply: false);
        StopPreviewPlayback();

        int index = FilmList.SelectedIndex;
        bool recycled;
        try
        {
            _loadCts?.Cancel();
            recycled = NativeMethods.RecycleFile(item.Path);
            if (!recycled) File.Delete(item.Path);
        }
        catch (Exception ex)
        {
            ShowToast("删除失败：" + ex.Message, ToastKind.Error);
            return;
        }

        _suppressFilmSelection = true;
        _files.RemoveAt(index);
        _suppressFilmSelection = false;

        _document = null;
        _currentItem = null;
        Sprite.SetImage(null);
        Sprite.SelectedFrame = null;
        _selectedFrame = null;
        StopPreviewPlayback();

        for (int i = 0; i < _files.Count; i++) _files[i].Number = i + 1;

        if (_files.Count == 0)
        {
            FilmList.SelectedIndex = -1;
            FileNameText.Text = "未打开图片";
            FileMetaText.Text = "拖入图片或按 Ctrl + O";
            Title = "精灵镜 SpriteLens";
            FilmCount.Text = string.Empty;
            FrameWidthBox.Text = string.Empty;
            FrameHeightBox.Text = string.Empty;
            FrameWidthBox.IsEnabled = false;
            FrameHeightBox.IsEnabled = false;
            PresetButton.IsEnabled = false;
            UpdateFilmstripVisibility();
            SetEmptyState();
            UpdateFrameBadge();
            UpdateStatus();
            UpdateFrameStatus(null);
            UpdateInfoPanel();
            UpdatePreviewFrame();
            ShowToast(recycled ? "已移入回收站。" : "文件已删除。", ToastKind.Success);
            return;
        }

        UpdateFilmstripVisibility();
        SelectFile(Math.Min(index, _files.Count - 1));
        UpdateInfoPanel();
        ShowToast(recycled ? "已移入回收站。" : "文件已删除。", ToastKind.Success);
    }

    // =====================================================================
    // 主题 / 全屏
    // =====================================================================

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = ThemeManager.Toggle();
        ThemeManager.Apply(_settings.Theme);
        Sprite.ApplyTheme();
        UpdateThemeButton();
        UpdateFrameBadge();
        QueueSave();
    }

    private void UpdateThemeButton()
    {
        if (ThemeIcon == null) return;
        bool light = ThemeManager.Current == "light";
        ThemeIcon.Geometry = FindGeometry(light ? "Ico.Moon" : "Ico.Sun");
        ThemeButton.ToolTip = light ? "切换到深色主题（T）" : "切换到浅色主题（T）";
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!_isReady) return;
        Sprite.ApplyTheme();
        PreviewCanvas.ApplyTheme();
        UpdateThemeButton();
        UpdateFrameBadge();
        UpdateInfoPanel();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _isFullscreen = true;
            EnterImmersiveMode();
            TopBar.Visibility = Visibility.Collapsed;
            Shell.RowDefinitions[0].Height = new GridLength(0);
            FilmstripHost.Visibility = Visibility.Collapsed;
            Shell.RowDefinitions[2].Height = new GridLength(0);
            StatusBarHost.Visibility = Visibility.Collapsed;
            Shell.RowDefinitions[3].Height = new GridLength(0);
            FullscreenButton.ToolTip = "退出全屏（Esc）";
            FullscreenIcon.Geometry = FindGeometry("Ico.Collapse");
        }
        else
        {
            _isFullscreen = false;
            TopBar.Visibility = Visibility.Visible;
            Shell.RowDefinitions[0].Height = new GridLength(58);
            StatusBarHost.Visibility = Visibility.Visible;
            Shell.RowDefinitions[3].Height = new GridLength(30);
            Shell.RowDefinitions[2].Height = GridLength.Auto;
            UpdateFilmstripVisibility();
            ExitImmersiveMode();
            FullscreenButton.ToolTip = "全屏（F）";
            FullscreenIcon.Geometry = FindGeometry("Ico.Expand");
        }
    }

    // =====================================================================
    // 帮助
    // =====================================================================

    private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowHelp();

    // =====================================================================
    // 拖放
    // =====================================================================

    private void Stage_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Stage_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Stage_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Stage_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            OpenPaths(paths);
        }
    }

    // =====================================================================
    // 键盘
    // =====================================================================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool typing = Keyboard.FocusedElement is TextBoxBase;

        if (_modalOpen)
        {
            if (e.Key == Key.Escape)
            {
                CancelOpenModal();
                e.Handled = true;
            }

            return;
        }

        if (_slideshowActive)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ExitSlideshow();
                    break;
                case Key.Left:
                    PrevSlide();
                    break;
                case Key.Right:
                    NextSlide();
                    break;
                case Key.Space:
                    SlidePlay_Click(sender, e);
                    break;
            }

            e.Handled = true;
            return;
        }

        if (_cropMode)
        {
            if (e.Key == Key.Enter)
            {
                ExitCropMode(apply: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ExitCropMode(apply: false);
                e.Handled = true;
            }

            return;
        }

        if (ctrl && e.Key == Key.Z && !typing)
        {
            Undo_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.O)
        {
            if (shift) OpenFolderDialog();
            else OpenImageDialog();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S)
        {
            if (!typing) Export_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.P)
        {
            if (!typing) Print_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_isFullscreen) ToggleFullscreen();
            else
            {
                _selectedFrame = null;
                Sprite.SelectedFrame = null;
                UpdateFrameStatus(null);
                UpdateInfoPanel();
            }

            e.Handled = true;
            return;
        }

        if (typing) return;

        if (ctrl && e.Key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.G:
                GridToggle.IsChecked = GridToggle.IsChecked != true;
                e.Handled = true;
                break;
            case Key.T:
                ThemeButton_Click(sender, e);
                e.Handled = true;
                break;
            case Key.I:
                ShowInfoPanel();
                e.Handled = true;
                break;
            case Key.C:
                Crop_Click(sender, e);
                e.Handled = true;
                break;
            case Key.A:
                Adjust_Click(sender, e);
                e.Handled = true;
                break;
            case Key.P:
                Preview_Click(sender, e);
                e.Handled = true;
                break;
            case Key.S:
                Slideshow_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.R:
                if (shift) RotateCcw_Click(sender, e);
                else RotateCw_Click(sender, e);
                e.Handled = true;
                break;
            case Key.H:
                FlipH_Click(sender, e);
                e.Handled = true;
                break;
            case Key.V:
                FlipV_Click(sender, e);
                e.Handled = true;
                break;
            case Key.L:
                SetGridLabels(!Sprite.FrameGrid.Labels);
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteCurrentFile();
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                Sprite.ZoomBy(1.25);
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                Sprite.ZoomBy(1 / 1.25);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                Sprite.FitToView();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                Sprite.ZoomToActualSize();
                e.Handled = true;
                break;
            case Key.Left:
                if (FilmList.SelectedIndex > 0) SelectFile(FilmList.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Right:
                if (FilmList.SelectedIndex < _files.Count - 1) SelectFile(FilmList.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.OemOpenBrackets:
                SelectFrame(-1);
                e.Handled = true;
                break;
            case Key.OemCloseBrackets:
                SelectFrame(1);
                e.Handled = true;
                break;
            case Key.OemQuestion:
            case Key.F1:
                ShowHelp();
                e.Handled = true;
                break;
        }
    }

    private void SelectFrame(int delta)
    {
        if (_document == null || !Sprite.FrameGrid.IsActive) return;
        int total = Sprite.FrameGrid.FrameCount(_document.Width, _document.Height);
        if (total <= 0) return;
        int current = _selectedFrame ?? (delta > 0 ? -1 : total);
        int next = Math.Clamp(current + delta, 0, total - 1);
        _selectedFrame = next;
        Sprite.SelectedFrame = next;
        UpdateFrameStatus(next);
        UpdatePreviewFrame();
        UpdateInfoPanel();
    }

    // =====================================================================
    // 辅助
    // =====================================================================

    private Geometry FindGeometry(string key) => (Geometry)FindResource(key);

    private static GridLineStyle ParseGridStyle(string value) => value switch
    {
        "dash" => GridLineStyle.Dash,
        "accent" => GridLineStyle.Accent,
        _ => GridLineStyle.Dual
    };

    private static ImageBackground ParseBackground(string value) => value switch
    {
        "dark" => ImageBackground.Dark,
        "light" => ImageBackground.Light,
        "black" => ImageBackground.Black,
        "white" => ImageBackground.White,
        _ => ImageBackground.Checker
    };

    private void ShowToast(string message, ToastKind kind = ToastKind.Info)
    {
        if (ToastStack == null) return;

        while (ToastStack.Children.Count >= 4) ToastStack.Children.RemoveAt(0);

        string iconKey = kind switch
        {
            ToastKind.Success => "Ico.Check",
            ToastKind.Warn => "Ico.Alert",
            ToastKind.Error => "Ico.Alert",
            _ => "Ico.Info"
        };
        string colorKey = kind switch
        {
            ToastKind.Success => "B.Accent2",
            ToastKind.Warn => "B.Warn",
            ToastKind.Error => "B.Danger",
            _ => "B.Accent"
        };

        var icon = new Icon
        {
            Geometry = FindGeometry(iconKey),
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };
        icon.SetResourceReference(ForegroundProperty, colorKey);

        var text = new TextBlock
        {
            Text = message,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(text);

        var border = new Border
        {
            Child = content,
            Style = (Style)FindResource("PopupCard"),
            Padding = new Thickness(13, 9, 15, 9),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0
        };

        ToastStack.Children.Add(border);
        border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) => ToastStack.Children.Remove(border);
            border.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }
}







