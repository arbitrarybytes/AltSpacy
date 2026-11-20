using System.IO;

namespace AltSpacy;

/// <summary>
/// Where AltSpacy keeps its data: %APPDATA%\AltSpacy (or %ALTSPACY_HOME% when set), so the exe can live anywhere,
/// including a read-only folder such as Program Files.
/// </summary>
public static class AppPaths
{
    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AltSpacy");

    public static string Root { get; } = Environment.GetEnvironmentVariable("ALTSPACY_HOME") is { Length: > 0 } home ? home : DefaultRoot;

    /// <summary>The data folder from before 1.5, when the app was called Omnibar.</summary>
    public static string LegacyRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Omnibar");

    public static string Config => Path.Combine(Root, "shortcuts.json");
    public static string History => Path.Combine(Root, "history.json");
    public static string Log => Path.Combine(Root, "altspacy.log");
    public static string Icons => Path.Combine(Root, "icons");

    /// <summary>
    /// Copies shortcuts, history and cached icons from the folder used while the app was called Omnibar, once: only
    /// when there is no config here yet. The old folder is left as it was.
    /// </summary>
    public static bool MigrateFrom(string legacyRoot, string root)
    {
        var legacyConfig = Path.Combine(legacyRoot, "shortcuts.json");
        var config = Path.Combine(root, "shortcuts.json");
        if (File.Exists(config) || !File.Exists(legacyConfig)) return false;

        Directory.CreateDirectory(root);
        var legacyIcons = Path.Combine(legacyRoot, "icons");
        if (Directory.Exists(legacyIcons))
        {
            var icons = Directory.CreateDirectory(Path.Combine(root, "icons")).FullName;
            foreach (var file in Directory.EnumerateFiles(legacyIcons))
                File.Copy(file, Path.Combine(icons, Path.GetFileName(file)), overwrite: true);
        }
        var legacyHistory = Path.Combine(legacyRoot, "history.json");
        if (File.Exists(legacyHistory)) File.Copy(legacyHistory, Path.Combine(root, "history.json"), overwrite: true);
        File.Copy(legacyConfig, config); // last: an interrupted copy is retried on the next start
        return true;
    }

    /// <summary>Copies a config kept next to the exe by older versions into the new location, once.</summary>
    public static bool Migrate(string legacyConfig, string config)
    {
        if (File.Exists(config) || !File.Exists(legacyConfig)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.Copy(legacyConfig, config);
        return true;
    }
}

/// <summary>Small append-only log for things that would otherwise fail silently (icons, launches, config).</summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; set; } = AppPaths.Log;

    public static void Write(string message, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Move(FilePath, FilePath + ".1", overwrite: true);
                var detail = error is null ? "" : $" | {error.GetType().Name}: {error.Message}";
                var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                File.AppendAllText(FilePath, $"{time} {message}{detail}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
