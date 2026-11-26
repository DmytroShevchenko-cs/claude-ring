namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class RepoFinderTests
{
    private static void Repo(TempDir dir, string path, string branch) =>
        dir.Write($"{path}/.git/HEAD", $"ref: refs/heads/{branch}\n");

    private static void Worktree(TempDir dir, string path, string branch)
    {
        var gitDir = dir.Write($"_gitdirs/{path}/HEAD", $"ref: refs/heads/{branch}\n");
        dir.Write($"{path}/.git", $"gitdir: {Path.GetDirectoryName(gitDir)}\n");
    }

    [Fact]
    public void Finds_both_layouts_and_skips_similar_keys()
    {
        using var dir = new TempDir();
        Repo(dir, "api-service", "fix/PROJ-1142");
        Worktree(dir, "web-app-domain-PROJ-1069", "feature/PROJ-1069");
        Worktree(dir, "PROJ-1069/auth-service", "feature/PROJ-1069");
        Repo(dir, "other-PROJ-10690", "feature/PROJ-10690");

        var repos = new RepoFinder([dir.Path]).Find("PROJ-1069");

        Assert.Equal(["auth-service", "web-app-domain-PROJ-1069"], repos.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Detached_head_is_skipped()
    {
        using var dir = new TempDir();
        dir.Write("repo-PROJ-1/.git/HEAD", "0123456789abcdef0123456789abcdef01234567\n");

        Assert.Empty(new RepoFinder([dir.Path]).Find("PROJ-1"));
    }

    [Fact]
    public void Relative_gitdir_is_resolved()
    {
        using var dir = new TempDir();
        dir.Write("_gitdirs/x/HEAD", "ref: refs/heads/feature/PROJ-5\n");
        dir.Write("repo-PROJ-5/.git", "gitdir: ../_gitdirs/x\n");

        var repos = new RepoFinder([dir.Path]).Find("PROJ-5");

        Assert.Equal("feature/PROJ-5", Assert.Single(repos).Branch);
    }

    [Fact]
    public void Unreadable_head_is_skipped()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "bad-PROJ-6", ".git", "HEAD"));
        Repo(dir, "good-PROJ-6", "feature/PROJ-6");

        var repos = new RepoFinder([dir.Path]).Find("PROJ-6");

        Assert.Equal("good-PROJ-6", Assert.Single(repos).Name);
    }

    [Fact]
    public void Web_url_from_scp_style_origin()
    {
        using var dir = new TempDir();
        Repo(dir, "r-A-1", "feature/A-1");
        dir.Write("r-A-1/.git/config", "[core]\n\tbare = false\n[remote \"origin\"]\n\turl = git@gitlab.example.com:acme/api-service.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");

        Assert.Equal("https://gitlab.example.com/acme/api-service", Assert.Single(new RepoFinder([dir.Path]).Find("A-1")).WebUrl);
    }

    [Fact]
    public void Web_url_from_worktree_common_config_with_ssh_port()
    {
        using var dir = new TempDir();
        dir.Write("main/.git/config", "[remote \"origin\"]\n\turl=ssh://git@gitlab.example.com:2222/acme/web-app.git\n");
        dir.Write("main/.git/worktrees/wt/HEAD", "ref: refs/heads/feature/A-2\n");
        dir.Write("main/.git/worktrees/wt/commondir", "../..\n");
        dir.Write("A-2/wt/.git", $"gitdir: {Path.Combine(dir.Path, "main/.git/worktrees/wt")}\n");

        var repos = new RepoFinder([Path.Combine(dir.Path, "A-2")]).Find("A-2");

        Assert.Equal("https://gitlab.example.com/acme/web-app", Assert.Single(repos).WebUrl);
    }

    [Fact]
    public void Web_url_from_https_with_user_info()
    {
        using var dir = new TempDir();
        Repo(dir, "r-A-3", "feature/A-3");
        dir.Write("r-A-3/.git/config", "[remote \"origin\"]\n    url = https://user@gitlab.example.com/acme/x.git/\n");

        Assert.Equal("https://gitlab.example.com/acme/x", Assert.Single(new RepoFinder([dir.Path]).Find("A-3")).WebUrl);
    }

    [Fact]
    public void Web_url_is_null_without_origin()
    {
        using var dir = new TempDir();
        Repo(dir, "r-A-4", "feature/A-4");
        dir.Write("r-A-4/.git/config", "[remote \"upstream\"]\n\turl = git@h:a/b.git\n");

        Assert.Null(Assert.Single(new RepoFinder([dir.Path]).Find("A-4")).WebUrl);
    }

    [Fact]
    public void Remote_branch_from_loose_ref_packed_refs_and_worktree()
    {
        using var dir = new TempDir();
        dir.Write("a/.git/HEAD", "ref: refs/heads/x\n");
        dir.Write("a/.git/refs/remotes/origin/master", "0000000000000000000000000000000000000000\n");
        dir.Write("b/.git/HEAD", "ref: refs/heads/x\n");
        dir.Write("b/.git/packed-refs", "# pack-refs with: peeled fully-peeled sorted\n1111111111111111111111111111111111111111 refs/remotes/origin/main\n");
        dir.Write("main/.git/worktrees/wt/HEAD", "ref: refs/heads/x\n");
        dir.Write("main/.git/worktrees/wt/commondir", "../..\n");
        dir.Write("main/.git/refs/remotes/origin/main", "2222222222222222222222222222222222222222\n");
        dir.Write("wt/.git", $"gitdir: {Path.Combine(dir.Path, "main/.git/worktrees/wt")}\n");

        Assert.True(RepoFinder.HasRemoteBranch(Path.Combine(dir.Path, "a"), "master"));
        Assert.False(RepoFinder.HasRemoteBranch(Path.Combine(dir.Path, "a"), "main"));
        Assert.True(RepoFinder.HasRemoteBranch(Path.Combine(dir.Path, "b"), "main"));
        Assert.False(RepoFinder.HasRemoteBranch(Path.Combine(dir.Path, "b"), "master"));
        Assert.True(RepoFinder.HasRemoteBranch(Path.Combine(dir.Path, "wt"), "main"));
        Assert.False(RepoFinder.HasRemoteBranch(Path.Combine(dir.Path, "missing"), "main"));
    }
}
