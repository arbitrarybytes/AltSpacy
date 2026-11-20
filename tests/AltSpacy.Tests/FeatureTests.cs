using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AltSpacy.Tests;

public class ProjectShortcutTests
{
    private static readonly AltSpacyConfig Config = TestConfig.Load();
    private static readonly Dictionary<string, string> NoProjects = [];
    private static readonly Shortcut GenericMr = new() { Key = "mr", Url = "https://gitlab.example.com/{project}/-/merge_requests/{q}" };
    private static readonly Shortcut GenericPl = new() { Key = "pl", Url = "https://gitlab.example.com/{project}/-/pipelines" };

    private static string? Url(string input) => ShortcutResolver.Resolve(input, Config.Shortcuts, Config.Projects)?.Url;

    [Theory]
    [InlineData("mr 1190", "/-/merge_requests/1190")]
    [InlineData("mr ire 1190", "/-/merge_requests/1190")]
    [InlineData("MR IRE 1190", "/-/merge_requests/1190")]
    [InlineData("pl", "/-/pipelines")]
    [InlineData("pl ire", "/-/pipelines")]
    [InlineData("gb feature/login", "/-/tree/feature%2Flogin")]
    [InlineData("gc ire 3f2a1b", "/-/commit/3f2a1b")]
    public void Alias_or_default_project_fills_project(string input, string suffix) =>
        Assert.Equal($"https://gitlab.example.com/{TestConfig.Ire}{suffix}", Url(input));

    [Theory]
    [InlineData("mr ire")]
    [InlineData("mr")]
    public void Incomplete_project_shortcuts_do_not_resolve(string input) => Assert.Null(Url(input));

    [Fact]
    public void Without_default_project_a_group_path_is_the_project()
    {
        Assert.Equal("https://gitlab.example.com/my-group/my-app/-/merge_requests/42", ShortcutResolver.Resolve("mr my-group/my-app 42", [GenericMr], NoProjects)?.Url);
        Assert.Equal("https://gitlab.example.com/my-group/my-app/-/pipelines", ShortcutResolver.Resolve("pl my-group/my-app", [GenericPl], NoProjects)?.Url);
        Assert.Null(ShortcutResolver.Resolve("mr 42", [GenericMr], NoProjects));
        Assert.Null(ShortcutResolver.Resolve("pl", [GenericPl], NoProjects));
    }

    [Fact]
    public void Project_shortcuts_still_group_by_app() => Assert.Equal("GitLab", GenericMr.App);
}

public class CalculatorTests
{
    [Theory]
    [InlineData("2+2", 4)]
    [InlineData("1200*1.18", 1416)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^10", 1024)]
    [InlineData("10 % 3", 1)]
    [InlineData("-5+2", -3)]
    [InlineData("3x4", 12)]
    [InlineData("=7/2", 3.5)]
    [InlineData("1,5*2", 3)]
    public void Evaluates(string text, double expected) => Assert.Equal(expected, Calculator.TryEvaluate(text)!.Value, 9);

    [Theory]
    [InlineData("1190")]
    [InlineData("mr 1190")]
    [InlineData("j CML-41")]
    [InlineData("2+")]
    [InlineData("(1+2")]
    [InlineData("1/0")]
    [InlineData("max")]
    [InlineData("-")]
    public void Ignores_non_arithmetic(string text) => Assert.Null(Calculator.TryEvaluate(text));

    [Fact]
    public void Formats_for_display_and_copying()
    {
        Assert.Equal("1,416", Calculator.Format(1416));
        Assert.Equal("1416", Calculator.Plain(1416));
        Assert.Equal("0.3333333333", Calculator.Format(1.0 / 3));
    }
}

public class SmartInputTests
{
    [Theory]
    [InlineData("jane.doe@example.com", true)]
    [InlineData("a@b.co", true)]
    [InlineData("not an email", false)]
    [InlineData("user@localhost", false)]
    [InlineData("@example.com", false)]
    public void Detects_emails(string text, bool expected) => Assert.Equal(expected, SmartInput.IsEmail(text));

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("www.example.org", "https://www.example.org")]
    [InlineData("github.com/dotnet/runtime", "https://github.com/dotnet/runtime")]
    [InlineData("http://intranet.local:8080/x", "http://intranet.local:8080/x")]
    [InlineData("https://jira.example.com/browse/CML-1", "https://jira.example.com/browse/CML-1")]
    public void Detects_web_addresses(string text, string url) => Assert.Equal(url, SmartInput.WebUrl(text));

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("services.msc")]
    [InlineData("appwiz.cpl")]
    [InlineData("j CML-41")]
    [InlineData("1.5")]
    [InlineData("someone@example.com")]
    public void Leaves_commands_and_numbers_alone(string text) => Assert.Null(SmartInput.WebUrl(text));

    [Theory]
    [InlineData("CML-123", true)]
    [InlineData("ASRC24-9", true)]
    [InlineData("cml-123", false)]
    [InlineData("CML 123", false)]
    public void Detects_jira_keys(string text, bool expected) => Assert.Equal(expected, SmartInput.IsJiraKey(text));

    [Fact]
    public void Builds_mail_and_teams_links()
    {
        Assert.Equal("mailto:a@example.com", SmartInput.MailTo(" a@example.com "));
        Assert.Equal("msteams:/l/chat/0/0?users=a%2Bb%40example.com", SmartInput.TeamsChat("a+b@example.com"));
    }

    [Fact]
    public void Escapes_sendkeys_specials_so_names_are_typed_literally() =>
        Assert.Equal("O{(}Brien{)} {+}{^}{%}{~}{{}{}}{[}{]}", TeamsLauncher.EscapeSendKeys("O(Brien) +^%~{}[]"));
}

