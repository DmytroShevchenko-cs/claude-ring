namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class SettingsLoaderTests
{
    private static Settings Valid(string json)
    {
        var r = SettingsLoader.Parse(json);
        Assert.Empty(r.Errors);
        Assert.NotNull(r.Settings);
        return r.Settings!;
    }

    private static string Error(string json)
    {
        var r = SettingsLoader.Parse(json);
        Assert.Null(r.Settings);
        return Assert.Single(r.Errors);
    }

    private static string[] Ids(Settings s) => s.ChatMenu.OfType<MenuEntry.BuiltIn>().Select(b => b.Id).ToArray();

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var r = SettingsLoader.Load("/nonexistent/config.json");
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
        var s = r.Settings!;
        Assert.Equal(UiLanguage.Auto, s.Language);
        Assert.Equal([Path.Combine(Settings.Home, "Projects")], s.RepoRoots.ToArray());
        Assert.Equal(Settings.DefaultTaskKeyPattern, s.TaskKeyPattern);
        Assert.Equal(GitHostType.GitLab, s.Git.Host);
        Assert.Equal("https://gitlab.com", s.Git.BaseUrl);
        Assert.Null(s.Git.Group);
        Assert.Equal(["main"], s.Git.Targets.Select(t => t.Label).ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.None), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.None), s.Ide);
        Assert.Equal(["chat", "ide", "reviews", "issue", "repos"], Ids(s));
        Assert.Equal(8, s.SlotCount);
        Assert.Equal(150, s.ReopenDelayMs);
        Assert.Equal("", s.ResetOnRingEvent);
    }

    [Fact]
    public void File_is_read()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """{ "slotCount": 6 }""");
        Assert.Equal(6, SettingsLoader.Load(path).Settings!.SlotCount);
    }

    [Fact]
    public void Empty_object_gives_defaults()
    {
        var s = Valid("{}");
        Assert.Equal(8, s.SlotCount);
        Assert.Equal(GitHostType.GitLab, s.Git.Host);
        Assert.Equal(new IdeSettings(IdeType.None), s.Ide);
    }

    [Fact]
    public void Full_example_with_comments_and_trailing_commas()
    {
        var s = Valid("""
            {
              // labels
              "language": "ru",
              "repoRoots": ["~/Projects", "/work"],
              "taskKeyPattern": "#(\\d+)",
              "git": { "host": "github", "group": "acme", "targets": ["develop", "main"], },
              "tracker": { "type": "linear", "workspace": "acme" },
              "ide": { "type": "cursor" },
              "chatMenu": [
                "chat", "reviews",
                { "label": "Sentry", "url": "https://sentry.io/issues/?query={key}" },
                { "label": "Terminal", "run": ["open", "-a", "Terminal", "{repo.path}"], "perRepo": true },
              ],
              "slotCount": 9,
              "reopenDelayMs": 200,
              "resetOnRingEvent": "MouseLeave",
            }
            """);

        Assert.Equal(UiLanguage.Ru, s.Language);
        Assert.Equal([Path.Combine(Settings.Home, "Projects"), "/work"], s.RepoRoots.ToArray());
        Assert.Equal(@"#(\d+)", s.TaskKeyPattern);
        Assert.Equal(GitHostType.GitHub, s.Git.Host);
        Assert.Equal("https://github.com", s.Git.BaseUrl);
        Assert.Equal("acme", s.Git.Group);
        Assert.Equal(["develop", "main"], s.Git.Targets.Select(t => t.Label).ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.Linear, null, "acme"), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.Cursor), s.Ide);
        Assert.Equal(4, s.ChatMenu.Count);
        Assert.Equal(["chat", "reviews"], Ids(s));
        var sentry = Assert.IsType<MenuEntry.Custom>(s.ChatMenu[2]);
        Assert.Equal("Sentry", sentry.Label);
        Assert.Equal("https://sentry.io/issues/?query={key}", sentry.Url);
        Assert.Null(sentry.Run);
        Assert.False(sentry.PerRepo);
        var terminal = Assert.IsType<MenuEntry.Custom>(s.ChatMenu[3]);
        Assert.Null(terminal.Url);
        Assert.Equal(["open", "-a", "Terminal", "{repo.path}"], terminal.Run!.ToArray());
        Assert.True(terminal.PerRepo);
        Assert.Equal(9, s.SlotCount);
        Assert.Equal(200, s.ReopenDelayMs);
        Assert.Equal("MouseLeave", s.ResetOnRingEvent);
    }

    [Fact]
    public void Names_and_values_ignore_case()
    {
        var s = Valid("""{ "Git": { "Host": "GitHub" }, "SLOTCOUNT": 5 }""");
        Assert.Equal(GitHostType.GitHub, s.Git.Host);
        Assert.Equal(5, s.SlotCount);
    }

    [Fact]
    public void Home_is_expanded()
    {
        var s = Valid("""{ "repoRoots": ["~/Projects", "~"], "ide": { "type": "custom", "app": "~/Applications/Zed.app" } }""");
        Assert.Equal([Path.Combine(Settings.Home, "Projects"), Settings.Home], s.RepoRoots.ToArray());
        Assert.Equal(Path.Combine(Settings.Home, "Applications/Zed.app"), s.Ide.App);
    }

    [Fact]
    public void Github_enterprise_base_url_has_no_trailing_slash()
    {
        var s = Valid("""{ "git": { "host": "github", "baseUrl": "https://ghe.example.com/" } }""");
        Assert.Equal("https://ghe.example.com", s.Git.BaseUrl);
    }

    [Fact]
    public void Legacy_fields_map_to_the_new_schema()
    {
        var r = SettingsLoader.Parse("""
            {
              "repoRoots": ["/work"],
              "gitLabBaseUrl": "https://gitlab.example.com/",
              "gitLabGroup": "acme",
              "jiraBaseUrl": "https://example.atlassian.net",
              "mrTargets": [ { "label": "→ Dev", "branch": "Dev" }, { "label": "→ RC", "branch": "RC" } ],
              "slotCount": 9,
              "reopenDelayMs": 150,
              "resetOnRingEvent": ""
            }
            """);

        Assert.Empty(r.Errors);
        var s = r.Settings!;
        Assert.Equal(GitHostType.GitLab, s.Git.Host);
        Assert.Equal("https://gitlab.example.com", s.Git.BaseUrl);
        Assert.Equal("acme", s.Git.Group);
        Assert.Equal(["Dev", "RC"], s.Git.Targets.Select(t => t.Label).ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.Jira, "https://example.atlassian.net"), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.Rider), s.Ide);
        Assert.Equal(9, s.SlotCount);
        Assert.Contains(r.Warnings, w => w.StartsWith("gitLabBaseUrl:"));
        Assert.Contains(r.Warnings, w => w.StartsWith("mrTargets:"));
    }

    [Fact]
    public void Legacy_config_without_language_uses_russian()
    {
        Assert.Equal(UiLanguage.Ru, Valid("""{ "jiraBaseUrl": "https://example.atlassian.net" }""").Language);
        Assert.Equal(UiLanguage.En, Valid("""{ "jiraBaseUrl": "https://example.atlassian.net", "language": "en" }""").Language);
        Assert.Equal(UiLanguage.Auto, Valid("""{ "repoRoots": ["/work"] }""").Language);
    }

    [Fact]
    public void Legacy_rider_path_gives_the_app()
    {
        var s = Valid("""{ "riderPath": "/Apps/Rider 2025.app/Contents/MacOS/rider" }""");
        Assert.Equal(new IdeSettings(IdeType.Rider, "/Apps/Rider 2025.app"), s.Ide);
    }

    [Fact]
    public void Legacy_rider_path_without_app_bundle_uses_the_default_app()
    {
        var r = SettingsLoader.Parse("""{ "riderPath": "/usr/local/bin/rider" }""");
        Assert.Equal(new IdeSettings(IdeType.Rider), r.Settings!.Ide);
        Assert.Contains(r.Warnings, w => w.StartsWith("riderPath:") && w.Contains(".app"));
    }

    [Fact]
    public void New_fields_win_over_legacy_fields()
    {
        var s = Valid("""
            {
              "gitLabBaseUrl": "https://old.example.com",
              "gitLabGroup": "old",
              "jiraBaseUrl": "https://old.atlassian.net",
              "mrTargets": [ { "branch": "old" } ],
              "riderPath": "/Applications/Rider.app/Contents/MacOS/rider",
              "git": { "baseUrl": "https://new.example.com", "targets": ["new"] },
              "tracker": { "type": "youtrack", "baseUrl": "https://yt.example.com" },
              "ide": { "type": "idea" }
            }
            """);
        Assert.Equal("https://new.example.com", s.Git.BaseUrl);
        Assert.Equal("old", s.Git.Group);
        Assert.Equal(["new"], s.Git.Targets.Select(t => t.Label).ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.YouTrack, "https://yt.example.com"), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.Idea), s.Ide);
    }

    [Fact]
    public void Target_can_group_several_branch_names()
    {
        var s = SettingsLoader.Parse("""
            { "git": { "targets": [ "develop", { "label": "prod", "branches": ["master", "main"] } ] } }
            """).Settings!;
        Assert.Equal(["develop", "prod"], s.Git.Targets.Select(t => t.Label).ToArray());
        Assert.Equal(["develop"], s.Git.Targets[0].Branches.ToArray());
        Assert.Equal(["master", "main"], s.Git.Targets[1].Branches.ToArray());
    }

    [Theory]
    [InlineData("""{ "git": { "host": "bitbucket" } }""", "git.host:")]
    [InlineData("""{ "tracker": { "type": "trello" } }""", "tracker.type:")]
    [InlineData("""{ "ide": { "type": "notepad" } }""", "ide.type:")]
    [InlineData("""{ "ide": { "type": "custom" } }""", "ide.app:")]
    [InlineData("""{ "taskKeyPattern": "([A-Z" }""", "taskKeyPattern:")]
    [InlineData("""{ "language": "de" }""", "language:")]
    [InlineData("""{ "slotCount": 12 }""", "slotCount:")]
    [InlineData("""{ "slotCount": "nine" }""", "slotCount:")]
    [InlineData("""{ "repoRoots": ["Projects"] }""", "repoRoots[0]:")]
    [InlineData("""{ "repoRoots": "/work" }""", "repoRoots:")]
    [InlineData("""{ "git": { "baseUrl": "gitlab.example.com" } }""", "git.baseUrl:")]
    [InlineData("""{ "git": { "targets": ["main", 5] } }""", "git.targets[1]:")]
    [InlineData("""{ "git": { "targets": [ { "branches": ["main"] } ] } }""", "git.targets[0].label:")]
    [InlineData("""{ "git": { "targets": [ { "label": "prod", "branches": [] } ] } }""", "git.targets[0].branches:")]
    [InlineData("""{ "git": { "targets": [ { "label": "prod" } ] } }""", "git.targets[0].branches:")]
    [InlineData("""{ "git": { "targets": "main" } }""", "git.targets:")]
    [InlineData("""{ "git": "gitlab" }""", "git:")]
    [InlineData("""{ "chatMenu": "chat" }""", "chatMenu:")]
    [InlineData("""{ "mrTargets": [ { "label": "x" } ] }""", "mrTargets[0].branch:")]
    [InlineData("[]", "config:")]
    [InlineData("{ not json", "config:")]
    public void Invalid_value_is_an_error_with_its_field_path(string json, string path) =>
        Assert.StartsWith(path, Error(json));

    [Fact]
    public void All_errors_are_reported()
    {
        var r = SettingsLoader.Parse("""{ "git": { "host": "x" }, "slotCount": 0 }""");
        Assert.Null(r.Settings);
        Assert.Equal(2, r.Errors.Count);
    }

    [Fact]
    public void Unknown_menu_item_is_skipped_with_a_warning()
    {
        var r = SettingsLoader.Parse("""{ "chatMenu": ["chat", "pipelines", "repos"] }""");
        Assert.Empty(r.Errors);
        Assert.Equal(["chat", "repos"], Ids(r.Settings!));
        Assert.StartsWith("chatMenu[1]:", Assert.Single(r.Warnings));
    }

    [Theory]
    [InlineData("""{ "url": "https://x/{key}" }""")]
    [InlineData("""{ "label": "X" }""")]
    [InlineData("""{ "label": "X", "url": "" }""")]
    [InlineData("""{ "label": "X", "url": "https://x", "run": ["open"] }""")]
    [InlineData("""{ "label": "X", "run": [] }""")]
    [InlineData("""{ "label": "X", "run": ["open", 5] }""")]
    [InlineData("""{ "label": "X", "url": "https://x/{ticket}" }""")]
    [InlineData("""{ "label": "X", "run": ["{repo.path}/run.sh"], "perRepo": true }""")]
    [InlineData("""{ "label": "X", "url": "{repo.webUrl}/pipelines" }""")]
    [InlineData("5")]
    public void Invalid_custom_item_is_skipped_with_a_warning(string item)
    {
        var r = SettingsLoader.Parse($$"""{ "chatMenu": ["chat", {{item}}] }""");
        Assert.Empty(r.Errors);
        Assert.Single(r.Settings!.ChatMenu);
        Assert.Equal(["chat"], Ids(r.Settings!));
        Assert.StartsWith("chatMenu[1]:", Assert.Single(r.Warnings));
    }
}
