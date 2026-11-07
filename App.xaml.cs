using System.IO;
using System.Windows;
using SpriteLens.Core;

namespace SpriteLens;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsStore.Load();
        ThemeManager.Apply(Settings.Theme);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteError(args.ExceptionObject as Exception);

        var window = new MainWindow(Settings, e.Args);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        WriteError(e.Exception);
        MessageBox.Show(
            "程序遇到一个错误，但仍然可以继续使用。\n\n" + e.Exception.Message,
            "精灵镜",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void WriteError(Exception? exception)
    {
        if (exception == null) return;
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.AppendAllText(
                Path.Combine(SettingsStore.Folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch
        {
            // 日志失败不应再次抛出。
        }
    }
}
