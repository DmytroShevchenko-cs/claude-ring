namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class GitHostTests
{
    private static readonly GitSettings GitLab = new(GitHostType.GitLab, "https://gitlab.example.com", "acme", [ReviewTarget.Branch("main")]);
    private static readonly GitSettings GitHub = new(GitHostType.GitHub, "https://github.com", null, [ReviewTarget.Branch("main")]);

    [Fact]
    public void Factory_picks_the_host()
    {
        Assert.IsType<GitLabHost>(GitHosts.Create(GitLab));
        Assert.IsType<GitHubHost>(GitHosts.Create(GitHub));
    }

    [Fact]
    public void Review_labels()
    {
        Assert.Equal("MR", new GitLabHost(GitLab).ReviewsLabel);
        Assert.Equal("PR", new GitHubHost(GitHub).ReviewsLabel);
    }

    [Fact]
    public void Gitlab_group_mrs_by_key() => Assert.Equal(
        "https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069",
        new GitLabHost(GitLab).ReviewsUrl("PROJ-1069", null));

    [Fact]
    public void Gitlab_group_mrs_by_key_and_target() => Assert.Equal(
        "https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=RC",
        new GitLabHost(GitLab).ReviewsUrl("PROJ-1069", "RC"));

    [Fact]
    public void Gitlab_dashboard_mrs_without_group() => Assert.Equal(
        "https://gitlab.example.com/dashboard/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=develop",
        new GitLabHost(GitLab with { Group = null }).ReviewsUrl("PROJ-1069", "develop"));

    [Fact]
    public void Gitlab_branch_link() => Assert.Equal(
        "https://gitlab.example.com/acme/api-service/-/tree/feature/PROJ-1069",
        new GitLabHost(GitLab).BranchUrl(new RepoRef("api-service", "/p", "feature/PROJ-1069", "https://gitlab.example.com/acme/api-service")));

    [Fact]
    public void Branch_link_is_null_without_web_url()
    {
        Assert.Null(new GitLabHost(GitLab).BranchUrl(new RepoRef("a", "/p", "b")));
        Assert.Null(new GitHubHost(GitHub).BranchUrl(new RepoRef("a", "/p", "b")));
    }

    [Fact]
    public void Github_pulls_by_key() => Assert.Equal(
        "https://github.com/pulls?q=is:pr+PROJ-1069",
        new GitHubHost(GitHub).ReviewsUrl("PROJ-1069", null));

    [Fact]
    public void Github_pulls_by_key_target_and_org() => Assert.Equal(
        "https://github.com/pulls?q=is:pr+PROJ-1069+base:develop+org:acme",
        new GitHubHost(GitHub with { Group = "acme" }).ReviewsUrl("PROJ-1069", "develop"));

    [Fact]
    public void Github_enterprise_uses_base_url() => Assert.Equal(
        "https://ghe.example.com/pulls?q=is:pr+PROJ-1069",
        new GitHubHost(GitHub with { BaseUrl = "https://ghe.example.com" }).ReviewsUrl("PROJ-1069", null));

    [Fact]
    public void Github_branch_link() => Assert.Equal(
        "https://github.com/acme/api/tree/feature/PROJ-1069",
        new GitHubHost(GitHub).BranchUrl(new RepoRef("api", "/p", "feature/PROJ-1069", "https://github.com/acme/api")));
}
