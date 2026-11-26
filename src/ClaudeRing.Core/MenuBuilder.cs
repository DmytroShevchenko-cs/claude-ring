namespace ClaudeRing.Core;

// Builds the chat page and its submenus from settings.
// Items whose data is missing are left out, so a page never shows dead slots.
public sealed class MenuBuilder
{
    private readonly Settings _settings;
    private readonly IGitHost _git;
    private readonly ITracker _tracker;
    private readonly Func<RepoRef, string, bool> _hasRemoteBranch;

    public MenuBuilder(Settings settings, Strings strings, Func<RepoRef, string, bool>? hasRemoteBranch = null)
    {
        _settings = settings;
        Strings = strings;
        _git = GitHosts.Create(settings.Git);
        _tracker = Trackers.Create(settings.Tracker);
        _hasRemoteBranch = hasRemoteBranch ?? ((r, b) => RepoFinder.HasRemoteBranch(r.Path, b));
    }

    public Strings Strings { get; }

    public int SlotCount => _settings.SlotCount;

    private int N => _settings.SlotCount;

    public MenuPage ChatPage(ChatContext chat)
    {
        var values = ChatValues(chat);
        var items = new List<MenuItem>();
        foreach (var entry in _settings.ChatMenu)
        {
            var item = entry switch
            {
                MenuEntry.BuiltIn b => BuiltIn(b.Id, chat),
                MenuEntry.Custom c => Custom(c, chat, values),
                _ => null,
            };
            if (item != null) items.Add(item);
        }
        return new MenuPage(items);
    }

    private MenuItem? BuiltIn(string id, ChatContext c) => id switch
    {
        "chat" => new MenuItem(Strings.Chat, new MenuAction.FocusChat(c.Tab)),
        "ide" => IdeItem(c),
        "reviews" => ReviewsItem(c),
        "issue" => IssueItem(c),
        "repos" => ReposItem(c),
        _ => null,
    };

    // One repo per slot, then "All"; repos that do not fit are reachable only through "All".
    private MenuItem? IdeItem(ChatContext c)
    {
        if (_settings.Ide.Type == IdeType.None || c.Repos.Count == 0) return null;
        var items = c.Repos.Take(N - 2)
            .Select(r => new MenuItem(DisplayName(r, c.TaskKey), new MenuAction.OpenIde(_settings.Ide, [r])))
            .ToList();
        items.Add(new MenuItem(Strings.All, new MenuAction.OpenIde(_settings.Ide, c.Repos)));
        return new MenuItem(IdeLauncher.DisplayName(_settings.Ide), Submenu: new MenuPage(items));
    }

    private MenuItem? ReviewsItem(ChatContext c)
    {
        if (c.TaskKey is not { } key) return null;
        var items = new List<MenuItem>
        {
            new(Strings.AllReviews(_git.ReviewsLabel), new MenuAction.OpenUrl(_git.ReviewsUrl(key, null))),
        };
        items.AddRange(_settings.Git.Targets.Take(N - 2).Select(t => new MenuItem(Strings.Target(t.Label), TargetAction(key, t, c.Repos))));
        return new MenuItem(_git.ReviewsLabel, Submenu: new MenuPage(items));
    }

    // Several branch names: open only the ones some repo has on origin; if none is found, open all of them.
    private MenuAction TargetAction(string key, ReviewTarget target, IReadOnlyList<RepoRef> repos)
    {
        var branches = target.Branches;
        if (branches.Count > 1)
        {
            var found = branches.Where(b => repos.Any(r => _hasRemoteBranch(r, b))).ToList();
            if (found.Count > 0) branches = found;
        }
        return branches.Count == 1
            ? new MenuAction.OpenUrl(_git.ReviewsUrl(key, branches[0]))
            : new MenuAction.OpenUrls(branches.Select(b => _git.ReviewsUrl(key, b)).ToList());
    }

    private MenuItem? IssueItem(ChatContext c) =>
        c.TaskKey is { } key && _tracker.IssueUrl(key, c.Repos) is { } url
            ? new MenuItem(_tracker.Name, new MenuAction.OpenUrl(url))
            : null;

