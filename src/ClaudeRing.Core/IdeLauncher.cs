namespace ClaudeRing.Core;

// How each IDE opens folders on macOS.
public static class IdeLauncher
{
    private enum Kind { JetBrains, VsCodeFamily }

    private sealed record Ide(string Name, string App, string Binary, Kind Kind);

    private static readonly Dictionary<IdeType, Ide> Table = new()
    {
        [IdeType.Rider] = new("Rider", "/Applications/Rider.app", "rider", Kind.JetBrains),
        [IdeType.Idea] = new("IDEA", "/Applications/IntelliJ IDEA.app", "idea", Kind.JetBrains),
        [IdeType.WebStorm] = new("WebStorm", "/Applications/WebStorm.app", "webstorm", Kind.JetBrains),
        [IdeType.PyCharm] = new("PyCharm", "/Applications/PyCharm.app", "pycharm", Kind.JetBrains),
        [IdeType.GoLand] = new("GoLand", "/Applications/GoLand.app", "goland", Kind.JetBrains),
        [IdeType.CLion] = new("CLion", "/Applications/CLion.app", "clion", Kind.JetBrains),
        [IdeType.RubyMine] = new("RubyMine", "/Applications/RubyMine.app", "rubymine", Kind.JetBrains),
        [IdeType.PhpStorm] = new("PhpStorm", "/Applications/PhpStorm.app", "phpstorm", Kind.JetBrains),
        [IdeType.VsCode] = new("VS Code", "/Applications/Visual Studio Code.app", "code", Kind.VsCodeFamily),
        [IdeType.Cursor] = new("Cursor", "/Applications/Cursor.app", "cursor", Kind.VsCodeFamily),
        [IdeType.Windsurf] = new("Windsurf", "/Applications/Windsurf.app", "windsurf", Kind.VsCodeFamily),
    };

    // Slot label of the IDE item.
    public static string DisplayName(IdeSettings ide)
    {
        if (ide.Type == IdeType.Custom)
        {
            var name = Path.GetFileNameWithoutExtension((ide.App ?? "").TrimEnd('/'));
            return name.Length > 0 ? name : "IDE";
        }
        return Table.TryGetValue(ide.Type, out var known) ? known.Name : "";
    }

    // App bundle folder of the IDE: the configured app or the default one. Null for IdeType.None.
    public static string? AppPath(IdeSettings ide)
    {
        if (ide.Type == IdeType.Custom) return ide.App?.TrimEnd('/');
        return Table.TryGetValue(ide.Type, out var known) ? (ide.App ?? known.App).TrimEnd('/') : null;
    }

    // Processes to start. Empty for IdeType.None or when there are no paths.
    public static IReadOnlyList<MenuAction.Run> Commands(IdeSettings ide, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return [];
        if (ide.Type == IdeType.Custom)
        {
            if (ide.App is not { } customApp) return [];
            return paths.Select(p => new MenuAction.Run("open", ["-a", customApp, p])).ToList();
        }
        if (!Table.TryGetValue(ide.Type, out var known)) return [];

        var app = (ide.App ?? known.App).TrimEnd('/');
        if (known.Kind == Kind.JetBrains) return [new MenuAction.Run($"{app}/Contents/MacOS/{known.Binary}", paths.ToList())];
        // "-n" opens a new window, so each repo gets its own window.
        return paths.Select(p => new MenuAction.Run($"{app}/Contents/Resources/app/bin/{known.Binary}", ["-n", p])).ToList();
    }
}
