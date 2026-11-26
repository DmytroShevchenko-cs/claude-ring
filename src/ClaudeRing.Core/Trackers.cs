namespace ClaudeRing.Core;

public interface ITracker
{
    // Slot label of the issue item.
    string Name { get; }

    // Null when the link cannot be built (missing settings or a key the tracker does not use).
    string? IssueUrl(string key, IReadOnlyList<RepoRef> repos);
}

public static class Trackers
{
    public static ITracker Create(TrackerSettings tracker) => tracker.Type switch
    {
        TrackerType.Jira => new JiraTracker(tracker.BaseUrl),
        TrackerType.YouTrack => new YouTrackTracker(tracker.BaseUrl),
        TrackerType.Linear => new LinearTracker(tracker.Workspace),
        TrackerType.GitHub => new GitHubIssuesTracker(),
        _ => new NoTracker(),
    };
}

public sealed class JiraTracker(string? baseUrl) : ITracker
{
    public string Name => "Jira";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) =>
        string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(key)}";
}

public sealed class YouTrackTracker(string? baseUrl) : ITracker
{
    public string Name => "YouTrack";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) =>
        string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/issue/{Uri.EscapeDataString(key)}";
}

public sealed class LinearTracker(string? workspace) : ITracker
{
    public string Name => "Linear";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) =>
        string.IsNullOrEmpty(workspace)
            ? null
            : $"https://linear.app/{Uri.EscapeDataString(workspace)}/issue/{Uri.EscapeDataString(key)}";
}

// The key must be the issue number, e.g. from "taskKeyPattern": "#(\\d+)".
public sealed class GitHubIssuesTracker : ITracker
{
    public string Name => "Issue";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos)
    {
        if (key.Length == 0 || !key.All(char.IsAsciiDigit)) return null;
        var webUrl = repos.FirstOrDefault(r => r.WebUrl != null)?.WebUrl;
        return webUrl == null ? null : $"{webUrl}/issues/{key}";
    }
}

public sealed class NoTracker : ITracker
{
    public string Name => "";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) => null;
}