    private MenuItem? ReposItem(ChatContext c)
    {
        if (c.Repos.Count == 0) return null;
        var items = c.Repos.Take(N - 3).Select(r => new MenuItem(DisplayName(r, c.TaskKey), RepoAction(r))).ToList();
        var urls = c.Repos.Select(_git.BranchUrl).OfType<string>().ToList();
        if (urls.Count > 0) items.Add(new MenuItem(Strings.AllInBrowser, new MenuAction.OpenUrls(urls)));
        items.Add(new MenuItem(Strings.FolderInFinder, new MenuAction.Reveal([CommonParent(c.Repos)])));
        return new MenuItem(Strings.Repos, Submenu: new MenuPage(items));
    }

    // Branch page in the browser. Without a web URL: the IDE, or Finder when no IDE is set.
    private MenuAction RepoAction(RepoRef r)
    {
        if (_git.BranchUrl(r) is { } url) return new MenuAction.OpenUrl(url);
        if (_settings.Ide.Type != IdeType.None) return new MenuAction.OpenIde(_settings.Ide, [r]);
        return new MenuAction.Reveal([r.Path]);
    }

    // A perRepo item opens a submenu of repos; repos without the needed values are left out.
    private MenuItem? Custom(MenuEntry.Custom item, ChatContext c, Dictionary<string, string?> chatValues)
    {
        if (!item.PerRepo)
            return CustomAction(item, chatValues) is { } action ? new MenuItem(item.Label, action) : null;

        var items = new List<MenuItem>();
        foreach (var r in c.Repos)
        {
            if (items.Count == N - 1) break;
            if (CustomAction(item, RepoValues(chatValues, r, c.TaskKey)) is { } action)
                items.Add(new MenuItem(DisplayName(r, c.TaskKey), action));
        }
        return items.Count == 0 ? null : new MenuItem(item.Label, Submenu: new MenuPage(items));
    }

    // Null when a placeholder has no value.
    private static MenuAction? CustomAction(MenuEntry.Custom item, IReadOnlyDictionary<string, string?> values)
    {
        if (item.Url != null)
            return Template.Render(item.Url, values, forUrl: true) is { } url ? new MenuAction.OpenUrl(url) : null;
        if (item.Run is not { Count: > 0 } run) return null;
        var args = new List<string>();
        foreach (var arg in run.Skip(1))
        {
            if (Template.Render(arg, values, forUrl: false) is not { } value) return null;
            args.Add(value);
        }
        return new MenuAction.Run(run[0], args);
    }

    private static Dictionary<string, string?> ChatValues(ChatContext c) => new()
    {
        ["key"] = c.TaskKey,
        ["branch"] = c.Session.GitBranch ?? c.Repos.FirstOrDefault()?.Branch,
        ["title"] = c.Session.CustomTitle ?? (c.Tab.Title.Length > 0 ? c.Tab.Title : c.Session.AiTitle),
        ["sessionId"] = c.Tab.SessionId,
        ["repos.parent"] = c.Repos.Count > 0 ? CommonParent(c.Repos) : null,
    };

    private static Dictionary<string, string?> RepoValues(Dictionary<string, string?> chat, RepoRef r, string? key) =>
        new(chat)
        {
            ["repo.name"] = DisplayName(r, key),
            ["repo.path"] = r.Path,
            ["repo.webUrl"] = r.WebUrl,
            ["repo.branch"] = r.Branch,
        };

    // Repo folder name without the trailing "-<TaskKey>" (old flat layout).
    internal static string DisplayName(RepoRef r, string? key)
    {
        var suffix = "-" + key;
        return key != null && r.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && r.Name.Length > suffix.Length
            ? r.Name[..^suffix.Length]
            : r.Name;
    }

    // Deepest directory that contains the parent folders of all repos.
    internal static string CommonParent(IReadOnlyList<RepoRef> repos)
    {
        var parts = repos
            .Select(r => (Path.GetDirectoryName(r.Path) ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var common = parts[0].AsEnumerable();
        foreach (var p in parts.Skip(1)) common = common.Zip(p).TakeWhile(x => x.First == x.Second).Select(x => x.First);
        return "/" + string.Join('/', common);
    }
}
