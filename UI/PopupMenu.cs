using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpriteLens.Controls;

namespace SpriteLens.UI;

public enum MenuKind
{
    Normal,
    Header,
    Separator
}

public sealed class MenuItemModel
{
    public string Text { get; init; } = string.Empty;
    public Geometry? Icon { get; init; }
    public string? Shortcut { get; init; }
    public bool IsChecked { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsDanger { get; init; }
    public MenuKind Kind { get; init; } = MenuKind.Normal;
    public Action? Action { get; init; }

    public static MenuItemModel Header(string text) => new() { Kind = MenuKind.Header, Text = text };

    public static MenuItemModel SeparatorItem() => new() { Kind = MenuKind.Separator };

    public static MenuItemModel Item(string text, Action? action = null, Geometry? icon = null,
        string? shortcut = null, bool isChecked = false, bool isEnabled = true, bool isDanger = false) =>
        new()
        {
            Text = text,
            Action = action,
            Icon = icon,
            Shortcut = shortcut,
            IsChecked = isChecked,
            IsEnabled = isEnabled,
            IsDanger = isDanger
        };
}

public static class PopupMenu
{
    private static Popup? _current;
    private static UIElement? _currentTarget;

    public static void Show(UIElement target, IEnumerable<MenuItemModel> items, bool above = false)
    {
        if (_current is { IsOpen: true } && ReferenceEquals(_currentTarget, target))
        {
            _current.IsOpen = false;
            return;
        }

        _current?.SetCurrentValue(Popup.IsOpenProperty, false);

        var panel = new StackPanel { MinWidth = 210 };
        Style? menuButtonStyle = Application.Current.TryFindResource("MenuButton") as Style;
        Style? cardStyle = Application.Current.TryFindResource("PopupCard") as Style;
        Style? sectionStyle = Application.Current.TryFindResource("SectionTitle") as Style;

        foreach (var item in items)
        {
            if (item.Kind == MenuKind.Separator)
            {
                var line = new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 5, 6, 5)
                };
                line.SetResourceReference(Border.BackgroundProperty, "B.Line");
                panel.Children.Add(line);
                continue;
            }

            if (item.Kind == MenuKind.Header)
            {
                var header = new TextBlock
                {
                    Text = item.Text,
                    Margin = new Thickness(9, 7, 9, 5),
                    Style = sectionStyle
                };
                panel.Children.Add(header);
                continue;
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

            if (item.Icon != null)
            {
                var icon = new Icon
                {
                    Geometry = item.Icon,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center
                };
                icon.SetResourceReference(Control.ForegroundProperty, "B.Text2");
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);
            }

            var text = new TextBlock
            {
                Text = item.Text,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, item.IsDanger ? "B.Danger" : "B.Text");
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                var shortcut = new TextBlock
                {
                    Text = item.Shortcut,
                    FontSize = 10,
                    Margin = new Thickness(18, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono")
                };
                shortcut.SetResourceReference(TextBlock.ForegroundProperty, "B.Text3");
                Grid.SetColumn(shortcut, 3);
                grid.Children.Add(shortcut);
            }

            if (item.IsChecked)
            {
                var check = new Icon
                {
                    Geometry = Application.Current.TryFindResource("Ico.Check") as Geometry,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                check.SetResourceReference(Control.ForegroundProperty, "B.Accent");
                Grid.SetColumn(check, 4);
                grid.Children.Add(check);
            }

            var button = new Button
            {
                Style = menuButtonStyle,
                Content = grid,
                IsEnabled = item.IsEnabled,
                Tag = item.IsDanger ? "danger" : null
            };

            var captured = item;
            button.Click += (_, _) =>
            {
                if (_current != null) _current.IsOpen = false;
                captured.Action?.Invoke();
            };

            panel.Children.Add(button);
        }

        var border = new Border { Child = panel, Style = cardStyle };
        var popup = new Popup
        {
            Child = border,
            PlacementTarget = target,
            Placement = above ? PlacementMode.Top : PlacementMode.Bottom,
            VerticalOffset = above ? -8 : 8,
            HorizontalOffset = 0,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Focusable = false
        };

        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, popup)) _current = null;
            if (ReferenceEquals(_currentTarget, target)) _currentTarget = null;
        };

        _current = popup;
        _currentTarget = target;
        popup.IsOpen = true;
    }
}
