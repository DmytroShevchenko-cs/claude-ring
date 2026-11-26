namespace ClaudeRing.Core;

public sealed class Launcher(IProcessRunner runner, RingConfig config, Action<int> sleep)
{
    private const string VsCodeBundle = "com.microsoft.VSCode";

    // The session URI goes to the focused VS Code window, so raise the right window first.
    public void FocusChat(ChatTab tab)
    {
        if (tab.WorkspaceFolder != null) runner.Start("open", "-b", VsCodeBundle, tab.WorkspaceFolder);
        else runner.Start("open", "-b", VsCodeBundle);
        sleep(500);
        runner.Start("open", $"vscode://anthropic.claude-code/open?session={tab.SessionId}");
    }

    public void OpenInRider(IEnumerable<RepoRef> repos) =>
        runner.Start(config.RiderPath, repos.Select(r => r.Path).ToArray());

    public void OpenUrl(string url) => runner.Start("open", url);

    public void OpenUrls(IEnumerable<string> urls)
    {
        var list = urls.ToArray();
        if (list.Length > 0) runner.Start("open", list);
    }

    public void Reveal(IEnumerable<string> paths) => runner.Start("open", paths.ToArray());
}
