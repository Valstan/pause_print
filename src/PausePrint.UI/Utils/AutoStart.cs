using Microsoft.Win32;

namespace PausePrint.UI.Utils;

public static class AutoStart
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppName = "PausePrint";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var path = key?.GetValue(AppName) as string;
        return !string.IsNullOrEmpty(path);
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ??
                        Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            key.SetValue(AppName, '"' + exePath + '"');
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}


