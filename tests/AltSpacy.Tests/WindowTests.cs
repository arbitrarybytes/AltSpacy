using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace AltSpacy.Tests;

public class AboutWindowTests
{
    [Fact]
    public void Shows_version_and_hotkey() => Ui.Run(() =>
    {
        var window = new AboutWindow("Alt+Space");
        Ui.Show(window);
        Assert.Equal($"Version {AppInfo.Version}", window.VersionText.Text);
        Assert.Equal("Copyright © 2026 Vaibhav Sharma", window.CopyrightText.Text);
        Assert.Equal(AppInfo.LicenseUrl, window.LicenseLink.NavigateUri.ToString());
        Assert.Contains("Alt+Space", window.HotkeyText.Text);
        Assert.Equal(["Alt", "Space"], window.HotkeyKeys.Items.Cast<string>());
        window.Close();
    });

    [Fact]
    public void Ok_closes() => Ui.Run(() =>
    {
        var window = new AboutWindow("Alt+Space");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Ui.Show(window);
        Ui.Click(Ui.Button(window, "OK"));
        Assert.True(closed);
    });

    [Fact]
    public void Escape_closes() => Ui.Run(() =>
    {
        var window = new AboutWindow("Alt+Space");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Ui.Show(window);
        Ui.Press(window, Key.Escape);
        Assert.True(closed);
    });
}

