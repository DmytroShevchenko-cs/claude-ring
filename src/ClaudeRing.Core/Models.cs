namespace ClaudeRing.Core;

using System.Text.Json;

public sealed record ChatTab(string SessionId, string Title, string? WorkspaceFolder);

public sealed record SessionInfo(string? CustomTitle, string? AiTitle, string? GitBranch, string? Cwd);

public sealed record RepoRef(string Name, string Path, string Branch, string? WebUrl = null);

public sealed record ChatContext(ChatTab Tab, SessionInfo Session, string? TaskKey, IReadOnlyList<RepoRef> Repos)
{
    // Short label for a ring bubble: task key first, otherwise the best title.
    public string Label => TaskKey ?? Session.CustomTitle ?? Tab.Title;
}

public sealed record MrTarget(string Label, string Branch);

public sealed record RingConfig
{
    public List<string> RepoRoots { get; init; } = [Path.Combine(Home, "Projects")];
    public string GitLabBaseUrl { get; init; } = "https://gitlab.com";
    public string? GitLabGroup { get; init; }
    public string? JiraBaseUrl { get; init; }
    public List<MrTarget> MrTargets { get; init; } = [new("→ develop", "develop"), new("→ main", "main")];
    public string RiderPath { get; init; } = "/Applications/Rider.app/Contents/MacOS/rider";
    public int SlotCount { get; init; } = 8;
    public int ReopenDelayMs { get; init; } = 150;
    // Service event that means "the ring was closed". Chosen in the spike (Task 1). Empty = never reset by itself.
    public string ResetOnRingEvent { get; init; } = "";

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string DefaultPath => Path.Combine(Home, ".config", "claude-ring", "config.json");

    public static RingConfig Load(string path)
    {
        if (!File.Exists(path)) return new RingConfig();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<RingConfig>(File.ReadAllText(path), options) ?? new RingConfig();
    }
}
