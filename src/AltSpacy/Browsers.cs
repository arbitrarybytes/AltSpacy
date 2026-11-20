using System.IO;
using System.Windows.Media;
using Microsoft.Win32;

namespace AltSpacy;

public sealed record BrowserInfo(string Name, string Path)
{
    public ImageSource? Icon => ShellIcons.For(Path);
}

/// <summary>Browsers registered with Windows (Settings > Default apps reads the same keys).</summary>
public static class Browsers
{
    private static readonly string[] Roots = [@"SOFTWARE\Clients\StartMenuInternet", @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"];

    public static IReadOnlyList<BrowserInfo> Installed()
    {
        var found = new List<BrowserInfo>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var root in Roots)
        {
            using var clients = hive.OpenSubKey(root);
            if (clients is null) continue;
            foreach (var name in clients.GetSubKeyNames())
            {
                if (name.Equals("IEXPLORE.EXE", StringComparison.OrdinalIgnoreCase)) continue;
                using var client = clients.OpenSubKey(name);
                using var command = client?.OpenSubKey(@"shell\open\command");
                if (command?.GetValue(null) is not string line || ExecutableOf(line) is not { } exe) continue;
                if (found.Any(b => b.Path.Equals(exe, StringComparison.OrdinalIgnoreCase))) continue;
                found.Add(new(client!.GetValue(null) as string ?? name, exe));
            }
        }
        return found.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Name of the browser Windows opens https links with, e.g. "Microsoft Edge".</summary>
    public static string? DefaultName()
    {
        using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        if (choice?.GetValue("ProgId") is not string progId) return null;
        using var app = Registry.ClassesRoot.OpenSubKey($@"{progId}\Application");
        return app?.GetValue("ApplicationName") is string name && !name.StartsWith('@') ? name : progId;
    }

    /// <summary>Finds the configured browser ("msedge", "chrome" or a full path) among the installed ones.</summary>
    public static BrowserInfo? Match(string? configured, IEnumerable<BrowserInfo> installed)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var value = configured.Trim().Trim('"');
        return installed.FirstOrDefault(b => b.Path.Equals(value, StringComparison.OrdinalIgnoreCase)
                                             || System.IO.Path.GetFileNameWithoutExtension(b.Path).Equals(System.IO.Path.GetFileNameWithoutExtension(value), StringComparison.OrdinalIgnoreCase));
    }

    internal static string? ExecutableOf(string commandLine)
    {
        var exe = RunTarget.Split(commandLine).Command;
        exe = Environment.ExpandEnvironmentVariables(exe);
        return File.Exists(exe) ? exe : null;
    }
}
