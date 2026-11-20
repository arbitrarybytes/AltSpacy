using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AltSpacy;

/// <summary>Recognises emails, web addresses and Jira keys in typed or copied text.</summary>
public static partial class SmartInput
{
    private static readonly HashSet<string> CommonTlds =
        ["com", "net", "org", "io", "dev", "ai", "app", "co", "in", "uk", "us", "de", "edu", "gov", "info", "me", "ly", "cloud", "tech", "site"];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^(?<scheme>https?://)?(?<host>([\w-]+\.)+(?<tld>[A-Za-z]{2,}))(:\d+)?(?<path>[/?#]\S*)?$")]
    private static partial Regex WebPattern();

    [GeneratedRegex(@"^[A-Z][A-Z0-9]+-\d+$")]
    private static partial Regex JiraKeyPattern();

    public static bool IsEmail(string text) => EmailPattern().IsMatch(text.Trim());

    public static bool IsJiraKey(string text) => JiraKeyPattern().IsMatch(text.Trim());

    /// <summary>
    /// "https://x", "www.example.com", "github.com/dotnet" -> a full https URL. Bare names need a common TLD,
    /// so "notepad.exe" or "services.msc" stay Run commands.
    /// </summary>
    public static string? WebUrl(string text)
    {
        text = text.Trim();
        var m = WebPattern().Match(text);
        if (!m.Success) return null;
        var known = m.Groups["scheme"].Success || m.Groups["host"].Value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                    || CommonTlds.Contains(m.Groups["tld"].Value.ToLowerInvariant());
        return known ? (m.Groups["scheme"].Success ? text : "https://" + text) : null;
    }

    public static string MailTo(string email) => "mailto:" + email.Trim();

    public static string TeamsChat(string email) => "msteams:/l/chat/0/0?users=" + Uri.EscapeDataString(email.Trim());
}

/// <summary>
/// Teams has a supported link for chatting with an email address, but none for searching by name. For names,
/// AltSpacy opens Teams, focuses its search box (Ctrl+E) and types the name, which is what you'd do by hand.
/// </summary>
public static class TeamsLauncher
{
    public static void Search(string text)
    {
        new RunTarget("msteams:", "", null).Start();
        Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (TeamsWindow() is { } hwnd and not 0)
                {
                    SetForegroundWindow(hwnd);
                    await Task.Delay(800);
                    System.Windows.Forms.SendKeys.SendWait("^e");
                    await Task.Delay(600);
                    System.Windows.Forms.SendKeys.SendWait(EscapeSendKeys(text));
                    await Task.Delay(250);
                    System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                    return;
                }
                await Task.Delay(250);
            }
            Log.Write("Teams search: Teams window did not appear");
        });
    }

    private static IntPtr? TeamsWindow() =>
        Process.GetProcessesByName("ms-teams").Concat(Process.GetProcessesByName("Teams"))
            .Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);

    /// <summary>SendKeys treats + ^ % ~ ( ) { } [ ] as commands; wrap them so they are typed literally.</summary>
    public static string EscapeSendKeys(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text) builder.Append("+^%~(){}[]".Contains(c) ? $"{{{c}}}" : c.ToString());
        return builder.ToString();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}

public sealed record ReleaseNote(string Version, string Notes);

/// <summary>The single source for "What's new" in About; the first entry must match the app version (tested).</summary>
public static class ReleaseNotes
{
    public static readonly ReleaseNote[] All =
    [
        new("1.5.0", "Omnibar is now AltSpacy: your shortcuts, history and Start with Windows carry over. First public release, free under the GNU GPL v3. New starter shortcuts: Google, GitHub, Stack Overflow, Wikipedia, YouTube. " +
                  "Runs on .NET 10: a small download if you have it, a standalone one if not. More site icons load. No more tinted strip across the top of the bar."),
        new("1.4.0", "Teams: t <name or email>. Emails open your mail app, web addresses your browser. Calculator. Clipboard suggestions. " +
                  "Remembers what you use most. Start with Windows. {project} shortcuts like pr runtime 1190. Settings and data now in %APPDATA%\\AltSpacy."),
        new("1.3.0", "Run anything Win+R can, with real app icons. Acrylic launcher that follows light/dark mode. GitHub, GitLab search and Confluence. Redesigned Settings."),
        new("1.2.0", "Site icons, Windows actions, dark/light toggle."),
        new("1.1.0", "Settings and About windows. Space or colon after the key."),
    ];
}