public class HistoryTests
{
    private DateTime _now = new(2026, 11, 17, 9, 0, 0);

    [Fact]
    public void Frequent_and_recent_items_rank_highest()
    {
        var history = new History(null, () => _now);
        history.Record(History.Action, "old-favourite");
        history.Record(History.Action, "old-favourite");
        history.Record(History.Action, "old-favourite");
        _now = _now.AddDays(60);
        history.Record(History.Action, "new-thing");
        history.Record(History.Action, "new-thing");

        Assert.Equal(["new-thing", "old-favourite"], history.Top(5).Select(e => e.Id));
        Assert.True(history.Score(History.Action, "new-thing") > history.Score(History.Action, "old-favourite"));
        Assert.Equal(0, history.Score(History.Action, "never-used"));
    }

    [Fact]
    public void Shortcut_key_usage_is_scored_but_not_listed_as_recent()
    {
        var history = new History(null, () => _now);
        history.Record(History.ShortcutKey, "snp");
        Assert.True(history.Score(History.ShortcutKey, "snp") > 0);
        Assert.Empty(history.Top(5));
    }

    [Fact]
    public void Persists_across_restarts_and_survives_a_corrupt_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"altspacy-history-{Guid.NewGuid():N}.json");
        try
        {
            new History(path, () => _now).Record(History.Url, "https://jira.example.com/browse/CML-1", "j CML-1", "https://jira.example.com/browse/CML-1");
            var reloaded = new History(path, () => _now).Top(1).Single();
            Assert.Equal("j CML-1", reloaded.Title);
            Assert.Equal(1, reloaded.Count);

            File.WriteAllText(path, "{ not json");
            Assert.Empty(new History(path).Top(5));
        }
        finally { File.Delete(path); }
    }
}

public sealed class IconRetryTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"altspacy-icons-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, true);
    }

    [Fact]
    public async Task Failed_favicon_is_retried_later()
    {
        var online = false;
        var png = Png();
        var calls = 0;
        var service = new IconService(_cacheDir, new Handler(_ =>
        {
            calls++;
            return online ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) } : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        })) { RetryAfter = TimeSpan.Zero };
        var jira = new Shortcut { Key = "j", Url = "https://jira.example.com/browse/{q}" };

        var handle = service.For(jira);
        await handle.Loading;
        Assert.IsNotAssignableFrom<BitmapSource>(handle.Image);

        online = true;
        Assert.Same(handle, service.For(jira));
        await handle.Loading;
        Assert.IsAssignableFrom<BitmapSource>(handle.Image);
        Assert.Equal(2, calls);
    }

    private static byte[] Png()
    {
        byte[] bytes = [];
        Ui.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 32)));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            bytes = stream.ToArray();
        });
        return bytes;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}

public class ReleaseNotesTests
{
    [Fact]
    public void First_release_note_matches_the_app_version() => Assert.Equal(AppInfo.Version, ReleaseNotes.All[0].Version);

    [Fact]
    public void About_lists_every_release_note() => Ui.Run(() =>
    {
        var about = new AboutWindow("Alt+Space");
        Ui.Show(about);
        Assert.Equal(ReleaseNotes.All.Length, about.Notes.Items.Count);
        about.Close();
    });

    [Fact]
    public void Bar_glyphs_exist_in_the_icon_font()
    {
        Assert.True(new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var font));
        Assert.All(MainWindow.Glyphs, g => Assert.True(font.CharacterToGlyphMap.ContainsKey(g[0]), $"U+{(int)g[0]:X4}"));
    }
}
