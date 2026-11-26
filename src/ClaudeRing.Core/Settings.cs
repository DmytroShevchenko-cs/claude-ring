namespace ClaudeRing.Core;

public enum UiLanguage { Auto, En, Ru }

public enum GitHostType { GitLab, GitHub }

public enum TrackerType { None, Jira, Linear, YouTrack, GitHub }

public enum IdeType { None, Rider, Idea, WebStorm, PyCharm, GoLand, CLion, RubyMine, PhpStorm, VsCode, Cursor, Windsurf, Custom }

// One item of the reviews submenu. Several branch names cover repos that name the same branch differently (master / main).
public sealed record ReviewTarget(string Label, IReadOnlyList<string> Branches)
{
    public static ReviewTarget Branch(string name) => new(name, [name]);
}

// BaseUrl has no trailing "/".
public sealed record GitSettings(GitHostType Host, string BaseUrl, string? Group, IReadOnlyList<ReviewTarget> Targets);

public sealed record TrackerSettings(TrackerType Type, string? BaseUrl = null, string? Workspace = null);

// App null means the default app of the IDE type.
public sealed record IdeSettings(IdeType Type, string? App = null);

// One item of the chat page.
public abstract record MenuEntry
{
    public static readonly IReadOnlyList<string> BuiltInIds = ["chat", "ide", "reviews", "issue", "repos"];

    public sealed record BuiltIn(string Id) : MenuEntry;

    // Exactly one of Url and Run is set. Run holds the program first, then argument templates.
    public sealed record Custom(string Label, string? Url, IReadOnlyList<string>? Run, bool PerRepo) : MenuEntry;
}

public sealed record Settings
{
    // A Jira-like key such as PROJ-123, not inside a longer word or number.
    public const string DefaultTaskKeyPattern = @"(?<![A-Za-z0-9])[A-Z][A-Z0-9]+-\d+(?!\d)";

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DefaultPath => Path.Combine(Home, ".config", "claude-ring", "config.json");

    public UiLanguage Language { get; init; } = UiLanguage.Auto;

    public IReadOnlyList<string> RepoRoots { get; init; } = [Path.Combine(Home, "Projects")];

    public string TaskKeyPattern { get; init; } = DefaultTaskKeyPattern;

    public GitSettings Git { get; init; } = new(GitHostType.GitLab, "https://gitlab.com", null, [ReviewTarget.Branch("main")]);

    public TrackerSettings Tracker { get; init; } = new(TrackerType.None);

    public IdeSettings Ide { get; init; } = new(IdeType.None);

    public IReadOnlyList<MenuEntry> ChatMenu { get; init; } =
        MenuEntry.BuiltInIds.Select(id => (MenuEntry)new MenuEntry.BuiltIn(id)).ToList();

    public int SlotCount { get; init; } = 8;

    public int ReopenDelayMs { get; init; } = 150;

    // Ring service event that means "the ring was closed". Empty = never reset by itself.
    public string ResetOnRingEvent { get; init; } = "";
}
