using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltSpacy;

public sealed class AltSpacyConfig
{
    public string Hotkey { get; set; } = "Alt+Space";
    public string? Browser { get; set; } = "msedge";
    public List<Shortcut> Shortcuts { get; set; } = [];
    /// <summary>Aliases for {project}, e.g. "runtime" -> "dotnet/runtime".</summary>
    public Dictionary<string, string> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ConfigStore
{
    public static string DefaultPath => AppPaths.Config;

    /// <summary>Written above the JSON on every save, so hand-editing stays easy even after saving from Settings.</summary>
    public const string Header =
        """
        // AltSpacy shortcuts. Edit here or in Settings (tray icon > Settings); changes apply on save.
        // Type a key, a space, then the value:  g weather   pr dotnet/runtime 1190   pr runtime 1190
        //   {q}       value without surrounding quotes, URL-encoded
        //   {raw}     value exactly as typed, URL-encoded
        //   {project} first word if it is an alias in "projects" (or an owner/repo path), else the shortcut's defaultProject
        //   prefixIfNumeric  added to digits-only values (j 41 -> PROJ-41)
        //   icon      optional image URL or file path; defaults to the site's favicon

        """;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AltSpacyConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<AltSpacyConfig>(json, ReadOptions) ?? throw new InvalidDataException("Config is empty.");
        config.Projects = new Dictionary<string, string>(config.Projects ?? [], StringComparer.OrdinalIgnoreCase);
        Validate(config);
        return config;
    }

    public static void Validate(AltSpacyConfig config)
    {
        try
        {
            HotkeyBinding.Parse(config.Hotkey);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(ex.Message);
        }
        foreach (var s in config.Shortcuts)
        {
            if (!ShortcutResolver.KeyPattern().IsMatch(s.Key))
                throw new InvalidDataException($"Invalid key '{s.Key}': use letters, digits, '-' or '_'.");
            if (!IsHttp(s.Url))
                throw new InvalidDataException($"Shortcut '{s.Key}': url must start with http:// or https://.");
            if (!string.IsNullOrWhiteSpace(s.Icon) && !IsHttp(s.Icon) && !Path.IsPathFullyQualified(s.Icon))
                throw new InvalidDataException($"Shortcut '{s.Key}': icon must be an http(s) URL or a full file path.");
        }
        var duplicate = config.Shortcuts.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate shortcut key '{duplicate.Key}'.");
    }

    private static bool IsHttp(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public static string ReadDefault()
    {
        using var stream = typeof(ConfigStore).Assembly.GetManifestResourceStream("AltSpacy.shortcuts.default.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static AltSpacyConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ReadDefault());
        }
        return Parse(File.ReadAllText(path));
    }

    public static void Save(string path, AltSpacyConfig config)
    {
        Validate(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Header + JsonSerializer.Serialize(config, WriteOptions));
    }
}
