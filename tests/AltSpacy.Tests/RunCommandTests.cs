using System.IO;

namespace AltSpacy.Tests;

public sealed class RunCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("altspacy-path-").FullName;
    private readonly string _wordpad;
    private readonly CommandResolver _resolver;

    public RunCommandTests()
    {
        foreach (var file in new[] { "tool.exe", "build.cmd", "my.msc", "applet.cpl" }) File.WriteAllText(Path.Combine(_dir, file), "");
        _wordpad = Path.Combine(_dir, "apps", "wordpad.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_wordpad)!);
        File.WriteAllText(_wordpad, "");
        _resolver = new CommandResolver([_dir], [".COM", ".EXE", ".CMD", ".MSC"], exe => exe == "wordpad.exe" ? _wordpad : null);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData("tool", "tool.exe")]
    [InlineData("TOOL.EXE", "tool.exe")]
    [InlineData("build", "build.cmd")]
    [InlineData("my.msc", "my.msc")]
    [InlineData("my", "my.msc")]
    [InlineData("applet.cpl", "applet.cpl")]
    public void Resolves_like_the_run_dialog(string name, string expectedFile) =>
        Assert.Equal(Path.Combine(_dir, expectedFile), _resolver.Resolve(name), ignoreCase: true);

    [Fact]
    public void Falls_back_to_app_paths_registry() => Assert.Equal(_wordpad, _resolver.Resolve("wordpad"));

    [Theory]
    [InlineData("missing")]
    [InlineData("sub\\tool")]
    [InlineData("")]
    public void Returns_null_for_unknown_names(string name) => Assert.Null(_resolver.Resolve(name));

    [Fact]
    public void Expands_environment_variables_and_accepts_absolute_paths()
    {
        Assert.Equal(Environment.ExpandEnvironmentVariables("%TEMP%"), _resolver.Resolve("%TEMP%"), ignoreCase: true);
        Assert.Equal(Path.Combine(_dir, "tool.exe"), _resolver.Resolve(Path.Combine(_dir, "tool.exe")));
    }

    [Theory]
    [InlineData("https://github.com", "https://github.com", "")]
    [InlineData("ms-settings:display", "ms-settings:display", "")]
    [InlineData("shell:startup", "shell:startup", "")]
    [InlineData(@"\\fileserver\share", @"\\fileserver\share", "")]
    public void Interprets_urls_protocols_and_unc_paths(string text, string file, string arguments)
    {
        var target = RunTarget.Interpret(text, _resolver);
        Assert.NotNull(target);
        Assert.Equal(file, target.File);
        Assert.Equal(arguments, target.Arguments);
        Assert.False(target.IsCommand);
    }

    [Fact]
    public void Interprets_commands_with_arguments()
    {
        var target = RunTarget.Interpret("tool --verbose \"a b\"", _resolver)!;
        Assert.Equal(Path.Combine(_dir, "tool.exe"), target.File);
        Assert.Equal("--verbose \"a b\"", target.Arguments);
        Assert.True(target.IsCommand);
    }

    [Fact]
    public void Interprets_existing_folders() => Assert.Equal(_dir, RunTarget.Interpret(_dir, _resolver)?.File);

    [Theory]
    [InlineData("x")]
    [InlineData("nothing here")]
    [InlineData(@"C:\definitely\not\here")]
    public void Rejects_text_the_run_dialog_would_reject(string text) => Assert.Null(RunTarget.Interpret(text, _resolver));

    [Theory]
    [InlineData("tool", "tool", "")]
    [InlineData("tool a b", "tool", "a b")]
    [InlineData("\"C:\\Program Files\\x.exe\" --flag", @"C:\Program Files\x.exe", "--flag")]
    public void Splits_command_lines(string text, string command, string arguments) =>
        Assert.Equal((command, arguments), RunTarget.Split(text));

    [Fact]
    public void Run_text_offers_run_with_admin_variant()
    {
        var action = SystemActions.RunText("tool --verbose", _resolver)!;
        Assert.Equal("Run tool --verbose", action.Title());
        Assert.Equal(SystemActions.RunGroup, action.Group);
        Assert.NotNull(action.RunElevated);
        Assert.Equal("Open https://github.com", SystemActions.RunText("https://github.com", _resolver)!.Title());
        Assert.Null(SystemActions.RunText("nothing here", _resolver));
    }

    [Fact]
    public void Catalog_entries_missing_on_this_pc_explain_themselves_when_typed()
    {
        var actions = SystemActions.Create(_ => { }, new CommandResolver([], [".EXE"], _ => null));
        Assert.DoesNotContain(ActionCatalog.Match("reg", actions), a => a.Title() == "Registry Editor");

        var regedit = Assert.Single(ActionCatalog.Match("regedit", actions), a => a.Title() == "Registry Editor");
        Assert.False(regedit.Available);
        Assert.Equal("Not installed on this PC", regedit.Detail());
        Assert.Equal("Registry Editor isn't installed on this PC.", Assert.Throws<InvalidOperationException>(regedit.Run).Message);
    }

    [Fact]
    public void Catalog_resolves_known_tools_to_their_files()
    {
        var resolver = new CommandResolver([_dir], [".EXE", ".MSC"], exe => exe == "wordpad.exe" ? _wordpad : null);
        var catalog = new[] { new RunEntry("tool", "Tool", RunCatalog.Apps, []), new RunEntry("wordpad", "WordPad", RunCatalog.Apps, []) };
        Assert.All(catalog, e => Assert.NotNull(resolver.Resolve(e.Command)));
    }
}

public class BrowserTests
{
    private static readonly BrowserInfo Edge = new("Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");
    private static readonly BrowserInfo Chrome = new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");

    [Theory]
    [InlineData("msedge", "Microsoft Edge")]
    [InlineData("MSEDGE.EXE", "Microsoft Edge")]
    [InlineData("chrome", "Google Chrome")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Google Chrome")]
    public void Matches_configured_browser(string configured, string expected) =>
        Assert.Equal(expected, Browsers.Match(configured, [Edge, Chrome])?.Name);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("firefox")]
    public void No_match_for_default_or_missing(string? configured) => Assert.Null(Browsers.Match(configured, [Edge, Chrome]));

    [Fact]
    public void Reads_executable_from_registered_command_line()
    {
        var exe = Path.Combine(Path.GetTempPath(), $"browser-{Guid.NewGuid():N}.exe");
        File.WriteAllText(exe, "");
        try
        {
            Assert.Equal(exe, Browsers.ExecutableOf($"\"{exe}\" --single-argument %1"));
            Assert.Null(Browsers.ExecutableOf("\"C:\\nope\\x.exe\" %1"));
        }
        finally { File.Delete(exe); }
    }

    [Fact]
    public void Finds_installed_browsers_on_this_machine() =>
        Assert.All(Browsers.Installed(), b => Assert.True(File.Exists(b.Path), b.Path));
}
