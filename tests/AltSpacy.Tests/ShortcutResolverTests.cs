namespace AltSpacy.Tests;

public class ShortcutResolverTests
{
    private static readonly AltSpacyConfig Config = TestConfig.Load();
    private static readonly List<Shortcut> Shortcuts = Config.Shortcuts;

    private const string Mr = "https://gitlab.example.com/my-group/platform/routing-service/-/merge_requests/";

    [Theory]
    [InlineData("j:CML-41", "https://jira.example.com/browse/CML-41")]
    [InlineData("j CML-41", "https://jira.example.com/browse/CML-41")]
    [InlineData("  J : CML-41  ", "https://jira.example.com/browse/CML-41")]
    [InlineData("j:\"CML-41\"", "https://jira.example.com/browse/CML-41")]
    [InlineData("j:41", "https://jira.example.com/browse/CML-41")]
    [InlineData("mr:1190", Mr + "1190")]
    [InlineData("sp:\"error 500\"", "https://splunksearch.example.com/en-US/app/search/search?q=search%20%22error%20500%22")]
    [InlineData("snp:index=ire status=500", "https://splunksearch-np.example.com/en-US/app/search/search?q=search%20index%3Dire%20status%3D500")]
    [InlineData("js:live agent", "https://jira.example.com/issues/?jql=text~%22live%20agent%22")]
    [InlineData("pl", "https://gitlab.example.com/my-group/platform/routing-service/-/pipelines")]
    public void Resolves_input_to_url(string input, string expected) =>
        Assert.Equal(expected, ShortcutResolver.Resolve(input, Shortcuts, Config.Projects)?.Url);

    [Theory]
    [InlineData("j 41", "https://jira.example.com/browse/CML-41")]
    [InlineData("j    CML-41", "https://jira.example.com/browse/CML-41")]
    [InlineData("j: CML-41", "https://jira.example.com/browse/CML-41")]
    [InlineData("mr 1190", Mr + "1190")]
    [InlineData("sp \"error 500\"", "https://splunksearch.example.com/en-US/app/search/search?q=search%20%22error%20500%22")]
    [InlineData("snp index=ire status=500", "https://splunksearch-np.example.com/en-US/app/search/search?q=search%20index%3Dire%20status%3D500")]
    [InlineData("js live agent", "https://jira.example.com/issues/?jql=text~%22live%20agent%22")]
    [InlineData("gl intent routing", "https://gitlab.example.com/search?search=intent%20routing")]
    [InlineData("gh dotnet/runtime", "https://github.com/search?q=dotnet%2Fruntime")]
    [InlineData("c release notes", "https://confluence.example.com/dosearchsite.action?queryString=release%20notes")]
    [InlineData("c:onboarding", "https://confluence.example.com/dosearchsite.action?queryString=onboarding")]
    public void Space_works_as_separator(string input, string expected) =>
        Assert.Equal(expected, ShortcutResolver.Resolve(input, Shortcuts, Config.Projects)?.Url);

    [Theory]
    [InlineData("")]
    [InlineData("j")]
    [InlineData("j ")]
    [InlineData(@"c:\Windows")]
    [InlineData("C:/logs")]
    [InlineData(@"d:\")]
    [InlineData("j:")]
    [InlineData("j:\"\"")]
    [InlineData("zz:123")]
    [InlineData(":edit")]
    public void Returns_null_when_unresolvable(string input) =>
        Assert.Null(ShortcutResolver.Resolve(input, Shortcuts, Config.Projects));

    [Fact]
    public void Prefix_is_only_added_to_numeric_values() =>
        Assert.Equal("https://jira.example.com/browse/ASRC-24028", ShortcutResolver.Resolve("j:ASRC-24028", Shortcuts)?.Url);

    [Theory]
    [InlineData("s", new[] { "sp", "snp" })]
    [InlineData("j", new[] { "j", "js" })]
    [InlineData("sp:", new[] { "sp" })]
    [InlineData("sp ", new[] { "sp" })]
    [InlineData("j CML", new[] { "j" })]
    [InlineData("j:CML", new[] { "j" })]
    [InlineData("x", new string[0])]
    public void Suggests_matching_keys(string input, string[] expected) =>
        Assert.Equal(expected, ShortcutResolver.Suggest(input, Shortcuts).Select(s => s.Key));

    [Fact]
    public void Empty_input_suggests_everything() =>
        Assert.Equal(Shortcuts.Count, ShortcutResolver.Suggest("", Shortcuts).Count);
}
