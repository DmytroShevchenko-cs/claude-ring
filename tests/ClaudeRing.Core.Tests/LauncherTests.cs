namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class LauncherTests
{
    private readonly FakeProcessRunner _runner = new();
    private Launcher Launcher() => new(_runner, new RingConfig(), _ => { });

    [Fact]
    public void Focus_chat_raises_window_then_opens_session()
    {
        Launcher().FocusChat(new ChatTab("11111111-aaaa-4bbb-8ccc-000000000001", "PROJ-1069", "/Users/me/proj"));
        Assert.Equal(["open", "-b", "com.microsoft.VSCode", "/Users/me/proj"], _runner.Started[0]);
        Assert.Equal(["open", "vscode://anthropic.claude-code/open?session=11111111-aaaa-4bbb-8ccc-000000000001"], _runner.Started[1]);
    }

    [Fact]
    public void Rider_gets_all_paths_in_one_call()
    {
        Launcher().OpenInRider([new RepoRef("a", "/p/a", "b"), new RepoRef("c", "/p/c", "b")]);
        Assert.Equal(["/Applications/Rider.app/Contents/MacOS/rider", "/p/a", "/p/c"], _runner.Started.Single());
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
        Launcher().OpenIde(new IdeSettings(IdeType.Cursor), [new RepoRef("a", "/p/a", "b"), new RepoRef("c", "/p/c", "b")]);
        Assert.Equal(2, _runner.Started.Count);
        Assert.Equal(["/Applications/Cursor.app/Contents/Resources/app/bin/cursor", "-n", "/p/a"], _runner.Started[0]);
        Assert.Equal(["/Applications/Cursor.app/Contents/Resources/app/bin/cursor", "-n", "/p/c"], _runner.Started[1]);
    }

    [Fact]
    public void Run_starts_the_program_with_arguments()
    {
        Launcher().Run("open", ["-a", "Terminal", "/p/a"]);
        Assert.Equal(["open", "-a", "Terminal", "/p/a"], _runner.Started.Single());
    }
}
