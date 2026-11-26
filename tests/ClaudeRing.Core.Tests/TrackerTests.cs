namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TrackerTests
{
    private static readonly IReadOnlyList<RepoRef> NoRepos = [];

    [Fact]
    public void Factory_picks_the_tracker()
    {
        Assert.IsType<JiraTracker>(Trackers.Create(new TrackerSettings(TrackerType.Jira, "https://j")));
        Assert.IsType<YouTrackTracker>(Trackers.Create(new TrackerSettings(TrackerType.YouTrack, "https://y")));
        Assert.IsType<LinearTracker>(Trackers.Create(new TrackerSettings(TrackerType.Linear, Workspace: "acme")));
        Assert.IsType<GitHubIssuesTracker>(Trackers.Create(new TrackerSettings(TrackerType.GitHub)));
        Assert.IsType<NoTracker>(Trackers.Create(new TrackerSettings(TrackerType.None)));
    }

    [Fact]
    public void Jira_link()
    {
        var jira = new JiraTracker("https://example.atlassian.net/");
        Assert.Equal("Jira", jira.Name);
        Assert.Equal("https://example.atlassian.net/browse/PROJ-1069", jira.IssueUrl("PROJ-1069", NoRepos));
    }

    [Fact]
    public void Jira_without_base_url_has_no_link() => Assert.Null(new JiraTracker(null).IssueUrl("A-1", NoRepos));

    [Fact]
    public void Youtrack_link()
    {
        var youTrack = new YouTrackTracker("https://acme.youtrack.cloud");
        Assert.Equal("YouTrack", youTrack.Name);
        Assert.Equal("https://acme.youtrack.cloud/issue/PROJ-7", youTrack.IssueUrl("PROJ-7", NoRepos));
    }

    [Fact]
    public void Linear_link()
    {
        var linear = new LinearTracker("acme");
        Assert.Equal("Linear", linear.Name);
        Assert.Equal("https://linear.app/acme/issue/ENG-12", linear.IssueUrl("ENG-12", NoRepos));
    }

    [Fact]
    public void Linear_without_workspace_has_no_link() => Assert.Null(new LinearTracker(null).IssueUrl("ENG-12", NoRepos));

    [Fact]
    public void Github_issue_uses_the_first_repo_with_a_web_url()
    {
        var repos = new[] { new RepoRef("local", "/p/local", "123-fix"), new RepoRef("api", "/p/api", "123-fix", "https://github.com/acme/api") };
        var issues = new GitHubIssuesTracker();
        Assert.Equal("Issue", issues.Name);
        Assert.Equal("https://github.com/acme/api/issues/123", issues.IssueUrl("123", repos));
    }

    [Fact]
    public void Github_issue_needs_a_number_and_a_web_url()
    {
        var withUrl = new[] { new RepoRef("api", "/p/api", "b", "https://github.com/acme/api") };
        Assert.Null(new GitHubIssuesTracker().IssueUrl("PROJ-1", withUrl));
        Assert.Null(new GitHubIssuesTracker().IssueUrl("123", [new RepoRef("api", "/p/api", "b")]));
    }

    [Fact]
    public void No_tracker_has_no_link() => Assert.Null(new NoTracker().IssueUrl("A-1", NoRepos));
}
