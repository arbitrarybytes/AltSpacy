using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace AltSpacy;

public sealed class LauncherAction
{
    public required Func<string> Title { get; init; }
    public required Func<string> Detail { get; init; }
    public required string[] Keywords { get; init; }
    public required Func<ImageSource> Icon { get; init; }
    public required Action Run { get; init; }
    public Action? RunElevated { get; init; }
    public string Group { get; init; } = SystemActions.WindowsGroup;
    public string Badge { get; init; } = "";
    /// <summary>False for catalog entries not installed on this PC: listed only when typed exactly, so "wordpad" explains itself.</summary>
    public bool Available { get; init; } = true;
    public string[] Glyphs { get; init; } = [];
    /// <summary>Stable identity for history (titles like "Switch to dark mode" change).</summary>
    public string Id { get; init; } = "";

    public string Key => Id.Length > 0 ? Id : $"{Group}/{Title()}";

    /// <summary>How a run is remembered: History.Action (by Key), History.Run (command text), ... Empty = not remembered.</summary>
    public string HistoryKind { get; init; } = History.Action;
    public string? HistoryId { get; init; }
}

/// <summary>One row in the bar's list: a URL shortcut or an action, under a group header.</summary>
public sealed record LauncherItem(string Group, string Badge, string Title, string Detail, IconHandle Icon, Shortcut? Shortcut = null, LauncherAction? Action = null);

public static class ActionCatalog
{
    private static readonly char[] Separators = [' ', '-', '(', ')', '/', '&', '.', ',', '\\'];

    /// <summary>Every typed word must start a word in the title, badge or keywords. Exact command/keyword hits rank first.</summary>
    public static IReadOnlyList<LauncherAction> Match(string input, IEnumerable<LauncherAction> actions, Func<LauncherAction, double>? usage = null)
    {
        var query = input.Trim().ToLowerInvariant();
        var tokens = query.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return [];

        return actions
            .Select((action, index) => (action, index, words: Words(action), exact: Exactness(action, query)))
            .Where(x => (x.action.Available || x.exact > 0) && tokens.All(t => x.words.Any(w => w.StartsWith(t, StringComparison.Ordinal))))
            .OrderByDescending(x => x.exact)
            .ThenByDescending(x => usage?.Invoke(x.action) ?? 0)
            .ThenBy(x => x.index)
            .Select(x => x.action)
            .ToList();
    }

    /// <summary>2 = typed the title or the command itself ("proxy", "regedit", "services"), 1 = typed a keyword, 0 = partial.</summary>
    private static int Exactness(LauncherAction action, string query) =>
        action.Title().Equals(query, StringComparison.OrdinalIgnoreCase)
        || action.Badge.Equals(query, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(action.Badge).Equals(query, StringComparison.OrdinalIgnoreCase) ? 2
        : action.Keywords.Contains(query, StringComparer.OrdinalIgnoreCase) ? 1 : 0;

    private static string[] Words(LauncherAction action) =>
        $"{action.Title()} {action.Badge} {string.Join(' ', action.Keywords)}".ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}

public static class SystemActions
{
    public const string WindowsGroup = "Windows", SettingsGroup = "Settings", AltSpacyGroup = "AltSpacy", RunGroup = "Run";
    public const string Moon = "\uE708", Sun = "\uE706";

    private static readonly Color WindowsBlue = Color.FromRgb(0x00, 0x67, 0xC0);
    private static readonly Color Slate = Color.FromRgb(0x4A, 0x51, 0x60);
    private static readonly Color Teal = Color.FromRgb(0x0F, 0x76, 0x6E);
    private static readonly Color Indigo = Color.FromRgb(0x4F, 0x46, 0xE5);
    private static readonly Color Amber = Color.FromRgb(0xD9, 0x77, 0x06);
    private static readonly Color Violet = Color.FromRgb(0x7C, 0x3A, 0xED);
    private static readonly Color Coral = Color.FromRgb(0xE5, 0x53, 0x3D);
    private static readonly Color Night = Color.FromRgb(0x1F, 0x29, 0x37);
    private static readonly Color Sunny = Color.FromRgb(0xF5, 0x9E, 0x0B);

    private static readonly Dictionary<string, (string Glyph, Color Color)> GroupArt = new()
    {
        [RunCatalog.Apps] = ("\uE71D", Slate),
        [RunCatalog.Consoles] = ("\uE9D9", Teal),
        [RunCatalog.ControlPanel] = ("\uE713", Indigo),
        [RunCatalog.Folders] = ("\uE8B7", Amber),
        [RunGroup] = ("\uE756", Violet),
    };

