namespace ClaudeRing.Core;

public interface IGitHost
{
    // Slot label of the reviews item: "MR" or "PR".
    string ReviewsLabel { get; }

    // Repo page on its current branch. Null when the repo has no web URL.
    string? BranchUrl(RepoRef repo);

    // All reviews for the task key, or only those into the target branch.
    string ReviewsUrl(string key, string? target);
}

public static class GitHosts
{
    public static IGitHost Create(GitSettings git) =>
        git.Host == GitHostType.GitHub ? new GitHubHost(git) : new GitLabHost(git);
}

public sealed class GitLabHost(GitSettings git) : IGitHost
{
    public string ReviewsLabel => "MR";

    public string? BranchUrl(RepoRef repo) =>
        repo.WebUrl == null ? null : $"{repo.WebUrl}/-/tree/{Template.EscapePath(repo.Branch)}";

    public string ReviewsUrl(string key, string? target)
    {
        var search = $"scope=all&state=all&search={Uri.EscapeDataString(key)}";
        var url = string.IsNullOrEmpty(git.Group)
            ? $"{git.BaseUrl}/dashboard/merge_requests?{search}"
            : $"{git.BaseUrl}/groups/{git.Group}/-/merge_requests?{search}";
        return target == null ? url : url + "&target_branch=" + Uri.EscapeDataString(target);
    }
}

public sealed class GitHubHost(GitSettings git) : IGitHost
{
    public string ReviewsLabel => "PR";

    public string? BranchUrl(RepoRef repo) =>
        repo.WebUrl == null ? null : $"{repo.WebUrl}/tree/{Template.EscapePath(repo.Branch)}";

    public string ReviewsUrl(string key, string? target)
    {
        var query = "is:pr+" + Uri.EscapeDataString(key);
        if (target != null) query += "+base:" + Uri.EscapeDataString(target);
        if (!string.IsNullOrEmpty(git.Group)) query += "+org:" + Uri.EscapeDataString(git.Group);
        return $"{git.BaseUrl}/pulls?q={query}";
    }
}
