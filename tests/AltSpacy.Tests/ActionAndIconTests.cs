using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AltSpacy.Tests;

public class ActionCatalogTests
{
    private static readonly IReadOnlyList<LauncherAction> Actions = SystemActions.Create(_ => { });

    private static string[] Titles(string input) => ActionCatalog.Match(input, Actions).Select(a => a.Title()).ToArray();

    [Theory]
    [InlineData("dark", "Switch to")]
    [InlineData("light", "Switch to")]
    [InlineData("dark mode", "Switch to")]
    [InlineData("theme", "Switch to")]
    [InlineData("wt", "Windows Terminal")]
    [InlineData("term", "Windows Terminal")]
    [InlineData("env", "Environment variables")]
    [InlineData("task", "Task Manager")]
    [InlineData("settings", "Windows Settings")]
    [InlineData("proxy", "Proxy")]
    [InlineData("wifi", "Wi-Fi")]
    [InlineData("notepad", "Notepad")]
    [InlineData("regedit", "Registry Editor")]
    [InlineData("registry", "Registry Editor")]
    [InlineData("services", "Services")]
    [InlineData("services.msc", "Services")]
    [InlineData("devmgmt", "Device Manager")]
    [InlineData("event", "Event Viewer")]
    [InlineData("appwiz.cpl", "Programs and Features")]
    [InlineData("ncpa", "Network Connections")]
    [InlineData("%temp%", "Temp folder")]
    [InlineData("shell:startup", "Startup folder")]
    [InlineData("hosts", "Edit hosts file")]
    [InlineData("msinfo", "System Information")]
    [InlineData("control", "Control Panel")]
    [InlineData("cmd", "Command Prompt")]
    public void Finds_action_by_title_command_or_keyword(string input, string expectedFirst) =>
        Assert.StartsWith(expectedFirst, Titles(input).FirstOrDefault() ?? "(none)");

    [Fact]
    public void Typing_a_tool_that_is_not_installed_still_lists_it()
    {
        var wordpad = Assert.Single(ActionCatalog.Match("wordpad", Actions));
        Assert.Equal("WordPad", wordpad.Title());
        if (!wordpad.Available) Assert.Equal("Not installed on this PC", wordpad.Detail());
    }

    [Fact]
    public void Catalog_tools_resolve_to_real_files_on_this_machine()
    {
        string[] mustExist = ["notepad", "regedit", "cmd", "taskmgr", "services.msc", "devmgmt.msc", "eventvwr.msc", "appwiz.cpl", "ncpa.cpl", "control", "mmc"];
        foreach (var command in mustExist)
            Assert.True(Actions.Single(a => a.Keywords.FirstOrDefault() == command && a.Badge == command).Available, command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzz")]
    [InlineData("j CML-41")]
    [InlineData("mr 1190")]
    public void Ignores_unrelated_input(string input) => Assert.Empty(Titles(input));

    [Fact]
    public void AltSpacy_commands_are_grouped() =>
        Assert.Equal(["AltSpacy settings", "About AltSpacy", "Reload AltSpacy config", "Edit AltSpacy config file", "Exit AltSpacy"], Titles("altspacy"));

    [Fact]
    public void Every_action_glyph_exists_in_the_installed_icon_font()
    {
        Assert.True(new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
            .TryGetGlyphTypeface(out var font), "Segoe Fluent Icons is not installed");
        foreach (var action in Actions)
        foreach (var glyph in action.Glyphs)
            Assert.True(font.CharacterToGlyphMap.ContainsKey(glyph[0]), $"{action.Title()}: U+{(int)glyph[0]:X4} missing");
        Assert.All(Actions, a => Assert.NotEmpty(a.Glyphs));
    }
}

public sealed class IconServiceTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"altspacy-icons-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private static readonly Shortcut Jira = new() { Key = "j", Url = "https://jira.example.com/browse/{q}" };
    private static readonly Shortcut JiraSearch = new() { Key = "js", Url = "https://jira.example.com/issues/?jql={q}" };

    private static byte[] Png(int size = 32) => OnSta(() =>
    {
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, new byte[size * size * 4], size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    });

    private static T OnSta<T>(Func<T> action)
    {
        T result = default!;
        Ui.Run(() => result = action());
        return result;
    }

    [Fact]
    public void Defaults_to_site_favicon_and_honours_override()
    {
        Assert.Equal("https://jira.example.com/favicon.ico", IconService.SourceFor(Jira));
        Assert.Equal("jira.example.com", IconService.HostOf(Jira));
        Assert.Equal("https://x/logo.png", IconService.SourceFor(new Shortcut { Key = "x", Url = "https://y/{q}", Icon = " https://x/logo.png " }));
    }

    [Fact]
    public async Task Downloads_once_per_host_caches_to_disk_and_reuses_cache()
    {
        var png = Png();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        var service = new IconService(_cacheDir, handler);

        var jira = service.For(Jira);
        Assert.Same(jira, service.For(JiraSearch));
        var placeholder = jira.Image;
        await jira.Loading;

        Assert.NotSame(placeholder, jira.Image);
        Assert.IsAssignableFrom<BitmapSource>(jira.Image);
        Assert.Single(handler.Requests);
        Assert.True(File.Exists(Path.Combine(_cacheDir, "jira.example.com.icon")));

        var offline = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var fromCache = new IconService(_cacheDir, offline).For(Jira);
        await fromCache.Loading;
        Assert.IsAssignableFrom<BitmapSource>(fromCache.Image);
        Assert.Empty(offline.Requests);
    }

    [Fact]
    public async Task Sends_a_user_agent_because_some_sites_refuse_requests_without_one()
    {
        var png = Png();
        string? agent = null;
        var handler = new FakeHandler(request =>
        {
            agent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
        });

        var handle = new IconService(_cacheDir, handler).For(Jira);
        await handle.Loading;

        Assert.Equal($"AltSpacy/{AppInfo.Version}", agent);
    }

    [Fact]
    public async Task Follows_same_host_https_to_http_redirect_over_https()
    {
        var png = Png();
        var splunk = new Shortcut { Key = "sp", Url = "https://splunksearch.example.com/en-US/app/search/search?q={raw}" };
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/favicon.ico"
            ? new HttpResponseMessage(HttpStatusCode.SeeOther) { Headers = { Location = new Uri("http://splunksearch.example.com/en-US/favicon.ico") } }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });

