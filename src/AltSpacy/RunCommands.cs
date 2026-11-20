using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AltSpacy;

/// <summary>A well-known Win+R command with a friendly name, e.g. "regedit" = Registry Editor.</summary>
public sealed record RunEntry(string Command, string Title, string Group, string[] Keywords, string Arguments = "");

public static class RunCatalog
{
    public const string Apps = "Apps & tools", Consoles = "Management consoles", ControlPanel = "Control Panel", Folders = "Folders";

    public static readonly RunEntry[] Entries =
    [
        new("wt", "Windows Terminal", Apps, ["terminal", "shell", "console"]),
        new("cmd", "Command Prompt", Apps, ["console", "shell", "dos"]),
        new("powershell", "Windows PowerShell", Apps, ["shell", "ps"]),
        new("pwsh", "PowerShell 7", Apps, ["shell", "ps", "core"]),
        new("notepad", "Notepad", Apps, ["text", "editor"]),
        new("wordpad", "WordPad", Apps, ["write", "rtf", "editor"]),
        new("mspaint", "Paint", Apps, ["draw", "image"]),
        new("calc", "Calculator", Apps, ["math"]),
        new("snippingtool", "Snipping Tool", Apps, ["snip", "screenshot", "capture"]),
        new("explorer", "File Explorer", Apps, ["files", "folders"]),
        new("code", "Visual Studio Code", Apps, ["vscode", "editor", "ide"]),
        new("msedge", "Microsoft Edge", Apps, ["browser", "web"]),
        new("taskmgr", "Task Manager", Apps, ["processes", "kill", "cpu", "memory"]),
        new("regedit", "Registry Editor", Apps, ["registry"]),
        new("control", "Control Panel", Apps, ["classic"]),
        new("rundll32.exe", "Environment variables", Apps, ["env", "path", "variables"], "sysdm.cpl,EditEnvironmentVariables"),
        new("notepad", "Edit hosts file", Apps, ["hosts", "dns"], @"%SystemRoot%\System32\drivers\etc\hosts"),
        new("resmon", "Resource Monitor", Apps, ["network", "disk", "cpu"]),
        new("perfmon", "Performance Monitor", Apps, ["counters"]),
        new("msinfo32", "System Information", Apps, ["hardware", "specs"]),
        new("msconfig", "System Configuration", Apps, ["boot", "startup"]),
        new("dxdiag", "DirectX Diagnostic Tool", Apps, ["graphics", "gpu"]),
        new("winver", "About Windows", Apps, ["version", "build"]),
        new("mstsc", "Remote Desktop Connection", Apps, ["rdp", "remote"]),
        new("charmap", "Character Map", Apps, ["symbols", "unicode"]),
        new("osk", "On-Screen Keyboard", Apps, ["keyboard"]),
        new("magnify", "Magnifier", Apps, ["zoom", "accessibility"]),
        new("sndvol", "Volume Mixer", Apps, ["audio", "sound"]),
        new("cleanmgr", "Disk Cleanup", Apps, ["space", "junk"]),
        new("dfrgui", "Optimize Drives", Apps, ["defrag", "trim"]),
        new("rstrui", "System Restore", Apps, ["restore", "recovery"]),
        new("odbcad32", "ODBC Data Sources", Apps, ["database", "dsn"]),
        new("optionalfeatures", "Windows Features", Apps, ["features", "wsl", "hyper-v"]),
        new("netplwiz", "User Accounts", Apps, ["users", "autologon"]),
        new("systempropertiesadvanced", "Advanced System Settings", Apps, ["performance", "virtual memory"]),
        new("colorcpl", "Color Management", Apps, ["icc", "profile"]),
        new("mmc", "Microsoft Management Console", Apps, ["snap-in"]),

        new("services.msc", "Services", Consoles, ["service", "daemon"]),
        new("devmgmt.msc", "Device Manager", Consoles, ["devices", "drivers", "hardware"]),
        new("diskmgmt.msc", "Disk Management", Consoles, ["partitions", "volumes"]),
        new("compmgmt.msc", "Computer Management", Consoles, ["manage"]),
        new("eventvwr.msc", "Event Viewer", Consoles, ["logs", "events"]),
        new("taskschd.msc", "Task Scheduler", Consoles, ["cron", "schedule", "jobs"]),
        new("gpedit.msc", "Local Group Policy Editor", Consoles, ["policy", "gpo"]),
        new("secpol.msc", "Local Security Policy", Consoles, ["security", "policy"]),
        new("lusrmgr.msc", "Local Users and Groups", Consoles, ["users", "groups"]),
        new("certmgr.msc", "Certificates - Current User", Consoles, ["certs", "ssl", "tls"]),
        new("certlm.msc", "Certificates - Local Computer", Consoles, ["certs", "ssl", "tls", "machine"]),
        new("wf.msc", "Windows Firewall with Advanced Security", Consoles, ["firewall", "ports", "rules"]),
        new("fsmgmt.msc", "Shared Folders", Consoles, ["shares", "smb"]),
        new("printmanagement.msc", "Print Management", Consoles, ["printers"]),
        new("tpm.msc", "TPM Management", Consoles, ["tpm", "security chip"]),

        new("appwiz.cpl", "Programs and Features", ControlPanel, ["uninstall", "programs"]),
        new("ncpa.cpl", "Network Connections", ControlPanel, ["adapters", "network", "ethernet"]),
        new("inetcpl.cpl", "Internet Options", ControlPanel, ["proxy", "internet"]),
        new("mmsys.cpl", "Sound", ControlPanel, ["audio", "playback", "recording"]),
        new("timedate.cpl", "Date and Time", ControlPanel, ["clock", "timezone"]),
        new("powercfg.cpl", "Power Options", ControlPanel, ["battery", "sleep", "power plan"]),
        new("main.cpl", "Mouse Properties", ControlPanel, ["mouse", "pointer"]),
        new("firewall.cpl", "Windows Defender Firewall", ControlPanel, ["firewall"]),
        new("intl.cpl", "Region", ControlPanel, ["locale", "format"]),
        new("sysdm.cpl", "System Properties", ControlPanel, ["computer name", "domain"]),
        new("joy.cpl", "Game Controllers", ControlPanel, ["gamepad", "joystick"]),

        new("shell:downloads", "Downloads", Folders, ["download"]),
        new("shell:desktop", "Desktop", Folders, []),
        new("shell:personal", "Documents", Folders, ["documents"]),
        new("shell:startup", "Startup folder", Folders, ["autostart"]),
        new("shell:appsfolder", "All apps", Folders, ["applications"]),
        new("shell:sendto", "Send To folder", Folders, []),
        new("shell:recent", "Recent files", Folders, []),
        new("shell:fonts", "Fonts", Folders, ["typeface"]),
        new("shell:recyclebinfolder", "Recycle Bin", Folders, ["trash"]),
        new("%TEMP%", "Temp folder", Folders, ["temp", "tmp"]),
        new("%APPDATA%", "AppData (Roaming)", Folders, ["appdata", "roaming"]),
        new("%LOCALAPPDATA%", "AppData (Local)", Folders, ["localappdata", "local"]),
        new("%USERPROFILE%", "User folder", Folders, ["home", "profile"]),
    ];
}

