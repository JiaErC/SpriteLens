using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpriteLens.Core;

public sealed class FrameMemory
{
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "dark";

    public bool GridEnabled { get; set; } = true;
    public string GridStyle { get; set; } = "dual";
    public int GridThickness { get; set; } = 1;
    public bool GridLabels { get; set; } = true;
    public bool GridHighlight { get; set; } = true;

    public string Background { get; set; } = "checker";
    public bool PixelSharp { get; set; } = true;

    public double WindowWidth { get; set; } = 1320;
    public double WindowHeight { get; set; } = 840;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool Maximized { get; set; }

    public int SlideshowInterval { get; set; } = 5000;

    [JsonInclude]
    public Dictionary<string, FrameMemory> FrameMemory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpriteLens");

    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    // 反序列化后的字典丢失比较器，这里重新构建。
                    settings.FrameMemory = new Dictionary<string, FrameMemory>(
                        settings.FrameMemory ?? new Dictionary<string, FrameMemory>(),
                        StringComparer.OrdinalIgnoreCase);
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时使用默认值，不影响启动。
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, true);
        }
        catch
        {
            // 保存失败不应打断使用。
        }
    }
}

