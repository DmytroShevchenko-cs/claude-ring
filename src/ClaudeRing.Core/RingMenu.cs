namespace ClaudeRing.Core;

public abstract record MenuAction
{
    public sealed record FocusChat(ChatTab Tab) : MenuAction;
    public sealed record OpenRider(IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenIde(IdeSettings Ide, IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenUrl(string Url) : MenuAction;
    public sealed record OpenUrls(IReadOnlyList<string> Urls) : MenuAction;
    public sealed record Reveal(IReadOnlyList<string> Paths) : MenuAction;
    // Starts a program directly, without a shell.
    public sealed record Run(string File, IReadOnlyList<string> Args) : MenuAction;
}

public sealed record ClickResult(MenuAction? Action, bool Reopen);

public sealed record SlotView(string Label, bool Enabled);

public sealed class RingMenu(IChatSource source, Links links, RingConfig config)
{
    // One ring slot: what it shows and what a click does.
    private sealed record Slot(string Label, Func<ClickResult> OnClick);

    private const string Back = "← назад";
    private const string More = "ещё ›";

    private readonly object _lock = new();
    // Pages opened from the root; the top one is shown. Empty means the root page.
    private readonly Stack<MenuPage> _pages = new();
    private IReadOnlyList<ChatContext> _chats = [];
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
            ToRoot();
        }
    }

    public IReadOnlyList<SlotView> Render()
    {
        lock (_lock)
        {
            var slots = Slots();
            return Enumerable.Range(0, N)
                .Select(i => i < slots.Count && slots[i] != null ? new SlotView(slots[i]!.Label, true) : new SlotView("", false))
                .ToList();
        }
    }

    // A final action switches to the root page at once, under the lock. It does not reload chats:
    // the caller reloads chats after running the action, so the action never waits for the reload.
    public ClickResult Click(int slot)
    {
        ClickResult result;
        lock (_lock)
        {
            var slots = Slots();
            if (slot < 0 || slot >= slots.Count || slots[slot] == null) return new ClickResult(null, false);

            result = slots[slot]!.OnClick();
            if (!result.Reopen) ToRoot();
        }
        return result;
    }

    // Slots of the shown page. Null means an empty slot.
    private List<Slot?> Slots() => _pages.Count == 0 ? RootSlots() : PageSlots(_pages.Peek());

    private List<Slot?> RootSlots()
    {
        var perPage = PerPage();
        var page = CurrentPage();
        var pageChats = _chats.Skip(page * perPage).Take(perPage)
            .Select(c => (Slot?)new Slot(c.Label, () => Push(ChatPage(c)))).ToList();
        if (_chats.Count > N)
        {
            while (pageChats.Count < N - 1) pageChats.Add(null);
            pageChats.Add(new Slot(More, () => { _page = (CurrentPage() + 1) % PageCount(); return new ClickResult(null, true); }));
        }
        return pageChats;
    }

    private int PerPage() => _chats.Count > N ? N - 1 : N;

    private int PageCount() => Math.Max(1, (int)Math.Ceiling(_chats.Count / (double)PerPage()));

    // An out-of-range page falls back to the first one.
    private int CurrentPage() => _page >= 0 && _page < PageCount() ? _page : 0;

    // Page items in order; "back" goes into the last slot.
    private List<Slot?> PageSlots(MenuPage page)
    {
        var slots = page.Items.Take(N - 1).Select(i => (Slot?)new Slot(i.Label, () => Open(i))).ToList();
        while (slots.Count < N - 1) slots.Add(null);
        slots.Add(new Slot(Back, Pop));
        return slots;
    }

    // A submenu goes on top of the stack; a final action goes back to the caller.
    private ClickResult Open(MenuItem item) =>
        item.Submenu != null ? Push(item.Submenu) : new ClickResult(item.Action, false);

    private ClickResult Push(MenuPage page)
    {
        _pages.Push(page);
        return new ClickResult(null, true);
    }

    private ClickResult Pop()
    {
        _pages.TryPop(out _);
        return new ClickResult(null, true);
    }

    private void ToRoot()
    {
        _pages.Clear();
        _page = 0;
    }

    private MenuPage ChatPage(ChatContext c)
    {
        var items = new List<MenuItem> { new("VS Code", new MenuAction.FocusChat(c.Tab)) };
        if (c.TaskKey == null) return new MenuPage(items);
        if (c.Repos.Count > 0) items.Add(new("Rider", Submenu: RiderPage(c)));
        items.Add(new("MR", Submenu: MrPage(c)));
        if (links.JiraIssue(c.TaskKey) is { } jira) items.Add(new("Jira", new MenuAction.OpenUrl(jira)));
        if (c.Repos.Count > 0) items.Add(new("Репо", Submenu: RepoPage(c)));
        return new MenuPage(items);
    }

    private MenuPage MrPage(ChatContext c)
    {
        var items = new List<MenuItem> { new("Все MR", new MenuAction.OpenUrl(links.GroupMrs(c.TaskKey!))) };
        items.AddRange(config.MrTargets.Select(t => new MenuItem(t.Label, new MenuAction.OpenUrl(links.GroupMrs(c.TaskKey!, t.Branch)))));
        return new MenuPage(items);
    }

    // One repo per slot, then "Все"; repos that do not fit are reachable only through "Все".
    private MenuPage RiderPage(ChatContext c)
    {
        var items = c.Repos.Take(N - 2).Select(r => new MenuItem(MenuBuilder.DisplayName(r, c.TaskKey), new MenuAction.OpenRider([r]))).ToList();
        items.Add(new("Все", new MenuAction.OpenRider(c.Repos)));
        return new MenuPage(items);
    }

    private MenuPage RepoPage(ChatContext c)
    {
        var items = c.Repos.Take(N - 3).Select(r => new MenuItem(MenuBuilder.DisplayName(r, c.TaskKey),
            links.RepoBranch(r) is { } url ? new MenuAction.OpenUrl(url) : new MenuAction.OpenRider([r]))).ToList();
        var urls = c.Repos.Select(links.RepoBranch).OfType<string>().ToList();
        if (urls.Count > 0) items.Add(new("Все в GitLab", new MenuAction.OpenUrls(urls)));
        items.Add(new("Папка в Finder", new MenuAction.Reveal([MenuBuilder.CommonParent(c.Repos)])));
        return new MenuPage(items);
    }
}
