using System.IO;
using System.Windows.Input;

namespace AltSpacy.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Default_config_parses()
    {
        var config = ConfigStore.Parse(ConfigStore.ReadDefault());
        Assert.Equal("Alt+Space", config.Hotkey);
        Assert.Equal("", config.Browser);
        Assert.Empty(config.Projects);
        Assert.Contains(config.Shortcuts, s => s.Key == "pr" && s.UsesProject);
        Assert.All(config.Shortcuts, s => Assert.StartsWith("https://", s.Url));
    }

    [Fact]
    public void Shipped_defaults_contain_no_personal_projects_or_prefixes()
    {
        var json = ConfigStore.ReadDefault();
        Assert.DoesNotContain("routing-service", json);
        Assert.DoesNotContain("CML-", json);
        Assert.DoesNotContain("IRE", json);
    }

    [Fact]
    public void Save_keeps_a_comment_header_and_projects()
    {
        var path = Path.Combine(Path.GetTempPath(), $"altspacy-{Guid.NewGuid():N}", "shortcuts.json");
        try
        {
            var config = TestConfig.Load();
            ConfigStore.Save(path, config);
            var text = File.ReadAllText(path);
            Assert.StartsWith(ConfigStore.Header, text);
            Assert.Contains("{project}", ConfigStore.Header);
            var loaded = ConfigStore.Load(path);
            Assert.Equal(TestConfig.Ire, loaded.Projects["IRE"]);
            Assert.Equal("ire", loaded.Shortcuts.Single(s => s.Key == "mr").DefaultProject);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Migrates_old_config_next_to_exe_once()
    {
        var dir = Directory.CreateTempSubdirectory("altspacy-migrate-").FullName;
        try
        {
            var legacy = Path.Combine(dir, "old", "shortcuts.json");
            var current = Path.Combine(dir, "appdata", "AltSpacy", "shortcuts.json");
            Assert.False(AppPaths.Migrate(legacy, current));
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(legacy, TestConfig.Json);
            Assert.True(AppPaths.Migrate(legacy, current));
            Assert.Equal(TestConfig.Json, File.ReadAllText(current));
            File.WriteAllText(legacy, "{}");
            Assert.False(AppPaths.Migrate(legacy, current));
            Assert.Equal(TestConfig.Json, File.ReadAllText(current));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Copies_data_from_the_old_Omnibar_folder_once()
    {
        var dir = Directory.CreateTempSubdirectory("altspacy-rename-").FullName;
        try
        {
            var legacy = Path.Combine(dir, "Omnibar");
            var root = Path.Combine(dir, "AltSpacy");
            Assert.False(AppPaths.MigrateFrom(legacy, root));

            Directory.CreateDirectory(Path.Combine(legacy, "icons"));
            File.WriteAllText(Path.Combine(legacy, "shortcuts.json"), TestConfig.Json);
            File.WriteAllText(Path.Combine(legacy, "history.json"), "[]");
            File.WriteAllText(Path.Combine(legacy, "icons", "github.com.icon"), "png");
            File.WriteAllText(Path.Combine(legacy, "omnibar.log"), "old log");

            Assert.True(AppPaths.MigrateFrom(legacy, root));
            Assert.Equal(TestConfig.Json, File.ReadAllText(Path.Combine(root, "shortcuts.json")));
            Assert.Equal("[]", File.ReadAllText(Path.Combine(root, "history.json")));
            Assert.Equal("png", File.ReadAllText(Path.Combine(root, "icons", "github.com.icon")));
            Assert.False(File.Exists(Path.Combine(root, "omnibar.log")));
            Assert.True(File.Exists(Path.Combine(legacy, "shortcuts.json")), "the old folder is left as it was");

            File.WriteAllText(Path.Combine(legacy, "shortcuts.json"), "{}");
            Assert.False(AppPaths.MigrateFrom(legacy, root));
            Assert.Equal(TestConfig.Json, File.ReadAllText(Path.Combine(root, "shortcuts.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Log_appends_and_rolls_over()
    {
        var dir = Directory.CreateTempSubdirectory("altspacy-log-").FullName;
        var original = Log.FilePath;
        try
        {
            Log.FilePath = Path.Combine(dir, "altspacy.log");
            Log.Write("hello", new InvalidOperationException("boom"));
            Assert.Contains("hello | InvalidOperationException: boom", File.ReadAllText(Log.FilePath));
            File.WriteAllText(Log.FilePath, new string('x', 600 * 1024));
            Log.Write("after");
            Assert.True(File.Exists(Log.FilePath + ".1"));
            Assert.DoesNotContain("xxx", File.ReadAllText(Log.FilePath));
        }
        finally
        {
            Log.FilePath = original;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Start_with_windows_writes_and_removes_run_entry()
    {
        var key = $@"Software\AltSpacy.Tests\Run-{Guid.NewGuid():N}";
        var startup = new StartupRegistration(key);
        try
        {
            Assert.False(startup.IsEnabled);
            startup.Set(true, @"C:\Apps\AltSpacy.exe");
            Assert.True(startup.IsEnabled);
            Assert.Equal("\"C:\\Apps\\AltSpacy.exe\" --background", startup.Command);
            startup.Set(false, @"C:\Apps\AltSpacy.exe");
            Assert.False(startup.IsEnabled);
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\AltSpacy.Tests", throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Start_with_windows_moves_over_from_the_old_Omnibar_entry()
    {
        var key = $@"Software\AltSpacy.Tests\Run-{Guid.NewGuid():N}";
        var startup = new StartupRegistration(key);
        var old = new StartupRegistration(key, "Omnibar");
        try
        {
            Assert.False(startup.TakeOverFrom("Omnibar", @"C:\Apps\AltSpacy.exe"));
            Assert.False(startup.IsEnabled);

            old.Set(true, @"C:\Old\Omnibar.exe");
            Assert.True(startup.TakeOverFrom("Omnibar", @"C:\Apps\AltSpacy.exe"));
            Assert.False(old.IsEnabled);
            Assert.Equal(StartupRegistration.CommandFor(@"C:\Apps\AltSpacy.exe"), startup.Command);
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\AltSpacy.Tests", throwOnMissingSubKey: false); }
    }

    [Theory]
    [InlineData("""{ "shortcuts": [ { "key": "a", "url": "file:///c:/x" } ] }""", "http")]
    [InlineData("""{ "shortcuts": [ { "key": "a b", "url": "https://x/{q}" } ] }""", "Invalid key")]
    [InlineData("""{ "shortcuts": [ { "key": "a", "url": "https://x/{q}" }, { "key": "A", "url": "https://y/{q}" } ] }""", "Duplicate")]
    [InlineData("""{ "hotkey": "Space", "shortcuts": [] }""", "Invalid hotkey")]
    public void Rejects_invalid_config(string json, string message) =>
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => ConfigStore.Parse(json)).Message);

    [Fact]
    public void Save_round_trips_and_omits_computed_and_null_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"altspacy-{Guid.NewGuid():N}.json");
        var config = new AltSpacyConfig
        {
            Hotkey = "Ctrl+Alt+Space",
            Browser = "",
            Shortcuts = [new() { Key = "j", Url = "https://jira.example.com/browse/{q}", PrefixIfNumeric = "CML-" }],
        };
        try
        {
            ConfigStore.Save(path, config);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("hasPlaceholder", json);
            Assert.DoesNotContain("description", json);
            Assert.Contains("\"prefixIfNumeric\": \"CML-\"", json);

            var loaded = ConfigStore.Load(path);
            Assert.Equal("Ctrl+Alt+Space", loaded.Hotkey);
            Assert.Equal("", loaded.Browser);
            Assert.Equal("https://jira.example.com/browse/CML-7", ShortcutResolver.Resolve("j:7", loaded.Shortcuts)?.Url);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_rejects_invalid_config_without_writing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"altspacy-{Guid.NewGuid():N}.json");
        var config = new AltSpacyConfig { Shortcuts = [new() { Key = "j", Url = "ftp://x" }] };
        Assert.Throws<InvalidDataException>(() => ConfigStore.Save(path, config));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Load_creates_file_from_default_when_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"altspacy-{Guid.NewGuid():N}.json");
        try
        {
            Assert.NotEmpty(ConfigStore.Load(path).Shortcuts);
            Assert.Equal(ConfigStore.ReadDefault(), File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}

public class HotkeyBindingTests
{
    [Theory]
    [InlineData("Alt+Space", HotkeyBinding.ModAlt, 0x20u)]
    [InlineData("ctrl + shift + k", HotkeyBinding.ModControl | HotkeyBinding.ModShift, 0x4Bu)]
    [InlineData("Win+Alt+1", HotkeyBinding.ModWin | HotkeyBinding.ModAlt, 0x31u)]
    [InlineData("Ctrl+F1", HotkeyBinding.ModControl, 0x70u)]
    public void Parses_hotkeys(string text, uint modifiers, uint vk) =>
        Assert.Equal(new HotkeyBinding(modifiers, vk), HotkeyBinding.Parse(text));

    [Theory]
    [InlineData("Space")]
    [InlineData("Alt")]
    [InlineData("Alt+Banana")]
    [InlineData("Alt+A+B")]
    [InlineData("Alt+123")]
    public void Rejects_invalid_hotkeys(string text) =>
        Assert.Throws<FormatException>(() => HotkeyBinding.Parse(text));

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.Space, "Ctrl+Alt+Space")]
    [InlineData(ModifierKeys.Alt, Key.D1, "Alt+1")]
    [InlineData(ModifierKeys.Windows | ModifierKeys.Shift, Key.K, "Shift+Win+K")]
    public void Formats_captured_keys_in_parseable_form(ModifierKeys modifiers, Key key, string expected)
    {
        Assert.Equal(expected, HotkeyBinding.Format(modifiers, key));
        HotkeyBinding.Parse(expected);
    }

    [Theory]
    [InlineData(ModifierKeys.None, Key.Space)]
    [InlineData(ModifierKeys.Control, Key.LeftCtrl)]
    [InlineData(ModifierKeys.Alt, Key.System)]
    public void Ignores_incomplete_combinations(ModifierKeys modifiers, Key key) =>
        Assert.Null(HotkeyBinding.Format(modifiers, key));
}
