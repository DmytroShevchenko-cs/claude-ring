namespace ClaudeRing.Core;

public abstract record MenuAction
{
    public sealed record FocusChat(ChatTab Tab) : MenuAction;
    public sealed record OpenRider(IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenUrl(string Url) : MenuAction;
    public sealed record OpenUrls(IReadOnlyList<string> Urls) : MenuAction;
    public sealed record Reveal(IReadOnlyList<string> Paths) : MenuAction;
}

public sealed record ClickResult(MenuAction? Action, bool Reopen);

public sealed record SlotView(string Label, bool Enabled);

public sealed class RingMenu(IChatSource source, Links links, RingConfig config)
{
    private enum Level { Root, Chat, Mr, Repos, Rider }

    // One ring item: what it shows and what a click does.
    private sealed record Item(string Label, Func<ClickResult> OnClick);

    private const string Back = "← назад";
    private const string More = "ещё ›";

    private readonly object _lock = new();
    private IReadOnlyList<ChatContext> _chats = [];
    private Level _level = Level.Root;
    private ChatContext? _chat;
    private int _page;

    private int N => config.SlotCount;

    // The plugin logs this after Reset. Null after a successful load.
    public Exception? LastLoadError { get; private set; }

    // Never throws. Loads outside the lock; if loading fails, the previous chat list stays.
    public void Reset()
    {
        IReadOnlyList<ChatContext>? loaded = null;
        Exception? error = null;
        try
        {
            loaded = source.Load();
        }
        catch (Exception e)
        {
            error = e;
        }

        lock (_lock)
        {
            if (loaded != null) _chats = loaded;
            LastLoadError = error;
            _level = Level.Root;
            _chat = null;
            _page = 0;
        }
    }

    public IReadOnlyList<SlotView> Render()
    {
        lock (_lock)
        {
            var items = Items();
            return Enumerable.Range(0, N)
                .Select(i => i < items.Count && items[i] != null ? new SlotView(items[i]!.Label, true) : new SlotView("", false))
                .ToList();
        }
    }

    // A final action switches to the root level at once, under the lock. It does not reload chats:
    // the caller reloads chats after running the action, so the action never waits for the reload.
    public ClickResult Click(int slot)
    {
        ClickResult result;
        lock (_lock)
        {
            var items = Items();
            if (slot < 0 || slot >= items.Count || items[slot] == null) return new ClickResult(null, false);

            result = items[slot]!.OnClick();
            if (!result.Reopen)
            {
                _level = Level.Root;
                _chat = null;
                _page = 0;
            }
        }
        return result;
    }

    // Builds the slot list for the current level. Null means an empty slot.
    private List<Item?> Items() => _level switch
    {
        Level.Root => RootItems(),
        Level.Chat => WithBack(ChatItems(_chat!)),
        Level.Mr => WithBack(MrItems(_chat!)),
        Level.Repos => WithBack(RepoItems(_chat!)),
        Level.Rider => WithBack(RiderItems(_chat!)),
        _ => [],
    };

    private List<Item?> RootItems()
    {
        var perPage = PerPage();
        var page = CurrentPage();
        var pageChats = _chats.Skip(page * perPage).Take(perPage)
            .Select(c => (Item?)new Item(c.Label, () => Go(Level.Chat, c))).ToList();
        if (_chats.Count > N)
        {
            while (pageChats.Count < N - 1) pageChats.Add(null);
            pageChats.Add(new Item(More, () => { _page = (CurrentPage() + 1) % PageCount(); return new ClickResult(null, true); }));
        }
        return pageChats;
    }

    private int PerPage() => _chats.Count > N ? N - 1 : N;

    private int PageCount() => Math.Max(1, (int)Math.Ceiling(_chats.Count / (double)PerPage()));

    // An out-of-range page falls back to the first one.
    private int CurrentPage() => _page >= 0 && _page < PageCount() ? _page : 0;

    private List<Item> ChatItems(ChatContext c)
    {
        var items = new List<Item> { new("VS Code", () => Done(new MenuAction.FocusChat(c.Tab))) };
        if (c.TaskKey == null) return items;
        if (c.Repos.Count > 0) items.Add(new("Rider", () => Go(Level.Rider, c)));
        items.Add(new("MR", () => Go(Level.Mr, c)));
        if (links.JiraIssue(c.TaskKey) is { } jira) items.Add(new("Jira", () => Done(new MenuAction.OpenUrl(jira))));
        if (c.Repos.Count > 0) items.Add(new("Репо", () => Go(Level.Repos, c)));
        return items;
    }

    private List<Item> MrItems(ChatContext c)
    {
        var items = new List<Item> { new("Все MR", () => Done(new MenuAction.OpenUrl(links.GroupMrs(c.TaskKey!)))) };
        items.AddRange(config.MrTargets.Select(t => new Item(t.Label, () => Done(new MenuAction.OpenUrl(links.GroupMrs(c.TaskKey!, t.Branch))))));
        return items;
    }

    // One repo per slot, then "Все"; repos that do not fit are reachable only through "Все".
    private List<Item> RiderItems(ChatContext c)
    {
        var items = c.Repos.Take(N - 2).Select(r => new Item(DisplayName(r), () => Done(new MenuAction.OpenRider([r])))).ToList();
        items.Add(new("Все", () => Done(new MenuAction.OpenRider(c.Repos))));
        return items;
    }

    // Repo folder name without the trailing "-<TaskKey>" (old flat layout).
    private string DisplayName(RepoRef r)
    {
        var suffix = "-" + _chat!.TaskKey;
        return _chat.TaskKey != null && r.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && r.Name.Length > suffix.Length
            ? r.Name[..^suffix.Length]
            : r.Name;
    }

    private List<Item> RepoItems(ChatContext c)
    {
        var items = c.Repos.Take(N - 3).Select(r => new Item(DisplayName(r), () =>
            Done(links.RepoBranch(r) is { } url ? new MenuAction.OpenUrl(url) : new MenuAction.OpenRider([r])))).ToList();
        var urls = c.Repos.Select(links.RepoBranch).OfType<string>().ToList();
        if (urls.Count > 0) items.Add(new("Все в GitLab", () => Done(new MenuAction.OpenUrls(urls))));
        items.Add(new("Папка в Finder", () => Done(new MenuAction.Reveal([CommonParent(c.Repos)]))));
        return items;
    }

    // Deepest directory that contains the parent folders of all repos.
    private static string CommonParent(IReadOnlyList<RepoRef> repos)
    {
        var parts = repos
            .Select(r => (Path.GetDirectoryName(r.Path) ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var common = parts[0].AsEnumerable();
        foreach (var p in parts.Skip(1)) common = common.Zip(p).TakeWhile(x => x.First == x.Second).Select(x => x.First);
        return "/" + string.Join('/', common);
    }

    // Puts "back" into the last slot, the rest stays in order.
    private List<Item?> WithBack(List<Item> items)
    {
        var slots = items.Take(N - 1).Cast<Item?>().ToList();
        while (slots.Count < N - 1) slots.Add(null);
        slots.Add(new Item(Back, GoBack));
        return slots;
    }

    private ClickResult Go(Level level, ChatContext chat)
    {
        _chat = chat;
        _level = level;
        return new ClickResult(null, true);
    }

    private ClickResult GoBack()
    {
        _level = _level == Level.Chat ? Level.Root : Level.Chat;
        return new ClickResult(null, true);
    }

    private static ClickResult Done(MenuAction action) => new(action, false);
}
