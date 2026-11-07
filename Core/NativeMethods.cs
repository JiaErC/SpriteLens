using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace SpriteLens.Core;

internal static class NativeMethods
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmcpRound = 2;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private const uint SpiSetDeskWallpaper = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

    public static bool RecycleFile(string path)
    {
        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = 3, // FO_DELETE
                pFrom = path + "\0\0",
                fFlags = 0x40 | 0x10 | 0x04 | 0x400 // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
            };
            return SHFileOperation(ref operation) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyRoundedCorners(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int preference = DwmcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // 旧版 Windows 不支持该属性。
        }
    }

    public static bool SetWallpaper(string path)
    {
        try
        {
            return SystemParametersInfo(SpiSetDeskWallpaper, 0, path, SpifUpdateIniFile | SpifSendChange);
        }
        catch
        {
            return false;
        }
    }

    public static int NaturalCompare(string x, string y)
    {
        try
        {
            return StrCmpLogicalW(x, y);
        }
        catch
        {
            return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    public static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // 忽略资源管理器启动失败。
        }
    }

    public static void OpenWithDefaultApp(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 忽略。
        }
    }

    public static string ShortPath(string path, int max = 52)
    {
        if (path.Length <= max) return path;
        var builder = new StringBuilder(path.Length);
        builder.Append(path.AsSpan(0, 12)).Append('…').Append(path.AsSpan(path.Length - (max - 13)));
        return builder.ToString();
    }
}
