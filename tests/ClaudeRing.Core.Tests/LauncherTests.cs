namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class LauncherTests
{
    private readonly FakeProcessRunner _runner = new();
    private Launcher Launcher() => new(_runner, _ => { });

    [Fact]
    public void Focus_chat_raises_window_then_opens_session()
    {
        Launcher().FocusChat(new ChatTab("11111111-aaaa-4bbb-8ccc-000000000001", "PROJ-1069", "/Users/me/proj"));
        Assert.Equal(["open", "-b", "com.microsoft.VSCode", "/Users/me/proj"], _runner.Started[0]);
        Assert.Equal(["open", "vscode://anthropic.claude-code/open?session=11111111-aaaa-4bbb-8ccc-000000000001"], _runner.Started[1]);
    }

    [Fact]
    public void Open_urls_uses_one_call()
    {
        Launcher().OpenUrls(["https://a", "https://b"]);
        Assert.Equal(["open", "https://a", "https://b"], _runner.Started.Single());
    }

    [Fact]
    public void Open_urls_does_nothing_for_empty_list()
    {
        Launcher().OpenUrls([]);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Open_ide_starts_every_command()
    {
        using var dir = new TempDir();
        var app = Path.Combine(dir.Path, "Cursor.app");
        Directory.CreateDirectory(app);
        Launcher().OpenIde(new IdeSettings(IdeType.Cursor, app), [new RepoRef("a", "/p/a", "b"), new RepoRef("c", "/p/c", "b")]);
        Assert.Equal(2, _runner.Started.Count);
        Assert.Equal([$"{app}/Contents/Resources/app/bin/cursor", "-n", "/p/a"], _runner.Started[0]);
        Assert.Equal([$"{app}/Contents/Resources/app/bin/cursor", "-n", "/p/c"], _runner.Started[1]);
    }

    [Fact]
    public void Open_ide_with_missing_app_throws_with_the_path_and_starts_nothing()
    {
        using var dir = new TempDir();
        var app = Path.Combine(dir.Path, "Nope.app");
        var ex = Assert.Throws<FileNotFoundException>(() =>
            Launcher().OpenIde(new IdeSettings(IdeType.Custom, app), [new RepoRef("a", "/p/a", "b")]));
        Assert.Contains(app, ex.Message);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Open_ide_with_existing_custom_app_runs_open()
    {
        using var dir = new TempDir();
        var app = Path.Combine(dir.Path, "X.app");
        Directory.CreateDirectory(app);
        Launcher().OpenIde(new IdeSettings(IdeType.Custom, app), [new RepoRef("a", "/p/a", "b")]);
        Assert.Equal(["open", "-a", app, "/p/a"], _runner.Started.Single());
    }

    [Fact]
    public void Open_ide_with_custom_app_name_leaves_the_lookup_to_open()
    {
        Launcher().OpenIde(new IdeSettings(IdeType.Custom, "Some Editor"), [new RepoRef("a", "/p/a", "b")]);
        Assert.Equal(["open", "-a", "Some Editor", "/p/a"], _runner.Started.Single());
    }

    [Fact]
    public void Open_ide_none_does_nothing()
    {
        Launcher().OpenIde(new IdeSettings(IdeType.None), [new RepoRef("a", "/p/a", "b")]);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Run_starts_the_program_with_arguments()
    {
        Launcher().Run("open", ["-a", "Terminal", "/p/a"]);
        Assert.Equal(["open", "-a", "Terminal", "/p/a"], _runner.Started.Single());
    }
}
