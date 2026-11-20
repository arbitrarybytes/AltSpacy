using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace AltSpacy.Tests;

public sealed class MainWindowTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"altspacy-icons-{Guid.NewGuid():N}");
    private readonly List<string> _commands = [];
    private readonly List<string> _opened = [];
    private readonly List<string> _ran = [];

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private MainWindow Open()
    {
        var config = TestConfig.Load();
        var icons = new IconService(_cacheDir, new NotFoundHandler());
        var gray = IconArt.Glyph("\uE713", Colors.Gray);
        var actions = new LauncherAction[]
        {
            new() { Title = () => "Switch to dark mode", Detail = () => "Test", Keywords = ["dark", "theme"], Icon = () => gray,
                    Run = () => _ran.Add("theme"), Glyphs = [SystemActions.Moon] },
            new() { Title = () => "Windows Terminal", Detail = () => "wt.exe", Keywords = ["wt", "terminal"], Icon = () => gray, Badge = "wt",
                    Group = RunCatalog.Apps, Run = () => _ran.Add("wt"), RunElevated = () => _ran.Add("wt (admin)") },
            new() { Title = () => "WordPad", Detail = () => "Not installed on this PC", Keywords = ["wordpad"], Icon = () => gray, Badge = "wordpad",
                    Group = RunCatalog.Apps, Available = false, Run = () => throw new InvalidOperationException("WordPad isn't installed on this PC.") },
        };
        var window = new MainWindow(() => config, icons, actions, _commands.Add, Services());
        Ui.Show(window);
        return window;
    }

    private readonly List<string> _launched = [];
    private readonly List<string> _teamsSearches = [];
    private readonly List<string> _copied = [];
    private string? _clipboard;
    private readonly History _history = new(null);

    private BarServices Services() => new()
    {
        OpenUrl = (url, _) => _opened.Add(url),
        Launch = _launched.Add,
        SearchTeams = _teamsSearches.Add,
        CopyText = _copied.Add,
        ReadClipboard = () => _clipboard,
        History = _history,
    };

    private MainWindow OpenWith(IReadOnlyList<LauncherAction> actions)
    {
        var window = new MainWindow(() => TestConfig.Load(), new IconService(_cacheDir, new NotFoundHandler()), actions, _commands.Add, Services());
        Ui.Show(window);
        return window;
    }

    [Fact]
    public void Bar_has_no_title_bar() => Ui.Run(() =>
    {
        // With a caption, DWM paints a title-bar strip (tinted by "Show accent color on title bars") across the top of
        // the extended acrylic frame.
        var window = Open();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        const long wsCaption = 0x00C00000;
        Assert.Equal(0, GetWindowLongPtr(hwnd, -16).ToInt64() & wsCaption);
        window.Close();
    });

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [Fact]
    public void Escape_clears_text_first_then_hides() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "reg");
        Ui.Press(window.Input, Key.Escape);
        Assert.Equal("", window.Input.Text);
        Assert.True(window.IsVisible);
        Ui.Press(window.Input, Key.Escape);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Colon_lists_altspacy_commands_and_enter_runs_the_selected_one() => Ui.Run(() =>
    {
        var window = OpenWith(SystemActions.Create(_commands.Add));
        Type(window, ":");
        Assert.Equal(["AltSpacy settings", "About AltSpacy", "Reload AltSpacy config", "Edit AltSpacy config file", "Exit AltSpacy"], window.Items.Select(i => i.Title));
        Type(window, ":ab");
        Assert.Equal("About AltSpacy", window.Items.Single().Title);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["about"], _commands);
    });

    [Fact]
    public void Calculator_shows_result_and_enter_copies_it() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "1200*1.18");
        var calc = window.Items[0];
        Assert.Equal(MainWindow.CalculatorGroup, calc.Group);
        Assert.Equal("= 1,416", calc.Title);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["1416"], _copied);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Email_offers_teams_chat_and_mail() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "jane.doe@example.com");
        Assert.Equal(["Chat with jane.doe@example.com in Teams", "Email jane.doe@example.com"], window.Items.Select(i => i.Title));
        Assert.DoesNotContain(window.Items, i => i.Group == SystemActions.RunGroup);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["msteams:/l/chat/0/0?users=jane.doe%40example.com"], _launched);
    });

    [Fact]
    public void Email_row_opens_mail_app() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "someone@example.com");
        Ui.Press(window.Input, Key.Down);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["mailto:someone@example.com"], _launched);
    });

    [Fact]
    public void T_name_searches_teams() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "t Vaibhav Sharma");
        var row = window.Items.Single();
        Assert.Equal("Search Teams for \u201CVaibhav Sharma\u201D", row.Title);
        Assert.Equal(MainWindow.PeopleGroup, row.Group);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["Vaibhav Sharma"], _teamsSearches);
    });

    [Fact]
    public void T_email_chats_directly_or_searches() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "t jane.doe@example.com");
        Assert.Equal(["Chat with jane.doe@example.com in Teams", "Search Teams for \u201Cjane.doe@example.com\u201D"], window.Items.Select(i => i.Title));
        window.Close();
    });

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("www.example.com/support", "https://www.example.com/support")]
    [InlineData("https://github.com/dotnet/runtime", "https://github.com/dotnet/runtime")]
    public void Web_addresses_open_in_the_configured_browser(string typed, string url) => Ui.Run(() =>
    {
        var window = Open();
        Type(window, typed);
        Assert.Equal(MainWindow.WebGroup, window.Items[0].Group);
        Assert.Equal($"Open {typed}", window.Items[0].Title);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal([url], _opened);
    });

    [Fact]
    public void Clipboard_jira_key_is_offered_first() => Ui.Run(() =>
    {
        _clipboard = "CML-123";
        var window = Open();
        Type(window, "x");
        Type(window, "");
        var first = window.Items[0];
        Assert.Equal(MainWindow.ClipboardGroup, first.Group);
        Assert.Equal("Open CML-123", first.Title);
        Assert.Equal("https://jira.example.com/browse/CML-123", first.Detail);
        ClickRow(window, i => i.Group == MainWindow.ClipboardGroup);
        Assert.Equal(["https://jira.example.com/browse/CML-123"], _opened);
    });

    [Theory]
    [InlineData("https://gitlab.example.com/x/-/merge_requests/5", "Open copied link")]
    [InlineData("someone@example.com", "Chat with someone@example.com in Teams")]
    public void Clipboard_link_or_email_is_offered(string clipboard, string title) => Ui.Run(() =>
    {
        _clipboard = clipboard;
        var window = Open();
        Type(window, "x");
        Type(window, "");
        Assert.Equal(title, window.Items[0].Title);
        window.Close();
    });

    [Fact]
    public void Multi_line_clipboard_is_ignored() => Ui.Run(() =>
    {
        _clipboard = "CML-1\nCML-2";
        var window = Open();
        Type(window, "x");
        Type(window, "");
        Assert.DoesNotContain(window.Items, i => i.Group == MainWindow.ClipboardGroup);
        window.Close();
    });

    [Fact]
    public void Recently_used_items_appear_first_and_rerun() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "j CML-7");
        Ui.Press(window.Input, Key.Enter);
        window.Show();
        Type(window, "wt");
        Ui.Press(window.Input, Key.Enter);
        window.Show();
        Type(window, "wt");
        Ui.Press(window.Input, Key.Enter);
        window.Show();

        Type(window, "x");
        Type(window, "");
        var recent = window.Items.Where(i => i.Group == MainWindow.RecentGroup).ToList();
        Assert.Equal(["Windows Terminal", "j CML-7"], recent.Select(i => i.Title));
        Assert.DoesNotContain(window.Items, i => i.Group == MainWindow.SuggestedGroup && i.Title == "Windows Terminal");

        ClickRow(window, i => i.Title == "j CML-7");
        Assert.Equal(["https://jira.example.com/browse/CML-7", "https://jira.example.com/browse/CML-7"], _opened);
    });

    [Fact]
    public void Shortcuts_you_use_rank_first() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "s");
        Assert.Equal(["sp", "snp"], window.Items.Select(i => i.Badge));
        _history.Record(History.ShortcutKey, "snp");
        Type(window, "x");
        Type(window, "s");
        Assert.Equal(["snp", "sp"], window.Items.Select(i => i.Badge));
        window.Close();
    });

    [Fact]
    public void Project_alias_and_default_project_both_resolve() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "mr ire 1190");
        Assert.EndsWith("/routing-service/-/merge_requests/1190", window.Preview.Text);
        Type(window, "gb feature/login");
        Assert.Equal($"https://gitlab.example.com/{TestConfig.Ire}/-/tree/feature%2Flogin", window.Preview.Text);
        window.Close();
    });

    private static string[] Groups(MainWindow window) => window.Items.Select(i => i.Group).Distinct().ToArray();

    private static object? ShownIcon(MainWindow window) =>
        BindingOperations.GetBinding(window.LogoImage, Image.SourceProperty)?.Source;

    private static void Type(MainWindow window, string text)
    {
        window.Input.Text = text;
        Ui.Flush();
    }

    [Fact]
    public void Empty_input_shows_logo_shortcuts_by_app_then_suggestions() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "x");
        Type(window, "");
        Assert.Equal(["Jira", "GitLab", "GitHub", "Confluence", "Splunk", "Suggested"], Groups(window));
        Assert.Equal(["Switch to dark mode", "Windows Terminal"], window.Items.Where(i => i.Group == "Suggested").Select(i => i.Title));
        Assert.Equal(-1, window.Suggestions.SelectedIndex);
        Assert.Same(Logo.Image, ((IconHandle)ShownIcon(window)!).Image);
        Assert.Equal(System.Windows.Visibility.Visible, window.Placeholder.Visibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, window.PreviewChip.Visibility);
        window.Close();
    });

    [Fact]
    public void Shortcuts_are_grouped_by_app_and_chip_names_the_app() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "g");
        Assert.Equal(["GitLab", "GitHub"], Groups(window));
        Assert.Equal("GitLab", window.AppChipText.Text);
        Type(window, "gh dotnet/runtime");
        Assert.Equal("GitHub", window.AppChipText.Text);
        Assert.Equal("https://github.com/search?q=dotnet%2Fruntime", window.Preview.Text);
        window.Close();
    });

    [Fact]
    public void Unknown_commands_get_a_run_row_like_win_r() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "ping 127.0.0.1");
        var run = window.Items.Single();
        Assert.Equal(SystemActions.RunGroup, run.Group);
        Assert.Equal("Run ping 127.0.0.1", run.Title);
        Assert.EndsWith("PING.EXE", run.Detail, StringComparison.OrdinalIgnoreCase);
        window.Close();
    });

    [Fact]
    public void Drive_paths_open_folders_instead_of_confluence() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, @"c:\Windows");
        Assert.DoesNotContain(window.Items, i => i.Shortcut is not null);
        Assert.Equal(@"Open c:\Windows", window.Items.Single().Title);
        window.Close();
    });

    [Fact]
    public void Ctrl_shift_enter_runs_as_admin() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "wt");
        window.Submit(elevated: true);
        Assert.Equal(["wt (admin)"], _ran);
    });

    [Fact]
    public void Missing_tool_explains_instead_of_running() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "wor");
        Assert.Empty(window.Items);
        Type(window, "wordpad");
        Assert.Equal("Not installed on this PC", window.Items.Single().Detail);
        Assert.Equal("Not installed on this PC", window.Preview.Text);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal("WordPad isn't installed on this PC.", window.Preview.Text);
        Assert.True(window.IsVisible);
        window.Close();
    });

    [Fact]
    public void Typing_a_key_shows_that_apps_icon() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "j CML-41");
        Assert.Equal("j", window.Items[0].Shortcut!.Key);
        Assert.Same(window.Items[0].Icon, ShownIcon(window));
        Assert.Equal("https://jira.example.com/browse/CML-41", window.Preview.Text);

        Type(window, "sp");
        Assert.Equal("splunksearch.example.com", window.Items[0].Detail);
        Assert.Same(window.Items[0].Icon, ShownIcon(window));
        window.Close();
    });

    [Fact]
    public void Moving_selection_switches_icon() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "s");
        Assert.Equal(["sp", "snp"], window.Items.Take(2).Select(i => i.Badge));
        Ui.Press(window.Input, Key.Down);
        Assert.Same(window.Items[1].Icon, ShownIcon(window));
        window.Close();
    });

    [Fact]
    public void Enter_opens_resolved_url() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "mr 1190");
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["https://gitlab.example.com/my-group/platform/routing-service/-/merge_requests/1190"], _opened);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Enter_on_key_without_value_completes_instead_of_opening() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "j");
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal("j ", window.Input.Text);
        Assert.Empty(_opened);
        window.Close();
    });

    [Fact]
    public void Tab_completes_with_space() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "sn");
        Ui.Press(window.Input, Key.Tab);
        Assert.Equal("snp ", window.Input.Text);
        window.Close();
    });

    [Fact]
    public void Typing_an_action_name_lists_it_and_enter_runs_it() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "dark");
        Assert.Equal("Switch to dark mode", window.Items[0].Title);
        Assert.Same(window.Items[0].Icon, ShownIcon(window));
        Assert.Equal(System.Windows.Visibility.Collapsed, window.PreviewChip.Visibility);
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["theme"], _ran);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Shortcuts_rank_above_actions() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "wt");
        Assert.Equal("Windows Terminal", window.Items.Single().Title);
        Type(window, "s");
        Assert.All(window.Items, i => Assert.NotNull(i.Shortcut));
        Type(window, "js");
        Assert.Equal("js", window.Items[0].Badge);
        window.Close();
    });

    [Fact]
    public void Colon_commands_still_work() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, ":about");
        Ui.Press(window.Input, Key.Enter);
        Assert.Equal(["about"], _commands);
    });

    private static void ClickRow(MainWindow window, Func<LauncherItem, bool> match)
    {
        var item = window.Items.First(match);
        window.Suggestions.ScrollIntoView(item);
        Ui.Flush();
        var row = (System.Windows.Controls.ListBoxItem)window.Suggestions.ItemContainerGenerator.ContainerFromItem(item);
        Assert.NotNull(row);
        Ui.MouseClick(row);
    }

    [Fact]
    public void Clicking_a_shortcut_puts_its_key_in_the_bar() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "x");
        Type(window, "");
        ClickRow(window, i => i.Badge == "gh");
        Assert.Equal("gh ", window.Input.Text);
        Assert.Equal(window.Input.Text.Length, window.Input.CaretIndex);
        Assert.Empty(_opened);
        Assert.True(window.IsVisible);
        window.Close();
    });

    [Fact]
    public void Clicking_the_resolved_shortcut_opens_it() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "j CML-41");
        ClickRow(window, i => i.Badge == "j");
        Assert.Equal(["https://jira.example.com/browse/CML-41"], _opened);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Clicking_a_shortcut_without_placeholder_opens_it() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "pl");
        ClickRow(window, i => i.Badge == "pl");
        Assert.Equal(["https://gitlab.example.com/my-group/platform/routing-service/-/pipelines"], _opened);
    });

    [Fact]
    public void Clicking_an_action_runs_it() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "x");
        Type(window, "");
        ClickRow(window, i => i.Title == "Windows Terminal");
        Assert.Equal(["wt"], _ran);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Clicking_a_different_shortcut_replaces_the_key_and_keeps_the_value() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "s");
        ClickRow(window, i => i.Badge == "snp");
        Assert.Equal("snp ", window.Input.Text);
        window.Close();
    });

    [Fact]
    public void Same_action_keeps_its_icon_while_typing_so_it_does_not_pop_on_every_key() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "Wind");
        var first = window.Items[0].Icon;
        Type(window, "Windo");
        Assert.Same(first, window.Items[0].Icon);
        Assert.Same(first, ShownIcon(window));
        window.Close();
    });

    [Fact]
    public void Rows_expose_title_and_detail_to_screen_readers() => Ui.Run(() =>
    {
        var window = Open();
        Type(window, "j CML-41");
        var row = (System.Windows.Controls.ListBoxItem)window.Suggestions.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.Equal("Jira issue", System.Windows.Automation.AutomationProperties.GetName(row));
        Assert.Equal("jira.example.com", System.Windows.Automation.AutomationProperties.GetHelpText(row));
        window.Close();
    });

    [Fact]
    public void Border_and_glow_share_the_brand_gradient() => Ui.Run(() =>
    {
        var window = Open();
        var border = Assert.IsType<LinearGradientBrush>(window.Shell.BorderBrush);
        Assert.Same(border, window.GlowRing.BorderBrush);
        Assert.Equal(Color.FromRgb(0xFF, 0x6A, 0x3D), border.GradientStops[0].Color);
        window.Close();
    });

    [Theory]
    [InlineData("settings")]
    [InlineData("about")]
    [InlineData("reload")]
    [InlineData("exit")]
    public void Footer_commands_run_and_hide(string command) => Ui.Run(() =>
    {
        var window = Open();
        var button = Ui.Find<System.Windows.Controls.Button>(window, b => Equals(b.Tag, command));
        Assert.NotNull(button);
        Ui.Click(button);
        Assert.Equal([command], _commands);
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void Escape_hides() => Ui.Run(() =>
    {
        var window = Open();
        Ui.Press(window.Input, Key.Escape);
        Assert.False(window.IsVisible);
        window.Close();
    });

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
