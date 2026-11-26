namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class LinksTests
{
    private readonly Links _links = new(new RingConfig { GitLabBaseUrl = "https://gitlab.example.com", GitLabGroup = "acme", JiraBaseUrl = "https://example.atlassian.net" });

    [Fact]
    public void Group_mrs_by_key() => Assert.Equal(
        "https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069",
        _links.GroupMrs("PROJ-1069"));

    [Fact]
    public void Group_mrs_by_key_and_target() => Assert.Equal(
        "https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=RC",
        _links.GroupMrs("PROJ-1069", "RC"));

    [Fact]
    public void Dashboard_mrs_without_group() => Assert.Equal(
        "https://gitlab.example.com/dashboard/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=develop",
        new Links(new RingConfig { GitLabBaseUrl = "https://gitlab.example.com" }).GroupMrs("PROJ-1069", "develop"));

    [Fact]
    public void Jira_link() => Assert.Equal("https://example.atlassian.net/browse/PROJ-1069", _links.JiraIssue("PROJ-1069"));

    [Fact]
    public void No_jira_without_base_url() => Assert.Null(new Links(new RingConfig()).JiraIssue("A-1"));

    [Fact]
    public void Repo_branch_link() => Assert.Equal(
        "https://gitlab.example.com/acme/api-service/-/tree/feature/PROJ-1069",
        _links.RepoBranch(new RepoRef("api-service", "/p", "feature/PROJ-1069", "https://gitlab.example.com/acme/api-service")));

    [Fact]
    public void Repo_branch_link_is_null_without_web_url() =>
        Assert.Null(_links.RepoBranch(new RepoRef("a", "/p", "b")));
}
