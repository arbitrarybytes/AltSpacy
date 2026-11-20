namespace AltSpacy.Tests;

/// <summary>The personal config the tests exercise (CML- prefix, IRE as default project). The shipped defaults are generic.</summary>
internal static class TestConfig
{
    public const string Ire = "my-group/platform/routing-service";

    public const string Json =
        """
        {
          "hotkey": "Alt+Space",
          "browser": "msedge",
          "projects": { "ire": "my-group/platform/routing-service" },
          "shortcuts": [
            { "key": "j",   "description": "Jira issue",             "url": "https://jira.example.com/browse/{q}", "prefixIfNumeric": "CML-" },
            { "key": "js",  "description": "Jira text search",       "url": "https://jira.example.com/issues/?jql=text~%22{q}%22" },
            { "key": "gl",  "description": "GitLab search",          "url": "https://gitlab.example.com/search?search={q}" },
            { "key": "mr",  "description": "IRE merge request",      "url": "https://gitlab.example.com/{project}/-/merge_requests/{q}", "defaultProject": "ire" },
            { "key": "gc",  "description": "IRE commit",             "url": "https://gitlab.example.com/{project}/-/commit/{q}", "defaultProject": "ire" },
            { "key": "gb",  "description": "IRE branch",             "url": "https://gitlab.example.com/{project}/-/tree/{q}", "defaultProject": "ire" },
            { "key": "pl",  "description": "IRE pipelines",          "url": "https://gitlab.example.com/{project}/-/pipelines", "defaultProject": "ire" },
            { "key": "gh",  "description": "GitHub search",          "url": "https://github.com/search?q={q}" },
            { "key": "c",   "description": "Confluence search",      "url": "https://confluence.example.com/dosearchsite.action?queryString={q}" },
            { "key": "sp",  "description": "Splunk prod search",     "url": "https://splunksearch.example.com/en-US/app/search/search?q=search%20{raw}" },
            { "key": "snp", "description": "Splunk non-prod search", "url": "https://splunksearch-np.example.com/en-US/app/search/search?q=search%20{raw}" }
          ]
        }
        """;

    public static AltSpacyConfig Load() => ConfigStore.Parse(Json);
}
