namespace ClaudeRing.Core;

public sealed class Launcher(IProcessRunner runner, Action<int> sleep)
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

    public void OpenUrl(string url) => runner.Start("open", url);

    public void OpenUrls(IEnumerable<string> urls)
    {
        var list = urls.ToArray();
        if (list.Length > 0) runner.Start("open", list);
    }

    public void Reveal(IEnumerable<string> paths) => runner.Start("open", paths.ToArray());

    public void OpenIde(IdeSettings ide, IReadOnlyList<RepoRef> repos)
    {
        // "open -a" fails silently for a missing app, so check the bundle first.
        if (repos.Count > 0 && IdeLauncher.AppPath(ide) is { } app && !Directory.Exists(app))
            throw new FileNotFoundException($"IDE app not found: {app}", app);
        foreach (var command in IdeLauncher.Commands(ide, repos.Select(r => r.Path).ToList())) Run(command.File, command.Args);
    }

    // Start errors go to the caller; their message names the program path.
    public void Run(string file, IReadOnlyList<string> args) => runner.Start(file, [.. args]);
}
