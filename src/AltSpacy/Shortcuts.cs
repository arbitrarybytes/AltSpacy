using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AltSpacy;

public sealed class Shortcut
{
    public string Key { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Description { get; set; }
    public string? PrefixIfNumeric { get; set; }
    /// <summary>Project used for {project} when you don't type one, e.g. "runtime" so "pr 1190" works.</summary>
    public string? DefaultProject { get; set; }
    public string? Icon { get; set; }

    [JsonIgnore]
    public bool HasPlaceholder => UsesValue || UsesProject;

    [JsonIgnore]
    public bool UsesValue => Url.Contains("{q}") || Url.Contains("{raw}");

    [JsonIgnore]
    public bool UsesProject => Url.Contains("{project}");

    /// <summary>Friendly app name used to group shortcuts, e.g. "GitLab" for gitlab.example.com.</summary>
    [JsonIgnore]
    public string App => Uri.TryCreate(ShortcutResolver.Sample(Url), UriKind.Absolute, out var uri) ? AppNames.For(uri.Host) : "New";

    public Shortcut Clone() => (Shortcut)MemberwiseClone();
}

public sealed record ParsedInput(string Key, string Value, bool HasSeparator);

public sealed record Resolution(Shortcut Shortcut, string Url);

public static partial class ShortcutResolver
{
    [GeneratedRegex(@"^\s*(?<key>[A-Za-z0-9_-]+)(?<sep>\s*[:\s]\s*(?<value>.*?))?\s*$")]
    private static partial Regex InputPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    public static partial Regex KeyPattern();

    /// <summary>The URL template with placeholders filled with dummy values, for host/app detection.</summary>
    public static string Sample(string url) => url.Replace("{q}", "x").Replace("{raw}", "x").Replace("{project}", "x");

    public static ParsedInput? Parse(string input)
    {
        var m = InputPattern().Match(input);
        if (!m.Success) return null;
        // "c:\Windows" or "d:/logs" is a drive path for the Run dialog, not the "c" shortcut.
        var trimmed = input.TrimStart();
        if (trimmed.Length > 2 && trimmed[1] == ':' && trimmed[2] is '\\' or '/' && char.IsLetter(trimmed[0])) return null;
        return new(m.Groups["key"].Value, m.Groups["value"].Value, m.Groups["sep"].Success);
    }

    /// <summary>
    /// Turns "pr 1190" or "pr runtime 1190" into a URL. {project} takes the first word when it is a known alias or
    /// contains a '/', otherwise the shortcut's defaultProject. Aliases come from the config's "projects" map.
    /// </summary>
    public static Resolution? Resolve(string input, IEnumerable<Shortcut> shortcuts, IReadOnlyDictionary<string, string>? projects = null)
    {
        if (Parse(input) is not { } parsed) return null;
        var shortcut = shortcuts.FirstOrDefault(s => s.Key.Equals(parsed.Key, StringComparison.OrdinalIgnoreCase));
        if (shortcut is null) return null;
        if (!shortcut.HasPlaceholder) return new(shortcut, shortcut.Url);

        var raw = parsed.Value;
        var url = shortcut.Url;
        if (shortcut.UsesProject)
        {
            var space = raw.IndexOf(' ');
            var (first, rest) = space < 0 ? (raw, "") : (raw[..space], raw[(space + 1)..].Trim());
            // With a default project, only aliases switch project (so "gb feature/x" is a branch, not a project path).
            var isProject = first.Length > 0 && IsProject(first, projects, allowPaths: string.IsNullOrWhiteSpace(shortcut.DefaultProject));
            string? project;
            if (isProject && rest.Length == 0 && shortcut.UsesValue) return null;
            if (isProject || (!shortcut.UsesValue && first.Length > 0))
            {
                project = first;
                raw = rest;
            }
            else project = shortcut.DefaultProject;

            if (string.IsNullOrWhiteSpace(project)) return null;
            url = url.Replace("{project}", ProjectPath(project, projects));
        }

        if (!shortcut.UsesValue) return raw.Length == 0 ? new(shortcut, url) : null;
        var q = Unquote(raw);
        if (q.Length == 0) return null;
        if (!string.IsNullOrEmpty(shortcut.PrefixIfNumeric) && q.All(char.IsAsciiDigit)) q = shortcut.PrefixIfNumeric + q;
        return new(shortcut, url.Replace("{q}", Uri.EscapeDataString(q)).Replace("{raw}", Uri.EscapeDataString(raw)));
    }

    private static bool IsProject(string word, IReadOnlyDictionary<string, string>? projects, bool allowPaths) =>
        (allowPaths && word.Contains('/')) || projects?.Keys.Any(k => k.Equals(word, StringComparison.OrdinalIgnoreCase)) == true;

    private static string ProjectPath(string project, IReadOnlyDictionary<string, string>? projects)
    {
        var path = projects?.FirstOrDefault(p => p.Key.Equals(project, StringComparison.OrdinalIgnoreCase)).Value ?? project;
        return string.Join('/', path.Trim('/').Split('/').Select(Uri.EscapeDataString));
    }

    public static IReadOnlyList<Shortcut> Suggest(string input, IEnumerable<Shortcut> shortcuts)
    {
        if (string.IsNullOrWhiteSpace(input)) return shortcuts.ToList();
        if (Parse(input) is not { } parsed) return [];
        return parsed.HasSeparator
            ? shortcuts.Where(s => s.Key.Equals(parsed.Key, StringComparison.OrdinalIgnoreCase)).ToList()
            : shortcuts.Where(s => s.Key.StartsWith(parsed.Key, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Key.Length).ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1].Trim() : value;
}

public static class AppNames
{
    private static readonly (string Marker, string Name)[] Known =
    [
        ("jira", "Jira"), ("gitlab", "GitLab"), ("github", "GitHub"), ("confluence", "Confluence"), ("splunk", "Splunk"),
        ("google", "Google"), ("youtube", "YouTube"), ("wikipedia", "Wikipedia"), ("stackoverflow", "Stack Overflow"),
    ];

    public static string For(string host) =>
        Known.FirstOrDefault(k => host.Contains(k.Marker, StringComparison.OrdinalIgnoreCase)).Name ?? host;
}

public static class BrowserLauncher
{
    public static void Open(string url, string? browser)
    {
        url = url.Replace("\"", "%22");
        var psi = string.IsNullOrWhiteSpace(browser)
            ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo(browser, $"\"{url}\"") { UseShellExecute = true };
        Process.Start(psi);
    }
}