        var handle = new IconService(_cacheDir, handler).For(splunk);
        await handle.Loading;

        Assert.IsAssignableFrom<BitmapSource>(handle.Image);
        Assert.Equal(["https://splunksearch.example.com/favicon.ico", "https://splunksearch.example.com/en-US/favicon.ico"], handler.Requests.Select(u => u.ToString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "<html>login</html>")]
    [InlineData(HttpStatusCode.NotFound, "")]
    public async Task Keeps_monogram_when_favicon_is_not_an_image(HttpStatusCode status, string body)
    {
        var service = new IconService(_cacheDir, new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }));
        var handle = service.For(Jira);
        var monogram = handle.Image;
        await handle.Loading;

        Assert.Same(monogram, handle.Image);
        Assert.Same(IconArt.Monogram("jira.example.com"), monogram);
        Assert.False(Directory.Exists(_cacheDir) && Directory.EnumerateFiles(_cacheDir).Any());
    }

    [Theory]
    [InlineData(0x18, 0x17, 0x17, 0xFF, true)]
    [InlineData(0xFC, 0x6D, 0x26, 0xFF, false)]
    [InlineData(0xFF, 0xFF, 0xFF, 0xFF, false)]
    [InlineData(0x00, 0x00, 0x00, 0x00, false)]
    public void Detects_icons_too_dark_for_dark_mode(byte r, byte g, byte b, byte a, bool expected)
    {
        var pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new[] { b, g, r, a }).ToArray();
        var image = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
        Assert.Equal(expected, IconArt.IsDark(image));
    }

    [Fact]
    public async Task Dark_favicons_are_flagged_for_a_backplate()
    {
        var black = OnSta(() =>
        {
            var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null,
                Enumerable.Range(0, 256).SelectMany(_ => new byte[] { 0x10, 0x10, 0x10, 0xFF }).ToArray(), 64);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        });
        var service = new IconService(_cacheDir, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(black) }));
        var github = service.For(new Shortcut { Key = "gh", Url = "https://github.com/search?q={q}" });
        Assert.False(github.NeedsPlate);
        await github.Loading;
        Assert.True(github.NeedsPlate);
    }

    [Fact]
    public void Decodes_multi_size_ico_picking_largest_frame()
    {
        var ico = OnSta(() =>
        {
            using var stream = new MemoryStream();
            Logo.WriteIco(stream, [16, 32, 64]);
            return stream.ToArray();
        });
        Assert.Equal(64, IconArt.Decode(ico).PixelWidth);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request);
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response);
        }
    }
}
