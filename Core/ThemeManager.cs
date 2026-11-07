using System.Windows;

namespace SpriteLens.Core;

public static class ThemeManager
{
    public static string Current { get; private set; } = "dark";

    public static event EventHandler? ThemeChanged;

    public static void Apply(string? theme)
    {
        bool light = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        string file = light ? "Light" : "Dark";
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Themes/{file}.xaml", UriKind.Relative)
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }

        Current = light ? "light" : "dark";
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Toggle() => Current == "light" ? "dark" : "light";
}
