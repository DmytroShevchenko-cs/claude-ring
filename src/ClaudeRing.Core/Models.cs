namespace ClaudeRing.Core;

public sealed record ChatTab(string SessionId, string Title, string? WorkspaceFolder);

public sealed record SessionInfo(string? CustomTitle, string? AiTitle, string? GitBranch, string? Cwd);

public sealed record RepoRef(string Name, string Path, string Branch, string? WebUrl = null);

public sealed record ChatContext(ChatTab Tab, SessionInfo Session, string? TaskKey, IReadOnlyList<RepoRef> Repos)
{
    // Short label for a ring bubble: task key first, otherwise the best title.
    public string Label => TaskKey ?? Session.CustomTitle ?? Tab.Title;
}
