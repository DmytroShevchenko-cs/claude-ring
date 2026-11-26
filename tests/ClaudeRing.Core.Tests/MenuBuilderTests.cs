namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class MenuBuilderTests
{
    private static readonly IdeSettings Rider = new(IdeType.Rider);

    // Repo folders carry the key suffix ("repo0-A-1") to check that labels drop it.
    private static ChatContext Chat(string? key = "A-1", int repos = 2, bool webUrls = true) => new(
        new ChatTab("sess-1", key ?? "Some chat", "/w"),
        new SessionInfo(null, null, key == null ? null : $"feature/{key}", "/w"),
        key,
        Enumerable.Range(0, key == null ? 0 : repos)
            .Select(i => new RepoRef($"repo{i}-{key}", $"/p/{key}/repo{i}", $"feature/{key}", webUrls ? $"https://g/T/repo{i}" : null))
            .ToList());

    private static Settings Config(params MenuEntry[] menu) => new()
    {
        SlotCount = 8,
        Git = new GitSettings(GitHostType.GitLab, "https://gitlab.example.com", "acme", [ReviewTarget.Branch("develop"), ReviewTarget.Branch("main")]),
        Tracker = new TrackerSettings(TrackerType.Jira, "https://j"),
        Ide = Rider,
        ChatMenu = menu.Length == 0 ? new Settings().ChatMenu : menu,
    };

    private static MenuPage Page(Settings settings, ChatContext chat) => new MenuBuilder(settings, Strings.En).ChatPage(chat);

    private static string[] Labels(MenuPage page) => page.Items.Select(i => i.Label).ToArray();

    private static MenuItem Find(MenuPage page, string label) => page.Items.Single(i => i.Label == label);

    private static MenuEntry Id(string id) => new MenuEntry.BuiltIn(id);

    private static MenuEntry UrlItem(string label, string url, bool perRepo = false) => new MenuEntry.Custom(label, url, null, perRepo);

    private static MenuEntry RunItem(string label, bool perRepo, params string[] run) => new MenuEntry.Custom(label, null, run, perRepo);

    [Fact]
    public void Default_menu_in_english() =>
        Assert.Equal(["VS Code", "Rider", "MR", "Jira", "Repos"], Labels(Page(Config(), Chat())));

    [Fact]
    public void Default_menu_in_russian() =>
        Assert.Equal(["VS Code", "Rider", "MR", "Jira", "Репо"], Labels(new MenuBuilder(Config(), Strings.Ru).ChatPage(Chat())));

    [Fact]
    public void Items_follow_the_chat_menu_order() =>
        Assert.Equal(["Repos", "VS Code"], Labels(Page(Config(Id("repos"), Id("chat")), Chat())));

    [Fact]
    public void Chat_item_focuses_the_chat()
    {
        var chat = Chat();
        Assert.Equal(new MenuAction.FocusChat(chat.Tab), Find(Page(Config(), chat), "VS Code").Action);
    }

    [Fact]
    public void Chat_without_key_shows_only_the_chat_item() =>
        Assert.Equal(["VS Code"], Labels(Page(Config(), Chat(key: null))));

    [Fact]
    public void Ide_item_is_hidden_without_ide() =>
        Assert.DoesNotContain("Rider", Labels(Page(Config() with { Ide = new IdeSettings(IdeType.None) }, Chat())));

    [Fact]
    public void Ide_and_repos_are_hidden_without_repos() =>
        Assert.Equal(["VS Code", "MR", "Jira"], Labels(Page(Config(), Chat(repos: 0))));

    [Fact]
    public void Ide_item_uses_the_ide_name() =>
        Assert.Contains("Cursor", Labels(Page(Config() with { Ide = new IdeSettings(IdeType.Cursor) }, Chat())));

    [Fact]
    public void Ide_submenu_lists_repos_then_all()
    {
        var chat = Chat(repos: 10);
        var ide = Find(Page(Config(), chat), "Rider").Submenu!;
        Assert.Equal(["repo0", "repo1", "repo2", "repo3", "repo4", "repo5", "All"], Labels(ide));
        Assert.Equal([chat.Repos[0]], ((MenuAction.OpenIde)ide.Items[0].Action!).Repos);
        Assert.Equal(new MenuAction.OpenIde(Rider, chat.Repos), ide.Items[^1].Action);
    }

    [Fact]
    public void Gitlab_reviews_submenu()
    {
        var reviews = Find(Page(Config(), Chat()), "MR").Submenu!;
        Assert.Equal(["All MRs", "→ develop", "→ main"], Labels(reviews));
        Assert.Equal(
            new MenuAction.OpenUrl("https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=A-1&target_branch=main"),
            reviews.Items[2].Action);
    }

    [Fact]
    public void Github_reviews_submenu()
    {
        var settings = Config() with { Git = new GitSettings(GitHostType.GitHub, "https://github.com", "acme", [ReviewTarget.Branch("main")]) };
        var reviews = Find(Page(settings, Chat()), "PR").Submenu!;
        Assert.Equal(["All PRs", "→ main"], Labels(reviews));
        Assert.Equal(new MenuAction.OpenUrl("https://github.com/pulls?q=is:pr+A-1+org:acme"), reviews.Items[0].Action);
    }

    [Fact]
    public void Issue_item_uses_the_tracker()
    {
        var settings = Config() with { Tracker = new TrackerSettings(TrackerType.Linear, Workspace: "acme") };
        Assert.Equal(new MenuAction.OpenUrl("https://linear.app/acme/issue/A-1"), Find(Page(settings, Chat()), "Linear").Action);
    }

    [Fact]
    public void Issue_item_is_hidden_when_the_tracker_cannot_link()
    {
        Assert.DoesNotContain("Jira", Labels(Page(Config() with { Tracker = new TrackerSettings(TrackerType.Jira) }, Chat())));
        Assert.Equal(["VS Code", "Rider", "MR", "Repos"], Labels(Page(Config() with { Tracker = new TrackerSettings(TrackerType.None) }, Chat())));
    }

    [Fact]
    public void Repos_submenu_lists_repos_then_browser_and_finder()
    {
        var repos = Find(Page(Config(), Chat(repos: 10)), "Repos").Submenu!;
        Assert.Equal(["repo0", "repo1", "repo2", "repo3", "repo4", "All in browser", "Folder in Finder"], Labels(repos));
        Assert.Equal(new MenuAction.OpenUrl("https://g/T/repo1/-/tree/feature/A-1"), repos.Items[1].Action);
        Assert.Equal(10, ((MenuAction.OpenUrls)repos.Items[5].Action!).Urls.Count);
        Assert.Equal(["/p/A-1"], ((MenuAction.Reveal)repos.Items[6].Action!).Paths);
    }

    [Fact]
    public void Repo_without_web_url_opens_in_ide_or_finder()
    {
        var chat = Chat(webUrls: false);
        var withIde = Find(Page(Config(), chat), "Repos").Submenu!;
        Assert.Equal(["repo0", "repo1", "Folder in Finder"], Labels(withIde));
        Assert.Equal([chat.Repos[0]], ((MenuAction.OpenIde)withIde.Items[0].Action!).Repos);

        var noIde = Find(Page(Config() with { Ide = new IdeSettings(IdeType.None) }, chat), "Repos").Submenu!;
        Assert.Equal(["/p/A-1/repo0"], ((MenuAction.Reveal)noIde.Items[0].Action!).Paths);
    }

    [Fact]
    public void Custom_url_item_renders_the_template() =>
        Assert.Equal(new MenuAction.OpenUrl("https://sentry.io/issues/?query=A-1"),
            Find(Page(Config(Id("chat"), UrlItem("Sentry", "https://sentry.io/issues/?query={key}")), Chat()), "Sentry").Action);

    [Fact]
    public void Custom_item_is_hidden_when_a_value_is_missing() =>
        Assert.Equal(["VS Code"], Labels(Page(Config(Id("chat"), UrlItem("Sentry", "https://sentry.io/issues/?query={key}")), Chat(key: null))));

    [Fact]
    public void Custom_run_item_passes_arguments_as_is()
    {
        var run = (MenuAction.Run)Find(Page(Config(RunItem("Finder", false, "open", "-R", "{repos.parent}")), Chat()), "Finder").Action!;
        Assert.Equal("open", run.File);
        Assert.Equal(["-R", "/p/A-1"], run.Args);
    }

    [Fact]
    public void Per_repo_item_opens_a_repo_submenu()
    {
        var submenu = Find(Page(Config(RunItem("Terminal", true, "open", "-a", "Terminal", "{repo.path}")), Chat()), "Terminal").Submenu!;
        Assert.Equal(["repo0", "repo1"], Labels(submenu));
        var run = (MenuAction.Run)submenu.Items[1].Action!;
        Assert.Equal("open", run.File);
        Assert.Equal(["-a", "Terminal", "/p/A-1/repo1"], run.Args);
    }

    [Fact]
    public void Per_repo_item_leaves_out_repos_without_values()
    {
        var chat = Chat() with
        {
            Repos = [new RepoRef("api", "/p/api", "feature/A-1", "https://g/T/api"), new RepoRef("local", "/p/local", "feature/A-1")],
        };
        var submenu = Find(Page(Config(UrlItem("CI", "{repo.webUrl}/-/pipelines", perRepo: true)), chat), "CI").Submenu!;
        Assert.Equal(["api"], Labels(submenu));
        Assert.Equal(new MenuAction.OpenUrl("https://g/T/api/-/pipelines"), submenu.Items[0].Action);
    }

    [Fact]
    public void Per_repo_item_is_hidden_without_repos() =>
        Assert.Empty(Labels(Page(Config(UrlItem("CI", "{repo.webUrl}/-/pipelines", perRepo: true)), Chat(repos: 0))));

    [Fact]
    public void Per_repo_submenu_fits_the_slots() =>
        Assert.Equal(7, Find(Page(Config(RunItem("T", true, "open", "{repo.path}")), Chat(repos: 10)), "T").Submenu!.Items.Count);

    private static readonly GitSettings ProdGroup = new(
        GitHostType.GitLab, "https://gitlab.example.com", "acme",
        [ReviewTarget.Branch("develop"), new ReviewTarget("prod", ["master", "main"])]);

    private static MenuPage Reviews(Func<RepoRef, string, bool> hasRemoteBranch, int repos = 2) =>
        new MenuBuilder(Config() with { Git = ProdGroup }, Strings.En, hasRemoteBranch)
            .ChatPage(Chat(repos: repos)).Items.Single(i => i.Label == "MR").Submenu!;

    private static string Mrs(string target) =>
        $"https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=A-1&target_branch={target}";

    [Fact]
    public void Group_target_opens_only_branches_that_exist()
    {
        var reviews = Reviews((r, b) => b == "main");
        Assert.Equal(["All MRs", "→ develop", "→ prod"], Labels(reviews));
        Assert.Equal(new MenuAction.OpenUrl(Mrs("main")), reviews.Items[2].Action);
    }

    [Fact]
    public void Group_target_opens_every_existing_branch_in_order()
    {
        var reviews = Reviews((r, b) => r.Name.StartsWith("repo0") ? b == "main" : b == "master");
        Assert.Equal(new MenuAction.OpenUrls([Mrs("master"), Mrs("main")]), reviews.Items[2].Action, new ActionComparer());
    }

    [Fact]
    public void Group_target_opens_all_branches_when_none_is_found()
    {
        Assert.Equal(new MenuAction.OpenUrls([Mrs("master"), Mrs("main")]), Reviews((r, b) => false).Items[2].Action, new ActionComparer());
        Assert.Equal(new MenuAction.OpenUrls([Mrs("master"), Mrs("main")]), Reviews((r, b) => true, repos: 0).Items[2].Action, new ActionComparer());
    }

    [Fact]
    public void One_branch_target_does_not_check_branches()
    {
        var git = ProdGroup with { Targets = [ReviewTarget.Branch("develop")] };
        var reviews = new MenuBuilder(Config() with { Git = git }, Strings.En, (r, b) => throw new InvalidOperationException("must not be called"))
            .ChatPage(Chat()).Items.Single(i => i.Label == "MR").Submenu!;
        Assert.Equal(new MenuAction.OpenUrl(Mrs("develop")), reviews.Items[1].Action);
    }

    [Fact]
    public void Reviews_submenu_fits_the_slots()
    {
        var git = ProdGroup with { Targets = [ReviewTarget.Branch("a"), ReviewTarget.Branch("b"), ReviewTarget.Branch("c")] };
        var reviews = new MenuBuilder(Config() with { Git = git, SlotCount = 4 }, Strings.En)
            .ChatPage(Chat()).Items.Single(i => i.Label == "MR").Submenu!;
        Assert.Equal(["All MRs", "→ a", "→ b"], Labels(reviews));
    }

    private sealed class ActionComparer : IEqualityComparer<MenuAction?>
    {
        public bool Equals(MenuAction? x, MenuAction? y) => (x, y) switch
        {
            (MenuAction.OpenUrls a, MenuAction.OpenUrls b) => a.Urls.SequenceEqual(b.Urls),
            _ => Object.Equals(x, y),
        };

        public int GetHashCode(MenuAction? obj) => 0;
    }
}
