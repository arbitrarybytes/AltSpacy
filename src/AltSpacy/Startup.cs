using Microsoft.Win32;

namespace AltSpacy;

/// <summary>"Start with Windows": a per-user Run key entry that launches AltSpacy hidden (--background).</summary>
public sealed class StartupRegistration(string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run", string name = "AltSpacy")
{
    public const string BackgroundArg = "--background";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey);
            return key?.GetValue(name) is string;
        }
    }

    public string? Command
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey);
            return key?.GetValue(name) as string;
        }
    }

    public static string CommandFor(string exePath) => $"\"{exePath}\" {BackgroundArg}";

    /// <summary>Moves an entry written under an older name (Omnibar, before 1.5) to this one, pointing at exePath.</summary>
    public bool TakeOverFrom(string oldName, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (key?.GetValue(oldName) is not string) return false;
        key.DeleteValue(oldName, throwOnMissingValue: false);
        key.SetValue(name, CommandFor(exePath));
        return true;
    }

    public void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKey);
        if (enabled) key.SetValue(name, CommandFor(exePath));
        else key.DeleteValue(name, throwOnMissingValue: false);
    }
}