public sealed class SettingsWindowTests : IDisposable
{
    private static readonly BrowserInfo Edge = new("Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");
    private static readonly BrowserInfo Chrome = new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"altspacy-{Guid.NewGuid():N}.json");
    private readonly string _iconDir = Path.Combine(Path.GetTempPath(), $"altspacy-icons-{Guid.NewGuid():N}");
    private readonly List<bool> _suspendCalls = [];

    public void Dispose()
    {
        File.Delete(_path);
        if (Directory.Exists(_iconDir)) Directory.Delete(_iconDir, true);
    }

    private static int DefaultCount => TestConfig.Load().Shortcuts.Count;

    private SettingsWindow Open(string? browser = "msedge")
    {
        var config = TestConfig.Load();
        config.Browser = browser;
        var window = new SettingsWindow(config, _path, _suspendCalls.Add, new IconService(_iconDir, new NotFound()), [Chrome, Edge], "Microsoft Edge",
            Startup(), @"C:\Apps\AltSpacy.exe");
        Ui.Show(window);
        return window;
    }

    private readonly string _runKey = $@"Software\AltSpacy.Tests\Run-{Guid.NewGuid():N}";
    private StartupRegistration Startup() => new(_runKey);

    [Fact]
    public void Start_with_windows_switch_registers_on_save() => Ui.Run(() =>
    {
        try
        {
            var window = Open();
            Assert.False(window.StartWithWindowsSwitch.IsChecked);
            window.StartWithWindowsSwitch.IsChecked = true;
            Ui.Click(Ui.Button(window, "Save"));
            Assert.Equal("\"C:\\Apps\\AltSpacy.exe\" --background", Startup().Command);

            var again = Open();
            Assert.True(again.StartWithWindowsSwitch.IsChecked);
            again.StartWithWindowsSwitch.IsChecked = false;
            Ui.Click(Ui.Button(again, "Save"));
            Assert.False(Startup().IsEnabled);
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\AltSpacy.Tests", throwOnMissingSubKey: false); }
    });

    [Fact]
    public void Save_keeps_projects_and_default_projects() => Ui.Run(() =>
    {
        var window = Open();
        Ui.Click(Ui.Button(window, "Save"));
        var saved = ConfigStore.Load(_path);
        Assert.Equal(TestConfig.Ire, saved.Projects["ire"]);
        Assert.Equal("ire", saved.Shortcuts.Single(s => s.Key == "mr").DefaultProject);
        Assert.StartsWith(ConfigStore.Header, File.ReadAllText(_path));
    });

    private static SettingsWindow WithClosedFlag(SettingsWindow window, out Func<bool> closed)
    {
        var isClosed = false;
        window.Closed += (_, _) => isClosed = true;
        closed = () => isClosed;
        return window;
    }

    [Fact]
    public void Opens_with_current_config_grouped_by_app() => Ui.Run(() =>
    {
        var window = Open();
        Assert.Equal("Alt+Space", window.Hotkey);
        Assert.Equal(["Alt", "Space"], window.HotkeyKeys.Items.Cast<string>());
        Assert.False(window.DefaultBrowserSwitch.IsChecked);
        Assert.Same(Edge, window.BrowserList.SelectedItem);
        Assert.Equal("Currently Microsoft Edge", window.DefaultBrowserName.Text);
        Assert.Equal(DefaultCount, window.ShortcutGrid.Items.Count);

        var groups = ((ListCollectionView)window.ShortcutGrid.ItemsSource).Groups!.Cast<CollectionViewGroup>()
            .ToDictionary(g => (string)g.Name, g => g.Items.Cast<Shortcut>().Select(s => s.Key).ToArray());
        Assert.Equal(["j", "js"], groups["Jira"]);
        Assert.Equal(["gl", "mr", "gc", "gb", "pl"], groups["GitLab"]);
        Assert.Equal(["gh"], groups["GitHub"]);
        Assert.Equal(["c"], groups["Confluence"]);
        Assert.Equal(["sp", "snp"], groups["Splunk"]);

        Assert.Equal("https://jira.example.com/browse/CML-41", window.TestResult.Text);
        Assert.Null(window.ShortcutGrid.SelectedItem);
        Assert.True(window.Height <= SystemParameters.WorkArea.Height);
        window.Close();
    });

    [Fact]
    public void Try_it_follows_input() => Ui.Run(() =>
    {
        var window = Open();
        window.TestBox.Text = "gh dotnet/runtime";
        Assert.Equal("https://github.com/search?q=dotnet%2Fruntime", window.TestResult.Text);
        window.TestBox.Text = "zz 1";
        Assert.StartsWith("No match", window.TestResult.Text);
        Assert.False(window.OpenTestButton.IsEnabled);
        window.Close();
    });

    [Fact]
    public void Hotkey_click_records_and_escape_cancels_without_closing() => Ui.Run(() =>
    {
        var window = WithClosedFlag(Open(), out var closed);
        Ui.Click(window.HotkeyButton);
        Assert.Equal(Visibility.Visible, window.HotkeyPrompt.Visibility);
        Assert.Equal("Recording", window.HotkeyButton.Tag);
        Assert.Equal([true], _suspendCalls);

        Ui.Press(window.HotkeyButton, Key.Escape);
        Assert.Equal(Visibility.Collapsed, window.HotkeyPrompt.Visibility);
        Assert.Equal("Alt+Space", window.Hotkey);
        Assert.Equal([true, false], _suspendCalls);
        Assert.False(closed());
        window.Close();
    });

    [Fact]
    public void Captured_combination_becomes_the_hotkey() => Ui.Run(() =>
    {
        var window = Open();
        Ui.Click(window.HotkeyButton);
        window.CaptureHotkey(ModifierKeys.None, Key.K);
        Assert.Equal("Alt+Space", window.Hotkey);
        window.CaptureHotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.K);
        Assert.Equal("Ctrl+Shift+K", window.Hotkey);
        Assert.Equal(["Ctrl", "Shift", "K"], window.HotkeyKeys.Items.Cast<string>());
        Assert.Equal(Visibility.Collapsed, window.HotkeyPrompt.Visibility);
        window.Close();
    });

    [Fact]
    public void Default_browser_switch_saves_empty_browser() => Ui.Run(() =>
    {
        var window = WithClosedFlag(Open(), out var closed);
        window.DefaultBrowserSwitch.IsChecked = true;
        Assert.False(window.BrowserList.IsEnabled);
        Ui.Click(Ui.Button(window, "Save"));
        Assert.True(closed(), window.ErrorText.Text);
        Assert.Equal("", ConfigStore.Load(_path).Browser);
    });

    [Fact]
    public void Picking_a_browser_saves_its_path() => Ui.Run(() =>
    {
        var window = Open(browser: "");
        Assert.True(window.DefaultBrowserSwitch.IsChecked);
        window.DefaultBrowserSwitch.IsChecked = false;
        window.BrowserList.SelectedItem = Chrome;
        Ui.Click(Ui.Button(window, "Save"));
        Assert.Equal(Chrome.Path, ConfigStore.Load(_path).Browser);
    });

    [Fact]
    public void Unknown_configured_browser_is_kept_as_custom_entry() => Ui.Run(() =>
    {
        var window = Open(browser: @"D:\Tools\Thorium\thorium.exe");
        var selected = Assert.IsType<BrowserInfo>(window.BrowserList.SelectedItem);
        Assert.Equal("thorium", selected.Name);
        Assert.Equal(3, window.BrowserList.Items.Count);
        window.Close();
    });

    [Fact]
    public void Cancel_closes_without_saving() => Ui.Run(() =>
    {
        var window = WithClosedFlag(Open(), out var closed);
        Ui.Click(Ui.Button(window, "Cancel"));
        Assert.True(closed());
        Assert.False(File.Exists(_path));
        Assert.Equal([false], _suspendCalls);
    });

    [Fact]
    public void Escape_closes() => Ui.Run(() =>
    {
        var window = WithClosedFlag(Open(), out var closed);
        Ui.Press(window, Key.Escape);
        Assert.True(closed());
    });

    [Fact]
    public void Save_writes_config_and_closes() => Ui.Run(() =>
    {
        var window = WithClosedFlag(Open(), out var closed);
        window.CaptureHotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.K);
        Ui.Click(Ui.Button(window, "Save"));

        Assert.True(closed(), window.ErrorText.Text);
        var saved = ConfigStore.Load(_path);
        Assert.Equal("Ctrl+Alt+K", saved.Hotkey);
        Assert.Equal(Edge.Path, saved.Browser);
        Assert.Equal(DefaultCount, saved.Shortcuts.Count);
    });

    [Fact]
    public void Add_then_save_new_shortcut() => Ui.Run(() =>
    {
        var window = Open();
        Ui.Click(Ui.Button(window, "+ Add"));
        Assert.Equal(DefaultCount + 1, window.ShortcutGrid.Items.Count);

        window.ShortcutGrid.CommitEdit();
        var added = window.ShortcutGrid.Items.Cast<Shortcut>().Single(s => s.Key == "");
        added.Key = "sn";
        added.Url = "https://example.service-now.com/nav_to.do?uri=incident.do?sysparm_query=number={q}";
        Ui.Click(Ui.Button(window, "Save"));

        Assert.Contains(ConfigStore.Load(_path).Shortcuts, s => s.Key == "sn");
    });

    [Fact]
    public void Remove_deletes_selected_rows() => Ui.Run(() =>
    {
        var window = Open();
        window.ShortcutGrid.SelectedIndex = 0;
        Ui.Click(Ui.Button(window, "Remove"));
        Assert.Equal(DefaultCount - 1, window.ShortcutGrid.Items.Count);
        window.Close();
    });

    [Fact]
    public void Blank_new_row_is_ignored_on_save() => Ui.Run(() =>
    {
        var window = Open();
        Ui.Click(Ui.Button(window, "+ Add"));
        Ui.Click(Ui.Button(window, "Save"));
        Assert.Equal(DefaultCount, ConfigStore.Load(_path).Shortcuts.Count);
    });

    [Fact]
    public void Invalid_save_shows_error_and_stays_open() => Ui.Run(() =>
    {
        var window = WithClosedFlag(Open(), out var closed);
        window.ShortcutGrid.Items.Cast<Shortcut>().First().Url = "ftp://nope";
        Ui.Click(Ui.Button(window, "Save"));

        Assert.False(closed());
        Assert.Contains("http", window.ErrorText.Text);
        Assert.False(File.Exists(_path));
        window.Close();
    });

    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
