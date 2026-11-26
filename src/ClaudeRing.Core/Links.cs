namespace ClaudeRing.Core;

public sealed class Links(RingConfig config)
{
    public string GroupMrs(string key, string? targetBranch = null)
    {
        var search = $"scope=all&state=all&search={Uri.EscapeDataString(key)}";
        var url = string.IsNullOrEmpty(config.GitLabGroup)
            ? $"{config.GitLabBaseUrl}/dashboard/merge_requests?{search}"
            : $"{config.GitLabBaseUrl}/groups/{config.GitLabGroup}/-/merge_requests?{search}";
        return targetBranch == null ? url : url + "&target_branch=" + Uri.EscapeDataString(targetBranch);
    }

    public string? JiraIssue(string key) =>
        config.JiraBaseUrl == null ? null : $"{config.JiraBaseUrl.TrimEnd('/')}/browse/{key}";

    // Link to the repo page on the repo's current branch. Null when the repo has no web URL.
    public string? RepoBranch(RepoRef repo) =>
        repo.WebUrl == null ? null : $"{repo.WebUrl}/-/tree/{string.Join('/', repo.Branch.Split('/').Select(Uri.EscapeDataString))}";
}
