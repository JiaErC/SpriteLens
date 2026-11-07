using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SpriteLens.Controls;
using SpriteLens.Core;
using SpriteLens.UI;

namespace SpriteLens;

public partial class MainWindow
{
    private void BuildInfoPanel()
    {
        InfoContent.Children.Clear();
        _info.Clear();

        AddInfoSection("文件", first: true);
        AddInfoRow("Name", "名称");
        AddInfoRow("Type", "类型");
        AddInfoRow("Size", "大小");
        AddInfoRow("Modified", "修改时间");
        AddInfoRow("Path", "路径");

        AddInfoSection("图像");
        AddInfoRow("Dims", "像素尺寸", mono: true);
        AddInfoRow("Ratio", "宽高比");
        AddInfoRow("Pixels", "像素总数", mono: true);

        AddInfoSection("帧网格");
        AddInfoRow("FrameSize", "帧尺寸", mono: true);
        AddInfoRow("Grid", "列 × 行");
        AddInfoRow("Frames", "总帧数", mono: true);
        AddInfoRow("Remainder", "边缘余量");

        _infoFrameExportButton = CreateInfoButton("导出选中帧 PNG", "Ico.Download", ExportSelectedFrame);
        _infoFrameExportButton.Margin = new Thickness(0, 10, 0, 0);
        InfoContent.Children.Add(_infoFrameExportButton);

        AddInfoSection("当前帧");
        AddInfoRow("FrameIndex", "序号", mono: true);
        AddInfoRow("FramePos", "位置");
        AddInfoRow("FrameXY", "图像坐标", mono: true);
        AddInfoRow("FrameSize2", "帧尺寸", mono: true);

        AddInfoSection("光标像素");
        BuildPixelReadout();

        AddInfoSection("视图");
        BuildViewRows();

        AddInfoSection("文件操作");
        BuildFileActions();

        var footer = new TextBlock
        {
            Text = "精灵镜 · 本地运行\n图片不会离开你的电脑",
            FontSize = 10,
            LineHeight = 17,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 20, 0, 4)
        };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "B.Text3");
        InfoContent.Children.Add(footer);
    }

    private void AddInfoSection(string title, bool first = false)
    {
        if (!first)
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 16, 0, 0) };
            divider.SetResourceReference(Border.BackgroundProperty, "B.Line");
            InfoContent.Children.Add(divider);
        }

        var header = new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("SectionTitle"),
            Margin = new Thickness(0, first ? 14 : 13, 0, 9)
        };
        InfoContent.Children.Add(header);
    }

    private TextBlock AddInfoRow(string key, string label, bool mono = false)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock { Text = label, Style = (Style)FindResource("KVLabel") };
        var value = new TextBlock { Text = "—", Style = (Style)FindResource("KVValue") };
        if (mono)
        {
            value.FontFamily = (FontFamily)FindResource("Font.Mono");
            value.FontSize = 10.5;
        }

        Grid.SetColumn(name, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(name);
        row.Children.Add(value);
        InfoContent.Children.Add(row);
        _info[key] = value;
        return value;
    }

    private Button CreateInfoButton(string text, string iconKey, Action action, bool danger = false)
    {
        var button = new Button
        {
            Style = (Style)FindResource(danger ? "Button.Danger" : "Button.Base"),
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(new Icon
        {
            Geometry = FindGeometry(iconKey),
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => action();
        return button;
    }

    private void BuildPixelReadout()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9)
        };
        card.SetResourceReference(Border.BackgroundProperty, "B.Panel2");
        card.SetResourceReference(Border.BorderBrushProperty, "B.Line");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatchHost = new Grid { Width = 38, Height = 38 };
        var checker = new Border { CornerRadius = new CornerRadius(7) };
        checker.SetResourceReference(Border.BackgroundProperty, "B.CheckerSmall");
        _infoPixelSwatch = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent
        };
        _infoPixelSwatch.SetResourceReference(Border.BorderBrushProperty, "B.Line2");
        swatchHost.Children.Add(checker);
        swatchHost.Children.Add(_infoPixelSwatch);

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0)
        };
        _infoPixelHex = new TextBlock
        {
            Text = "—",
            FontFamily = (FontFamily)FindResource("Font.Mono"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold
        };
        _infoPixelRgba = new TextBlock
        {
            Text = "把鼠标移到图片上取色",
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _infoPixelRgba.SetResourceReference(TextBlock.ForegroundProperty, "B.Text3");
        textStack.Children.Add(_infoPixelHex);
        textStack.Children.Add(_infoPixelRgba);

        _infoCopyPixelButton = new Button
        {
            Style = (Style)FindResource("IconButton.XS"),
            IsEnabled = false,
            ToolTip = "复制色值",
            VerticalAlignment = VerticalAlignment.Center
        };
        _infoCopyPixelButton.Content = new Icon
        {
            Geometry = FindGeometry("Ico.Copy"),
            Width = 14,
            Height = 14
        };
        _infoCopyPixelButton.Click += (_, _) => CopyCurrentColor();

        Grid.SetColumn(swatchHost, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(_infoCopyPixelButton, 2);
        grid.Children.Add(swatchHost);
        grid.Children.Add(textStack);
        grid.Children.Add(_infoCopyPixelButton);
        card.Child = grid;
        InfoContent.Children.Add(card);
    }

    private void BuildViewRows()
    {
        var backgroundRow = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        backgroundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        backgroundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var backgroundLabel = new TextBlock { Text = "背景", Style = (Style)FindResource("KVLabel") };
        _infoBgButton = new Button
        {
            Style = (Style)FindResource("Button.Ghost"),
            Height = 26,
            Padding = new Thickness(7, 0, 7, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "选择背景颜色"
        };
        var backgroundContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _infoBgText = new TextBlock { Text = "透明棋盘", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        backgroundContent.Children.Add(_infoBgText);
        backgroundContent.Children.Add(new Icon
        {
            Geometry = FindGeometry("Ico.Chevron"),
            Width = 12,
            Height = 12,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        _infoBgButton.Content = backgroundContent;
        _infoBgButton.Click += (_, _) => ShowBackgroundMenu();
        Grid.SetColumn(backgroundLabel, 0);
        Grid.SetColumn(_infoBgButton, 1);
        backgroundRow.Children.Add(backgroundLabel);
        backgroundRow.Children.Add(_infoBgButton);
        InfoContent.Children.Add(backgroundRow);

        var sharpRow = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        sharpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        sharpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sharpLabel = new TextBlock { Text = "像素锐利", Style = (Style)FindResource("KVLabel") };
        _infoSharpToggle = new ToggleButton
        {
            Style = (Style)FindResource("Switch"),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsChecked = _settings.PixelSharp,
            ToolTip = "放大时使用最近邻采样，保持像素边缘锐利"
        };
        _infoSharpToggle.Checked += (_, _) =>
        {
            if (!_isReady || _suppressSharpToggle) return;
            SetPixelSharp(true);
        };
        _infoSharpToggle.Unchecked += (_, _) =>
        {
            if (!_isReady || _suppressSharpToggle) return;
            SetPixelSharp(false);
        };
        Grid.SetColumn(sharpLabel, 0);
        Grid.SetColumn(_infoSharpToggle, 1);
        sharpRow.Children.Add(sharpLabel);
        sharpRow.Children.Add(_infoSharpToggle);
        InfoContent.Children.Add(sharpRow);

        AddInfoRow("Zoom", "缩放", mono: true);

        _infoResetButton = CreateInfoButton("还原所有编辑", "Ico.Undo", ResetAllEdits);
        InfoContent.Children.Add(_infoResetButton);
    }

    private void BuildFileActions()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _infoExplorerButton = CreateInfoButton("资源管理器中显示", "Ico.Folder", ShowCurrentInExplorer);
        _infoWallpaperButton = CreateInfoButton("设为桌面背景", "Ico.Image", SetCurrentAsWallpaper);
        _infoRenameButton = CreateInfoButton("重命名", "Ico.Pencil", RenameCurrentFile);
        _infoDeleteButton = CreateInfoButton("删除", "Ico.Trash", DeleteCurrentFile, danger: true);

        _infoExplorerButton.Margin = new Thickness(0, 8, 4, 0);
        _infoWallpaperButton.Margin = new Thickness(4, 8, 0, 0);
        _infoRenameButton.Margin = new Thickness(0, 8, 4, 0);
        _infoDeleteButton.Margin = new Thickness(4, 8, 0, 0);

        Grid.SetRow(_infoExplorerButton, 0);
        Grid.SetColumn(_infoExplorerButton, 0);
        Grid.SetRow(_infoWallpaperButton, 0);
        Grid.SetColumn(_infoWallpaperButton, 1);
        Grid.SetRow(_infoRenameButton, 1);
        Grid.SetColumn(_infoRenameButton, 0);
        Grid.SetRow(_infoDeleteButton, 1);
        Grid.SetColumn(_infoDeleteButton, 1);

        grid.Children.Add(_infoExplorerButton);
        grid.Children.Add(_infoWallpaperButton);
        grid.Children.Add(_infoRenameButton);
        grid.Children.Add(_infoDeleteButton);
        InfoContent.Children.Add(grid);
    }

    private void ShowInfoPanel()
    {
        if (!_infoOpen)
        {
            _infoOpen = true;
            Stage.ColumnDefinitions[1].Width = new GridLength(344);
            InfoDrawer.Visibility = Visibility.Visible;
            InfoDrawer.Opacity = 0;
            InfoDrawer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            InfoButton.SetResourceReference(Control.ForegroundProperty, "B.Accent");
            InfoButton.Background = (Brush)FindResource("B.AccentSoft");
        }

        UpdateInfoPanel();
    }

    private void HideInfoPanel()
    {
        if (!_infoOpen) return;
        _infoOpen = false;
        InfoDrawer.BeginAnimation(OpacityProperty, null);
        InfoDrawer.Opacity = 0;
        InfoDrawer.Visibility = Visibility.Collapsed;
        Stage.ColumnDefinitions[1].Width = new GridLength(0);
        InfoButton.ClearValue(Control.ForegroundProperty);
        InfoButton.ClearValue(Control.BackgroundProperty);
    }

    private void CloseInfo_Click(object sender, RoutedEventArgs e) => HideInfoPanel();

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_infoOpen) HideInfoPanel();
        else ShowInfoPanel();
    }

    private void UpdateInfoPanel()
    {
        if (!_infoOpen || _info.Count == 0) return;

        if (_document == null || _currentItem == null)
        {
            foreach (var value in _info.Values) value.Text = "—";
            _info["Name"].Text = "未打开图片";
            _infoBgText!.Text = BackgroundLabel(_settings.Background);
            _infoSharpToggle!.IsChecked = _settings.PixelSharp;
            _infoFrameExportButton!.IsEnabled = false;
            _infoResetButton!.IsEnabled = false;
            SetFileActionsEnabled(false);
            UpdatePixelInfo(null);
            return;
        }

        var document = _document;
        var file = new FileInfo(_currentItem.Path);

        _info["Name"].Text = _currentItem.Name;
        _info["Type"].Text = ImageLoader.GetFormatName(_currentItem.Path);
        _info["Size"].Text = FormatBytes(file.Exists ? file.Length : 0);
        _info["Modified"].Text = file.Exists ? file.LastWriteTime.ToString("yyyy-MM-dd HH:mm") : "—";
        _info["Path"].Text = NativeMethods.ShortPath(document.Path, 46);
        _info["Path"].ToolTip = document.Path;

        _info["Dims"].Text = $"{document.Width} × {document.Height} px";
        _info["Ratio"].Text = RatioText(document.Width, document.Height);
        _info["Pixels"].Text = ((long)document.Width * document.Height).ToString("N0");

        var grid = Sprite.FrameGrid;
        if (grid.IsActive)
        {
            int cols = grid.Columns(document.Width);
            int rows = grid.Rows(document.Height);
            int remainderX = grid.RemainderX(document.Width);
            int remainderY = grid.RemainderY(document.Height);
            _info["FrameSize"].Text = $"{grid.FrameWidth} × {grid.FrameHeight} px";
            _info["Grid"].Text = $"{cols} × {rows}";
            _info["Frames"].Text = (cols * rows).ToString();
            _info["Remainder"].Text = remainderX == 0 && remainderY == 0
                ? "无（整除）"
                : $"{remainderX}px × {remainderY}px";
        }
        else
        {
            _info["FrameSize"].Text = "—";
            _info["Grid"].Text = "—";
            _info["Frames"].Text = "—";
            _info["Remainder"].Text = "—";
        }

        int? frame = _selectedFrame ?? Sprite.HoveredFrame;
        if (frame is int index && grid.IsActive)
        {
            int cols = grid.Columns(document.Width);
            int total = grid.FrameCount(document.Width, document.Height);
            var rect = grid.GetFrameRect(index, document.Width, document.Height);
            if (index >= 0 && index < total && rect.Width > 0)
            {
                _info["FrameIndex"].Text = $"{index + 1} / {total}";
                _info["FramePos"].Text = $"列 {index % cols + 1} · 行 {index / cols + 1}";
                _info["FrameXY"].Text = $"x {rect.X} · y {rect.Y}";
                _info["FrameSize2"].Text = $"{rect.Width} × {rect.Height} px";
                _infoFrameExportButton!.IsEnabled = true;
            }
            else
            {
                SetFrameInfoEmpty();
            }
        }
        else
        {
            SetFrameInfoEmpty();
        }

        _info["Zoom"].Text = $"{Sprite.ZoomPercent}%";
        _infoBgText!.Text = BackgroundLabel(_settings.Background);
        _suppressSharpToggle = true;
        _infoSharpToggle!.IsChecked = _settings.PixelSharp;
        _suppressSharpToggle = false;
        _infoResetButton!.IsEnabled = document.HasEdits;
        SetFileActionsEnabled(true);
        UpdatePixelInfo(_lastColor);
    }

    private void SetFrameInfoEmpty()
    {
        _info["FrameIndex"].Text = "—";
        _info["FramePos"].Text = "—";
        _info["FrameXY"].Text = "—";
        _info["FrameSize2"].Text = "—";
        if (_infoFrameExportButton != null) _infoFrameExportButton.IsEnabled = false;
    }

    private void UpdatePixelInfo(Color? color)
    {
        if (_infoPixelHex == null || _infoPixelRgba == null || _infoPixelSwatch == null || _infoCopyPixelButton == null) return;

        if (color is Color value)
        {
            var brush = new SolidColorBrush(value);
            brush.Freeze();
            _infoPixelSwatch.Background = brush;
            _infoPixelHex.Text = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            _infoPixelRgba.Text = $"rgba({value.R}, {value.G}, {value.B}, {value.A / 255.0:0.##})";
            _infoCopyPixelButton.IsEnabled = true;
        }
        else
        {
            _infoPixelSwatch.Background = Brushes.Transparent;
            _infoPixelHex.Text = "—";
            _infoPixelRgba.Text = "把鼠标移到图片上取色";
            _infoCopyPixelButton.IsEnabled = false;
        }
    }

    private void SetFileActionsEnabled(bool enabled)
    {
        if (_infoExplorerButton != null) _infoExplorerButton.IsEnabled = enabled;
        if (_infoWallpaperButton != null) _infoWallpaperButton.IsEnabled = enabled;
        if (_infoRenameButton != null) _infoRenameButton.IsEnabled = enabled;
        if (_infoDeleteButton != null) _infoDeleteButton.IsEnabled = enabled;
    }

    private void ShowBackgroundMenu()
    {
        if (_infoBgButton == null) return;

        var options = new (string Key, string Label)[]
        {
            ("checker", "透明棋盘"),
            ("dark", "深灰"),
            ("light", "浅灰"),
            ("black", "纯黑"),
            ("white", "纯白")
        };

        var items = options
            .Select(option => MenuItemModel.Item(
                option.Label,
                () => SetBackground(option.Key),
                isChecked: string.Equals(_settings.Background, option.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PopupMenu.Show(_infoBgButton, items);
    }

    private void SetBackground(string key)
    {
        _settings.Background = key;
        Sprite.BackgroundMode = ParseBackground(key);
        if (_infoBgText != null) _infoBgText.Text = BackgroundLabel(key);
        QueueSave();
        UpdateInfoPanel();
    }

    private void SetPixelSharp(bool enabled)
    {
        _settings.PixelSharp = enabled;
        Sprite.PixelSharp = enabled;
        if (_infoSharpToggle != null)
        {
            _suppressSharpToggle = true;
            _infoSharpToggle.IsChecked = enabled;
            _suppressSharpToggle = false;
        }

        QueueSave();
    }

    private void ResetAllEdits()
    {
        if (_document == null) return;
        _document.ResetEdits();
        _document.SetAdjustments(Adjustments.None);
        _adjustTimer.Stop();
        SetAdjustSliders(0, 0, 0);
        _selectedFrame = null;
        Sprite.SelectedFrame = null;
        Sprite.SetImage(_document.Display);
        UpdateFrameBadge();
        UpdateStatus();
        UpdateFrameStatus(null);
        UpdateInfoPanel();
        UpdatePreviewFrame();
        UpdateAdjustButtonState();
        ShowToast("已还原所有编辑。", ToastKind.Success);
    }

    private static string BackgroundLabel(string key) => key switch
    {
        "dark" => "深灰",
        "light" => "浅灰",
        "black" => "纯黑",
        "white" => "纯白",
        _ => "透明棋盘"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1048576.0:0.##} MB"
    };

    private static string RatioText(int width, int height)
    {
        int gcd = Gcd(width, height);
        return gcd > 0 ? $"{width / gcd} : {height / gcd}" : "—";
    }
}