    public static IReadOnlyList<LauncherAction> Create(Action<string> altspacyCommand, CommandResolver? resolver = null)
    {
        resolver ??= CommandResolver.System;
        return
        [
            new()
            {
                Title = () => WindowsTheme.IsLight ? "Switch to dark mode" : "Switch to light mode",
                Detail = () => WindowsTheme.IsLight ? "Windows is in light mode" : "Windows is in dark mode",
                Keywords = ["theme", "dark", "light", "mode", "night", "personalization", "toggle"],
                Icon = () => WindowsTheme.IsLight ? IconArt.Glyph(Moon, Night) : IconArt.Glyph(Sun, Sunny),
                Run = WindowsTheme.Toggle,
                Glyphs = [Moon, Sun],
                Id = "theme",
            },
            Make("Lock PC", "Lock the screen", ["lock", "away"], "\uE72E", Slate, WindowsGroup,
                () => new RunTarget("rundll32.exe", "user32.dll,LockWorkStation", null).Start()),

            .. RunCatalog.Entries.Select(entry => FromCatalog(entry, resolver)),

            Page("Windows Settings", ["settings", "control"], "\uE713", ""),
            Page("Display", ["screen", "monitor", "resolution", "scale", "brightness"], "\uE7F4", "display"),
            Page("Sound", ["audio", "volume", "speaker", "microphone", "headset"], "\uE767", "sound"),
            Page("Bluetooth & devices", ["devices", "pair", "mouse", "keyboard"], "\uE702", "bluetooth"),
            Page("Wi-Fi", ["wifi", "wireless", "network", "internet"], "\uE701", "network-wifi"),
            Page("Network status", ["network", "ethernet", "internet"], "\uE774", "network-status"),
            Page("Proxy", ["network"], "\uE774", "network-proxy"),
            Page("VPN", ["network"], "\uE705", "network-vpn"),
            Page("Colors and accent", ["accent", "personalization", "theme"], "\uE790", "colors"),
            Page("Background", ["wallpaper", "personalization"], "\uE790", "personalization-background"),
            Page("Taskbar", ["personalization", "start"], "\uE71D", "taskbar"),
            Page("Night light", ["nightlight", "blue", "warm", "eyes"], Moon, "nightlight"),
            Page("Notifications", ["focus", "do not disturb"], "\uE7E7", "notifications"),
            Page("Power & battery", ["sleep", "battery", "energy"], "\uE7E8", "powersleep"),
            Page("Storage", ["disk", "space", "storage sense"], "\uE8B7", "storagesense"),
            Page("Clipboard", ["history", "copy", "paste"], "\uE77F", "clipboard"),
            Page("Multitasking", ["snap", "windows", "virtual desktops"], "\uE737", "multitasking"),
            Page("Mouse", ["pointer", "scroll"], "\uE962", "mousetouchpad"),
            Page("Date & time", ["clock", "timezone"], "\uE787", "dateandtime"),
            Page("Language & region", ["locale", "keyboard layout"], "\uE774", "regionlanguage"),
            Page("Printers & scanners", ["printer", "scanner"], "\uE749", "printers"),
            Page("Startup apps", ["autostart", "boot"], "\uE7B5", "startupapps"),
            Page("Sign-in options", ["password", "pin", "hello"], "\uE72E", "signinoptions"),
            Page("Privacy & security", ["permissions", "camera", "location"], "\uE72E", "privacy"),
            Page("Windows Update", ["patch", "upgrade"], "\uE895", "windowsupdate"),
            Page("Installed apps", ["uninstall", "programs", "features"], "\uE71D", "appsfeatures"),
            Page("Default apps", ["defaults", "browser", "associations"], "\uE71D", "defaultapps"),
            Page("About this PC", ["specs", "device name", "edition"], "\uE7F8", "about"),

            Make("AltSpacy settings", "Shortcuts, hotkey and browser", ["preferences", "config", "shortcuts", "altspacy"], "\uE713", Coral, AltSpacyGroup, () => altspacyCommand("settings")),
            Make("About AltSpacy", $"Version {AppInfo.Version}", ["version", "help", "altspacy"], "\uE946", Coral, AltSpacyGroup, () => altspacyCommand("about")),
            Make("Reload AltSpacy config", "Re-read shortcuts.json", ["refresh", "altspacy", "reload"], "\uE72C", Coral, AltSpacyGroup, () => altspacyCommand("reload")),
            Make("Edit AltSpacy config file", "Open shortcuts.json in Notepad", ["edit", "json", "altspacy"], "\uE70F", Coral, AltSpacyGroup, () => altspacyCommand("edit")),
            Make("Exit AltSpacy", "Close the launcher", ["quit", "close", "altspacy"], "\uE7E8", Coral, AltSpacyGroup, () => altspacyCommand("exit")),
        ];
    }