/// <summary>Finds what Win+R would launch for a name: absolute paths, PATH + PATHEXT, then the registry's App Paths.</summary>
public sealed class CommandResolver(IEnumerable<string> searchDirs, IEnumerable<string> extensions, Func<string, string?> appPath)
{
    private readonly string[] _dirs = searchDirs.ToArray();
    private readonly string[] _extensions = extensions.Select(e => e.ToLowerInvariant()).ToArray();
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static CommandResolver System { get; } = new(DefaultDirs(), DefaultExtensions(), AppPathFromRegistry);

    public string? Resolve(string name) => _cache.GetOrAdd(name, ResolveUncached);

    private string? ResolveUncached(string name)
    {
        name = Environment.ExpandEnvironmentVariables(name.Trim().Trim('"'));
        if (name.Length == 0) return null;
        if (Path.IsPathFullyQualified(name)) return File.Exists(name) || Directory.Exists(name) ? name : null;
        if (name.IndexOfAny(['\\', '/', ':']) >= 0) return null;

        var candidates = _extensions.Contains(Path.GetExtension(name).ToLowerInvariant()) || name.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)
            ? [name]
            : _extensions.Select(e => name + e).ToArray();
        foreach (var dir in _dirs)
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(dir, candidate);
            if (File.Exists(path)) return path;
        }
        return appPath(Path.HasExtension(name) ? name : name + ".exe");
    }

    private static IEnumerable<string> DefaultDirs() =>
        new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Environment.ExpandEnvironmentVariables))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> DefaultExtensions() =>
        (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.MSC").Split(';', StringSplitOptions.RemoveEmptyEntries);

    private static string? AppPathFromRegistry(string exe)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string value)
            {
                var path = Environment.ExpandEnvironmentVariables(value.Trim('"'));
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }
}

/// <summary>Something to start with ShellExecute, the way Win+R does.</summary>
public sealed record RunTarget(string File, string Arguments, string? IconPath, bool IsCommand = false)
{
    private static readonly string[] Protocols = ["ms-settings:", "shell:", "windowsdefender:", "mailto:"];

    public void Start(bool elevated = false) =>
        Process.Start(new ProcessStartInfo(File, Arguments)
        {
            UseShellExecute = true,
            Verb = elevated ? "runas" : "",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        });

    /// <summary>Interprets free text like the Run dialog: URLs, protocols, paths, %vars% and "command args".</summary>
    public static RunTarget? Interpret(string text, CommandResolver resolver)
    {
        text = text.Trim();
        if (text.Length < 2) return null;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || Protocols.Any(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return new(text, "", null);

        var expanded = Environment.ExpandEnvironmentVariables(text.Trim('"'));
        if (expanded.StartsWith(@"\\")) return new(expanded, "", null);
        if (Path.IsPathFullyQualified(expanded) && (System.IO.File.Exists(expanded) || Directory.Exists(expanded))) return new(expanded, "", expanded);

        var (command, arguments) = Split(text);
        return resolver.Resolve(command) is { } path ? new(path, arguments, path, IsCommand: true) : null;
    }

    public static (string Command, string Arguments) Split(string text)
    {
        text = text.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end < 0 ? (text.Trim('"'), "") : (text[1..end], text[(end + 1)..].Trim());
        }
        var space = text.IndexOf(' ');
        return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].Trim());
    }
}

/// <summary>The icon Explorer shows for a file or folder.</summary>
public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string path) => Cache.GetOrAdd(path, Load);

    private static ImageSource? Load(string path)
    {
        var info = new ShFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) == IntPtr.Zero || info.Icon == IntPtr.Zero)
            return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private const uint ShgfiIcon = 0x100, ShgfiLargeIcon = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
