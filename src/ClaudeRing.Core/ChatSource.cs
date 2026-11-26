namespace ClaudeRing.Core;

public interface IChatSource
{
    IReadOnlyList<ChatContext> Load();
}

public sealed class ChatSource(OpenChatsReader tabs, SessionInfoReader sessions, RepoFinder repos) : IChatSource
{
    public IReadOnlyList<ChatContext> Load() =>
        tabs.Read().Select(tab =>
        {
            var info = sessions.Read(tab.SessionId);
            var key = TaskKey.Find(info.CustomTitle, tab.Title, info.GitBranch, info.AiTitle);
            return new ChatContext(tab, info, key, key == null ? [] : repos.Find(key));
        }).ToList();

    public static ChatSource CreateDefault(Settings settings, IProcessRunner runner)
    {
        var home = Settings.Home;
        return new ChatSource(
            new OpenChatsReader(Path.Combine(home, "Library", "Application Support", "Code", "User"),
                Path.Combine(home, ".claude", "ide"), runner, OpenChatsReader.IsPidAlive),
            new SessionInfoReader(Path.Combine(home, ".claude", "projects")),
            new RepoFinder(settings.RepoRoots));
    }
}