    /// <summary>"Run ping 8.8.8.8" / "Open C:\Temp": anything the Run dialog would accept that isn't in the catalog.</summary>
    public static LauncherAction? RunText(string text, CommandResolver? resolver = null)
    {
        if (RunTarget.Interpret(text, resolver ?? CommandResolver.System) is not { } target) return null;
        var (glyph, color) = GroupArt[RunGroup];
        return new LauncherAction
        {
            Title = () => target.IsCommand ? $"Run {text.Trim()}" : $"Open {text.Trim()}",
            Detail = () => target.IconPath ?? target.File,
            Keywords = [],
            Icon = () => (target.IconPath is { } path ? ShellIcons.For(path) : null) ?? IconArt.Glyph(glyph, color),
            Run = () => target.Start(),
            RunElevated = () => target.Start(elevated: true),
            Group = RunGroup,
            Badge = "run",
            Glyphs = [glyph],
            HistoryKind = History.Run,
            HistoryId = text.Trim(),
        };
    }

    private static LauncherAction FromCatalog(RunEntry entry, CommandResolver resolver)
    {
        var expanded = Environment.ExpandEnvironmentVariables(entry.Command);
        var isFolder = entry.Command.StartsWith('%') || entry.Command.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        var path = entry.Command.StartsWith('%') ? (Directory.Exists(expanded) ? expanded : null)
            : entry.Command.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? ShellFolderPath(entry.Command)
            : resolver.Resolve(entry.Command);
        var available = path is not null || entry.Command.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        var iconPath = entry.Command.StartsWith("rundll32", StringComparison.OrdinalIgnoreCase) ? null : path;
        var (glyph, color) = GroupArt[entry.Group];
        var target = new RunTarget(isFolder ? expanded : path ?? entry.Command, Environment.ExpandEnvironmentVariables(entry.Arguments), path);

        return new LauncherAction
        {
            Title = () => entry.Title,
            Detail = () => available ? path ?? entry.Command : "Not installed on this PC",
            Keywords = [entry.Command, .. entry.Keywords],
            Icon = () => (iconPath is not null ? ShellIcons.For(iconPath) : null) ?? IconArt.Glyph(glyph, color),
            Run = () => Start(target, available, entry, elevated: false),
            RunElevated = isFolder ? null : () => Start(target, available, entry, elevated: true),
            Group = entry.Group,
            Badge = entry.Arguments.Length > 0 ? entry.Keywords[0] : entry.Command.ToLowerInvariant(),
            Available = available,
            Glyphs = [glyph],
        };
    }

    private static void Start(RunTarget target, bool available, RunEntry entry, bool elevated)
    {
        if (!available) throw new InvalidOperationException($"{entry.Title} isn't installed on this PC.");
        target.Start(elevated);
    }

    private static string? ShellFolderPath(string command) => command.ToLowerInvariant() switch
    {
        "shell:downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "shell:desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "shell:personal" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "shell:startup" => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "shell:sendto" => Environment.GetFolderPath(Environment.SpecialFolder.SendTo),
        "shell:recent" => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
        "shell:fonts" => Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        _ => null,
    } is { } path && Directory.Exists(path) ? path : null;

    private static LauncherAction Page(string title, string[] keywords, string glyph, string page) =>
        Make(title, $"ms-settings:{page}", keywords, glyph, WindowsBlue, SettingsGroup, () => new RunTarget($"ms-settings:{page}", "", null).Start());

    private static LauncherAction Make(string title, string detail, string[] keywords, string glyph, Color color, string group, Action run) =>
        new() { Title = () => title, Detail = () => detail, Keywords = keywords, Icon = () => IconArt.Glyph(glyph, color), Run = run, Group = group, Glyphs = [glyph] };
}

/// <summary>Reads and flips the Windows light/dark setting for apps and the taskbar (Settings > Personalization > Colors).</summary>
public static class WindowsTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    public static bool IsLight
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
    }

    public static void Toggle() => Set(light: !IsLight);

    public static void Set(bool light)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        }
        // Tell running apps and the taskbar to re-read the theme. Off the UI thread: it waits on every top-level window.
        Task.Run(() => SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "ImmersiveColorSet", SmtoAbortIfHung, 200, out _));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
