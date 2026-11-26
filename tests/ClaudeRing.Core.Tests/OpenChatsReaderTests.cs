namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class OpenChatsReaderTests
{
    private const string State = """
        {"panelTabSessions":[
          {"sessionId":"11111111-aaaa-4bbb-8ccc-000000000001","title":"PROJ-1069","fullEditor":true},
          {"sessionId":"22222222-bbbb-4ccc-8ddd-000000000002","title":"PROJ-1173 эмодзи из п…","fullEditor":true}]}
        """;

    [Fact]
    public void Parses_panel_tabs()
    {
        var tabs = OpenChatsReader.ParsePanelTabs(State, "/Users/me/proj");
        Assert.Equal(2, tabs.Count);
        Assert.Equal("PROJ-1069", tabs[0].Title);
        Assert.Equal("/Users/me/proj", tabs[0].WorkspaceFolder);
    }

    [Fact]
    public void Reads_only_live_windows()
    {
        using var dir = new TempDir();
        dir.Write("Code/User/workspaceStorage/aaa/state.vscdb", "");
        dir.Write("Code/User/workspaceStorage/aaa/workspace.json", """{"folder":"file:///Users/me/live"}""");
        dir.Write("Code/User/workspaceStorage/bbb/state.vscdb", "");
        dir.Write("Code/User/workspaceStorage/bbb/workspace.json", """{"folder":"file:///Users/me/closed"}""");
        dir.Write("ide/1.lock", """{"pid":111,"workspaceFolders":["/Users/me/live"]}""");
        var runner = new FakeProcessRunner
        {
            OnRun = (_, args) => args[^1].Contains("Anthropic.claude-code")
                ? System.Text.Json.JsonSerializer.Serialize(new[] { new { value = State } })
                : "[]"
        };
        var reader = new OpenChatsReader(Path.Combine(dir.Path, "Code/User"), Path.Combine(dir.Path, "ide"), runner, pid => pid == 111);

        var tabs = reader.Read();

        Assert.Equal(2, tabs.Count);
        Assert.All(tabs, t => Assert.Equal("/Users/me/live", t.WorkspaceFolder));
    }

    [Fact]
    public void Same_session_is_listed_once()
    {
        var tabs = OpenChatsReader.ParsePanelTabs(State, null).Concat(OpenChatsReader.ParsePanelTabs(State, null));
        Assert.Equal(2, OpenChatsReader.Dedupe(tabs).Count);
    }

    private static string StateFor(params string[] ids) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { new { value = "{\"panelTabSessions\":[" + string.Join(",", ids.Select(i => $"{{\"sessionId\":\"{i}\",\"title\":\"t{i}\"}}")) + "]}" } });

    private static OpenChatsReader MakeReader(TempDir dir, FakeProcessRunner runner) =>
        new(Path.Combine(dir.Path, "Code/User"), Path.Combine(dir.Path, "ide"), runner, _ => true);

    [Fact]
    public void Workspace_window_is_not_an_empty_window()
    {
        using var dir = new TempDir();
        var emptyDb = dir.Write("Code/User/workspaceStorage/empty/state.vscdb", "");
        var wsDb = dir.Write("Code/User/workspaceStorage/ws/state.vscdb", "");
        var wsFile = dir.Write("proj/x.code-workspace", """{"folders":[{"path":"a"}]}""");
        dir.Write("Code/User/workspaceStorage/ws/workspace.json", "{\"workspace\":\"file://" + wsFile + "\"}");
        File.SetLastWriteTimeUtc(emptyDb, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(wsDb, DateTime.UtcNow);
        dir.Write("ide/1.lock", """{"pid":1,"workspaceFolders":[]}""");
        var runner = new FakeProcessRunner { OnRun = (_, args) => args.Contains(emptyDb) ? StateFor("A") : StateFor("B") };

        var tabs = MakeReader(dir, runner).Read();

        Assert.Equal(["A"], tabs.Select(t => t.SessionId));
    }

    [Fact]
    public void Live_workspace_window_reads_its_tabs()
    {
        using var dir = new TempDir();
        dir.Write("Code/User/workspaceStorage/ws/state.vscdb", "");
        var wsFile = dir.Write("proj/x.code-workspace", "{\n // comment\n \"folders\":[{\"path\":\"a\"},],\n}");
        dir.Write("Code/User/workspaceStorage/ws/workspace.json", "{\"workspace\":\"file://" + wsFile + "\"}");
        dir.Write("ide/1.lock", "{\"pid\":1,\"workspaceFolders\":[\"" + Path.Combine(dir.Path, "proj", "a") + "\"]}");
        var runner = new FakeProcessRunner { OnRun = (_, _) => StateFor("B") };

        var tabs = MakeReader(dir, runner).Read();

        Assert.Single(tabs);
        Assert.Equal(wsFile, tabs[0].WorkspaceFolder);
    }

    [Fact]
    public void Bad_lock_file_is_skipped()
    {
        using var dir = new TempDir();
        dir.Write("Code/User/workspaceStorage/aaa/state.vscdb", "");
        dir.Write("Code/User/workspaceStorage/aaa/workspace.json", """{"folder":"file:///Users/me/live"}""");
        dir.Write("ide/1.lock", "{not json");
        dir.Write("ide/2.lock", """{"pid":2,"workspaceFolders":["/Users/me/live"]}""");
        var runner = new FakeProcessRunner { OnRun = (_, _) => StateFor("A") };

        Assert.Single(MakeReader(dir, runner).Read());
    }

    [Fact]
    public void Failing_db_is_skipped()
    {
        using var dir = new TempDir();
        var badDb = dir.Write("Code/User/workspaceStorage/aaa/state.vscdb", "");
        dir.Write("Code/User/workspaceStorage/aaa/workspace.json", """{"folder":"file:///Users/me/one"}""");
        dir.Write("Code/User/workspaceStorage/bbb/state.vscdb", "");
        dir.Write("Code/User/workspaceStorage/bbb/workspace.json", """{"folder":"file:///Users/me/two"}""");
        dir.Write("ide/1.lock", """{"pid":1,"workspaceFolders":["/Users/me/one","/Users/me/two"]}""");
        var runner = new FakeProcessRunner
        {
            OnRun = (_, args) => args.Contains(badDb) ? throw new InvalidOperationException("boom") : StateFor("B")
        };

        var tabs = MakeReader(dir, runner).Read();

        Assert.Equal(["B"], tabs.Select(t => t.SessionId));
    }

    [Fact]
    public void Tab_without_session_id_is_skipped()
    {
        var tabs = OpenChatsReader.ParsePanelTabs("""{"panelTabSessions":[{"title":"x"},{"sessionId":"s1","title":"ok"}]}""", null);
        Assert.Single(tabs);
    }
}
