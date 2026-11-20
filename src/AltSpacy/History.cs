using System.IO;
using System.Text.Json;

namespace AltSpacy;

/// <summary>One thing you ran: a URL you opened, an action, or a Run command.</summary>
public sealed class HistoryEntry
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastUsed { get; set; }
}

/// <summary>
/// Frecency: how often and how recently you used something. Weight halves roughly every week, so habits
/// rise to the top and old one-offs fade. Stored as JSON; a null path keeps it in memory (tests).
/// </summary>
public sealed class History
{
    public const string Url = "url", Action = "action", Run = "run", ShortcutKey = "shortcut", Launch = "launch", TeamsSearch = "teams-search";
    private const int MaxEntries = 300;

    private readonly string? _path;
    private readonly Func<DateTime> _now;
    private readonly List<HistoryEntry> _entries;

    public History(string? path, Func<DateTime>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTime.Now);
        _entries = Load(path);
    }

    public void Record(string kind, string id, string title = "", string detail = "")
    {
        var entry = Find(kind, id);
        if (entry is null) _entries.Add(entry = new HistoryEntry { Kind = kind, Id = id });
        entry.Count++;
        entry.LastUsed = _now();
        if (title.Length > 0) entry.Title = title;
        if (detail.Length > 0) entry.Detail = detail;
        if (_entries.Count > MaxEntries) _entries.Remove(_entries.MinBy(Score)!);
        Save();
    }

    public double Score(string kind, string id) => Find(kind, id) is { } entry ? Score(entry) : 0;

    /// <summary>Most-used recent things you can re-run directly (URLs, actions, commands).</summary>
    public IReadOnlyList<HistoryEntry> Top(int count) =>
        _entries.Where(e => e.Kind != ShortcutKey).OrderByDescending(Score).Take(count).ToList();

    private double Score(HistoryEntry entry) => entry.Count / (1 + Math.Max(0, (_now() - entry.LastUsed).TotalDays) / 7);

    private HistoryEntry? Find(string kind, string id) =>
        _entries.FirstOrDefault(e => e.Kind == kind && e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static List<HistoryEntry> Load(string? path)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            AltSpacy.Log.Write($"history.json unreadable, starting fresh ({path})", ex);
            return [];
        }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AltSpacy.Log.Write("Could not save history", ex);
        }
    }
}
