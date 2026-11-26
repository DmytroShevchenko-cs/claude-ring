namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class RingMenuTests
{
    private sealed class Source(params ChatContext[] chats) : IChatSource
    {
        public IReadOnlyList<ChatContext> Load() => chats;
    }

    private static ChatContext Chat(string key, int repos = 2) => new(
        new ChatTab(Guid.NewGuid().ToString(), key, "/w"),
        new SessionInfo(null, null, $"feature/{key}", "/w"),
        key,
        Enumerable.Range(0, repos).Select(i => new RepoRef($"repo{i}", $"/p/repo{i}-{key}", $"feature/{key}", $"https://g/T/repo{i}")).ToList());

    private static readonly IdeSettings Rider = new(IdeType.Rider);

    // Russian labels, like the ring had before settings existed.
    private static Settings Config(int n = 8, bool jira = true) => new()
    {
        Language = UiLanguage.Ru,
        SlotCount = n,
        Git = new GitSettings(GitHostType.GitLab, "https://gitlab.example.com", "acme", [ReviewTarget.Branch("Dev"), ReviewTarget.Branch("RC"), ReviewTarget.Branch("master")]),
        Tracker = jira ? new TrackerSettings(TrackerType.Jira, "https://j") : new TrackerSettings(TrackerType.None),
        Ide = Rider,
    };

    private static RingMenu Create(IChatSource source, Settings settings) => new(source, new MenuBuilder(settings, Strings.Ru));

    private static RingMenu Menu(params ChatContext[] chats)
    {
        var menu = Create(new Source(chats), Config());
        menu.Reset();
        return menu;
    }

    [Fact]
    public void Root_lists_chats()
    {
        var labels = Menu(Chat("PROJ-1069"), Chat("PROJ-1173")).Render().Select(s => s.Label).ToArray();
        Assert.Equal("PROJ-1069", labels[0]);
        Assert.Equal("PROJ-1173", labels[1]);
        Assert.Equal("", labels[2]);
    }

    [Fact]
    public void Root_pages_when_too_many_chats()
    {
        var menu = Menu(Enumerable.Range(1, 10).Select(i => Chat($"A-{i}")).ToArray());
        Assert.Equal("ещё ›", menu.Render()[7].Label);
        var r = menu.Click(7);
        Assert.True(r.Reopen);
        Assert.Equal("A-8", menu.Render()[0].Label);
    }

    [Fact]
    public void Chat_level_then_rider_all_action_resets_to_root()
    {
        var chat = Chat("PROJ-1069", repos: 3);
        var menu = Menu(chat);
        Assert.True(menu.Click(0).Reopen);
        Assert.Equal("Rider", menu.Render()[1].Label);
        Assert.Equal("← назад", menu.Render()[7].Label);

        Assert.True(menu.Click(1).Reopen);
        var r = menu.Click(Array.IndexOf(Labels(menu), "Все"));

        Assert.False(r.Reopen);
        Assert.Equal(new MenuAction.OpenIde(Rider, chat.Repos), r.Action);
        Assert.Equal("PROJ-1069", menu.Render()[0].Label);
    }

    [Fact]
    public void Chat_level_uses_short_labels()
    {
        var menu = MenuN(9, true, Chat("A-1"));
        menu.Click(0);
        Assert.Equal(["VS Code", "Rider", "MR", "Jira", "Репо", "", "", "", "← назад"], Labels(menu));
    }

    private static RingMenu RiderLevel(int n, ChatContext chat)
    {
        var menu = MenuN(n, true, chat);
        menu.Click(0);
        menu.Click(Array.IndexOf(Labels(menu), "Rider"));
        return menu;
    }

    [Fact]
    public void Rider_level_lists_repos_then_all_then_back()
    {
        var chat = Chat("A-1", 8);
        var menu = RiderLevel(9, chat);
        var labels = Labels(menu);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => $"repo{i}"), labels.Take(7));
        Assert.Equal("Все", labels[7]);
        Assert.Equal("← назад", labels[8]);

        var one = menu.Click(0);
        Assert.False(one.Reopen);
        Assert.Equal([chat.Repos[0]], ((MenuAction.OpenIde)one.Action!).Repos);

        menu = RiderLevel(9, chat);
        var all = menu.Click(7);
        Assert.Equal(chat.Repos, ((MenuAction.OpenIde)all.Action!).Repos);
    }

    [Fact]
    public void Rider_level_with_few_repos()
    {
        var labels = Labels(RiderLevel(9, Chat("A-1", 2)));
        Assert.Equal(["repo0", "repo1", "Все", "", "", "", "", "", "← назад"], labels);
    }

    [Fact]
    public void Repo_names_drop_task_key_suffix()
    {
        var repos = new[]
        {
            new RepoRef("web-app-domain-PROJ-1069", "/p/a", "b"),
            new RepoRef("x-service-proj-1069", "/p/b", "b"),
            new RepoRef("auth-service", "/p/c", "b"),
        };
        var chat = new ChatContext(new ChatTab("s", "t", "/w"), new SessionInfo(null, null, null, null), "PROJ-1069", repos);
        string[] expected = ["web-app-domain", "x-service", "auth-service"];

        Assert.Equal(expected, Labels(RiderLevel(9, chat)).Take(3));
        Assert.Equal(expected, Labels(ReposLevel(9, chat)).Take(3));
    }

    [Fact]
    public void Back_from_rider_returns_to_chat()
    {
        var menu = RiderLevel(8, Chat("A-1"));
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("VS Code", Labels(menu)[0]);
        Assert.Contains("Rider", Labels(menu));
    }

    [Fact]
    public void Mr_level_opens_target_link()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        menu.Click(2);
        Assert.Equal(["Все MR", "→ Dev", "→ RC", "→ master"], menu.Render().Take(4).Select(s => s.Label).ToArray());

        var r = menu.Click(2);

        Assert.Equal(new MenuAction.OpenUrl("https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=RC"), r.Action);
    }

    [Fact]
    public void Back_returns_one_level()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        menu.Click(2);
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("VS Code", menu.Render()[0].Label);
    }

    [Fact]
    public void Level_stays_until_reset()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        Assert.Equal("VS Code", menu.Render()[0].Label);
        Assert.Equal("VS Code", menu.Render()[0].Label);
        menu.Reset();
        Assert.Equal("PROJ-1069", menu.Render()[0].Label);
    }

    [Fact]
    public void Empty_slot_does_nothing() => Assert.Equal(new ClickResult(null, false), Menu(Chat("A-1")).Click(5));

    [Fact]
    public void Chat_without_key_has_no_task_actions()
    {
        var chat = new ChatContext(new ChatTab("s", "Потерянный клик", null), new SessionInfo(null, null, null, null), null, []);
        var menu = Menu(chat);
        menu.Click(0);
        var labels = menu.Render().Select(s => s.Label).ToArray();
        Assert.Equal("VS Code", labels[0]);
        Assert.DoesNotContain("MR", labels);
    }

    private static RingMenu MenuN(int n, bool jira, params ChatContext[] chats)
    {
        var menu = Create(new Source(chats), Config(n, jira));
        menu.Reset();
        return menu;
    }

    private static string[] Labels(RingMenu menu) => menu.Render().Select(s => s.Label).ToArray();

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(9)]
    public void Render_always_returns_slot_count_items(int n)
    {
        var menu = MenuN(n, true, Enumerable.Range(1, 12).Select(i => Chat($"A-{i}", 10)).ToArray());
        Assert.Equal(n, menu.Render().Count);
        menu.Click(0);
        Assert.Equal(n, menu.Render().Count);
        menu.Click(n - 1);
        menu.Click(0);
        var chatLabels = Labels(menu);
        var repos = Array.IndexOf(chatLabels, "Репо");
        if (repos >= 0) menu.Click(repos);
        else
        {
            menu.Click(n - 1);
            menu.Click(0);
        }
        Assert.Equal(n, menu.Render().Count);
    }

    [Fact]
    public void Render_returns_slot_count_items_on_repos_level()
    {
        foreach (var n in new[] { 3, 8, 9 })
        {
            var menu = MenuN(n, false, Chat("A-1", 10));
            menu.Click(0);
            var i = 0;
            while (i < n && Labels(menu)[i] != "Репо") i++;
            if (i < n) menu.Click(i);
            Assert.Equal(n, menu.Render().Count);
        }
    }

    private static RingMenu ReposLevel(int n, ChatContext chat)
    {
        var menu = MenuN(n, true, chat);
        menu.Click(0);
        menu.Click(Array.IndexOf(Labels(menu), "Репо"));
        return menu;
    }

    [Fact]
    public void Nine_slots_repos_level_shows_six_repos_then_gitlab_and_finder()
    {
        const int n = 9;
        var menu = ReposLevel(n, Chat("A-1", 10));

        var labels = Labels(menu);
        Assert.Equal(Enumerable.Range(0, 6).Select(i => $"repo{i}"), labels.Take(6));
        Assert.Equal("Все в браузере", labels[6]);
        Assert.Equal("Папка в Finder", labels[7]);
        Assert.Equal("← назад", labels[n - 1]);
    }

    [Fact]
    public void Repo_click_opens_branch_link()
    {
        var menu = ReposLevel(8, Chat("A-1"));
        Assert.Equal(new MenuAction.OpenUrl("https://g/T/repo1/-/tree/feature/A-1"), menu.Click(1).Action);
    }

    [Fact]
    public void Repo_click_falls_back_to_rider_without_web_url()
    {
        var repo = new RepoRef("r", "/p/r", "feature/A-1");
        var chat = new ChatContext(new ChatTab("s", "A-1", "/w"), new SessionInfo(null, null, null, null), "A-1", [repo]);
        var menu = ReposLevel(8, chat);
        Assert.Equal([repo], ((MenuAction.OpenIde)menu.Click(0).Action!).Repos);
    }

    [Fact]
    public void All_in_gitlab_opens_every_repo_link()
    {
        var menu = ReposLevel(8, Chat("A-1", 10));
        var labels = Labels(menu);
        var action = (MenuAction.OpenUrls)menu.Click(Array.IndexOf(labels, "Все в браузере")).Action!;
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"https://g/T/repo{i}/-/tree/feature/A-1"), action.Urls);
    }

    [Fact]
    public void All_in_gitlab_hidden_without_links()
    {
        var repo = new RepoRef("r", "/p/r", "feature/A-1");
        var chat = new ChatContext(new ChatTab("s", "A-1", "/w"), new SessionInfo(null, null, null, null), "A-1", [repo]);
        var labels = Labels(ReposLevel(8, chat));
        Assert.DoesNotContain("Все в браузере", labels);
        Assert.Contains("Папка в Finder", labels);
    }

    private static string? FinderTarget(params string[] repoPaths)
    {
        var repos = repoPaths.Select((p, i) => new RepoRef($"r{i}", p, "feature/A-1")).ToList();
        var chat = new ChatContext(new ChatTab("s", "A-1", "/w"), new SessionInfo(null, null, null, null), "A-1", repos);
        var menu = ReposLevel(8, chat);
        var action = (MenuAction.Reveal)menu.Click(Array.IndexOf(Labels(menu), "Папка в Finder")).Action!;
        return Assert.Single(action.Paths);
    }

    [Fact]
    public void Finder_folder_is_the_shared_parent_in_task_layout() =>
        Assert.Equal("/a/Projects/A-1", FinderTarget("/a/Projects/A-1/x", "/a/Projects/A-1/y"));

    [Fact]
    public void Finder_folder_is_the_shared_parent_in_flat_layout() =>
        Assert.Equal("/a/Projects", FinderTarget("/a/Projects/x-A-1", "/a/Projects/y-A-1"));

    [Fact]
    public void Finder_folder_is_the_common_parent_in_mixed_layout() =>
        Assert.Equal("/a/Projects", FinderTarget("/a/Projects/A-1/x", "/a/Projects/y-A-1"));

    [Fact]
    public void Exactly_n_chats_has_no_more_button()
    {
        var menu = MenuN(8, false, Enumerable.Range(1, 8).Select(i => Chat($"A-{i}")).ToArray());
        var labels = Labels(menu);
        Assert.DoesNotContain("ещё ›", labels);
        Assert.Equal("A-8", labels[7]);
    }

    [Fact]
    public void Paging_wraps_to_first_page()
    {
        var menu = MenuN(8, false, Enumerable.Range(1, 15).Select(i => Chat($"A-{i}")).ToArray());
        menu.Click(7);
        var labels = Labels(menu);
        Assert.Equal(Enumerable.Range(8, 7).Select(i => $"A-{i}"), labels.Take(7));
        Assert.Equal("ещё ›", labels[7]);
        menu.Click(7);
        Assert.Equal("A-15", menu.Render()[0].Label);
        menu.Click(7);
        Assert.Equal("A-1", menu.Render()[0].Label);
    }

    [Fact]
    public void Jira_hidden_without_base_url()
    {
        var menu = MenuN(8, false, Chat("A-1"));
        menu.Click(0);
        Assert.DoesNotContain("Jira", Labels(menu));
    }

    [Fact]
    public void No_repos_hides_rider_and_repos()
    {
        var menu = MenuN(8, true, Chat("A-1", repos: 0));
        menu.Click(0);
        Assert.Equal(["VS Code", "MR", "Jira", "", "", "", "", "← назад"], Labels(menu));
    }

    [Fact]
    public void Back_from_repos_returns_to_chat()
    {
        var menu = MenuN(8, true, Chat("A-1"));
        menu.Click(0);
        menu.Click(Array.IndexOf(Labels(menu), "Репо"));
        Assert.Equal("Все в браузере", Labels(menu)[2]);
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("VS Code", Labels(menu)[0]);
        Assert.Contains("Репо", Labels(menu));
    }

    [Fact]
    public void Out_of_range_or_negative_slot_does_nothing()
    {
        var menu = Menu(Chat("A-1"));
        Assert.Equal(new ClickResult(null, false), menu.Click(-1));
        Assert.Equal(new ClickResult(null, false), menu.Click(8));
        Assert.Equal(new ClickResult(null, false), menu.Click(100));
        Assert.Equal("A-1", menu.Render()[0].Label);
    }

    private sealed class FlakySource(IReadOnlyList<ChatContext> first) : IChatSource
    {
        private int _calls;
        public IReadOnlyList<ChatContext> Load() =>
            _calls++ == 0 ? first : throw new IOException("boom");
    }

    [Fact]
    public void Load_failure_keeps_previous_chats_and_sets_error()
    {
        var chats = new[] { Chat("A-1"), Chat("A-2") };
        var menu = Create(new FlakySource(chats), Config());
        menu.Reset();
        Assert.Null(menu.LastLoadError);
        Assert.Equal(["A-1", "A-2"], Labels(menu).Take(2));

        menu.Click(0);
        var r = menu.Click(0);

        Assert.IsType<MenuAction.FocusChat>(r.Action);
        menu.Reset();
        Assert.Equal(["A-1", "A-2"], Labels(menu).Take(2));
        Assert.IsType<IOException>(menu.LastLoadError);
    }

    private sealed class CountingSource(params ChatContext[] chats) : IChatSource
    {
        public int Loads;
        public IReadOnlyList<ChatContext> Load() { Loads++; return chats; }
    }

    [Fact]
    public void Final_action_returns_to_root_without_reloading()
    {
        var source = new CountingSource(Chat("A-1"), Chat("A-2"));
        var menu = Create(source, Config());
        menu.Reset();
        Assert.Equal(1, source.Loads);

        menu.Click(0);
        var r = menu.Click(0);

        Assert.IsType<MenuAction.FocusChat>(r.Action);
        Assert.Equal(1, source.Loads);
        Assert.Equal(["A-1", "A-2"], Labels(menu).Take(2));
    }

    [Fact]
    public void Concurrent_render_during_clicks_never_throws()
    {
        var menu = Menu(Chat("A-1"));
        Exception? renderError = null;
        using var stop = new CancellationTokenSource();
        var renderer = Task.Run(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested) Assert.Equal(8, menu.Render().Count);
            }
            catch (Exception e)
            {
                renderError = e;
            }
        });

        var deadline = DateTime.UtcNow.AddMilliseconds(200);
        while (DateTime.UtcNow < deadline)
        {
            menu.Click(0);
            menu.Click(2);
            menu.Click(7);
            menu.Click(7);
        }
        stop.Cancel();
        renderer.Wait();

        Assert.Null(renderError);
    }

    [Fact]
    public void Back_twice_from_submenu_returns_to_root()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        menu.Click(2);
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("VS Code", Labels(menu)[0]);
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("PROJ-1069", Labels(menu)[0]);
    }

    [Fact]
    public void Reset_from_submenu_returns_to_root()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        menu.Click(2);
        menu.Reset();
        Assert.Equal("PROJ-1069", Labels(menu)[0]);
    }

    [Fact]
    public void Back_to_root_keeps_the_chat_page()
    {
        var menu = MenuN(8, false, Enumerable.Range(1, 10).Select(i => Chat($"A-{i}")).ToArray());
        menu.Click(7);
        menu.Click(0);
        Assert.Equal("VS Code", Labels(menu)[0]);
        menu.Click(7);
        Assert.Equal("A-8", Labels(menu)[0]);
    }

    [Fact]
    public void Final_action_from_a_later_page_returns_to_the_first_page()
    {
        var menu = MenuN(8, false, Enumerable.Range(1, 10).Select(i => Chat($"A-{i}")).ToArray());
        menu.Click(7);
        menu.Click(0);
        var r = menu.Click(0);
        Assert.IsType<MenuAction.FocusChat>(r.Action);
        Assert.Equal("A-1", Labels(menu)[0]);
    }

    [Fact]
    public void Slot_count_comes_from_settings() => Assert.Equal(9, MenuN(9, true, Chat("A-1")).SlotCount);
}
