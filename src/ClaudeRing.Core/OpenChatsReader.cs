namespace ClaudeRing.Core;

using System.Text.Json;

public sealed class OpenChatsReader(string codeUserDir, string claudeIdeDir, IProcessRunner runner, Func<int, bool> isPidAlive)
{
    private const string StateKey = "Anthropic.claude-code";

    private abstract record Window;
    private sealed record FolderWindow(string Path) : Window;
    private sealed record WorkspaceWindow(string FilePath, IReadOnlyList<string> Members) : Window;
    private sealed record EmptyWindow : Window;

    public IReadOnlyList<ChatTab> Read()
    {
        var live = LiveWindows();
        var result = new List<ChatTab>();
        var storage = Path.Combine(codeUserDir, "workspaceStorage");
        if (!Directory.Exists(storage)) return result;

        var empties = new List<string>();
        foreach (var dir in Directory.GetDirectories(storage).Where(d => File.Exists(Path.Combine(d, "state.vscdb"))))
        {
            switch (ReadWindow(dir))
            {
                case FolderWindow f when live.Folders.Contains(f.Path):
                    result.AddRange(ReadTabs(dir, f.Path));
                    break;
                // A workspace window is open if any of its folders is open.
                case WorkspaceWindow w when w.Members.Any(live.Folders.Contains):
                    result.AddRange(ReadTabs(dir, w.FilePath));
                    break;
                case EmptyWindow:
                    empties.Add(dir);
                    break;
            }
        }

        if (live.HasEmptyWindow)
        {
            var newestEmpty = empties
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "state.vscdb")))
                .FirstOrDefault();
            if (newestEmpty != null) result.AddRange(ReadTabs(newestEmpty, null));
        }
        return Dedupe(result);
    }

    public static IReadOnlyList<ChatTab> ParsePanelTabs(string stateJson, string? folder)
    {
        using var doc = JsonDocument.Parse(stateJson);
        if (!doc.RootElement.TryGetProperty("panelTabSessions", out var tabs)) return [];
        var result = new List<ChatTab>();
        foreach (var t in tabs.EnumerateArray())
        {
            if (!t.TryGetProperty("sessionId", out var id) || id.ValueKind != JsonValueKind.String) continue;
            var title = t.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.String ? tt.GetString() : null;
            result.Add(new ChatTab(id.GetString()!, title ?? "", folder));
        }
        return result;
    }

    public static IReadOnlyList<ChatTab> Dedupe(IEnumerable<ChatTab> tabs) =>
        tabs.GroupBy(t => t.SessionId).Select(g => g.First()).ToList();

    private IEnumerable<ChatTab> ReadTabs(string dir, string? folder)
    {
        try
        {
            var db = Path.Combine(dir, "state.vscdb");
            var json = runner.Run("/usr/bin/sqlite3", "-readonly", "-json", db,
                $"select value from ItemTable where key='{StateKey}'");
            if (string.IsNullOrWhiteSpace(json)) return [];
            using var rows = JsonDocument.Parse(json);
            var value = rows.RootElement.EnumerateArray().Select(r => r.GetProperty("value").GetString()).FirstOrDefault();
            return value == null ? [] : ParsePanelTabs(value, folder);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or TimeoutException
                                      or KeyNotFoundException or IOException)
        {
            return [];
        }
    }

    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // Returns null when the storage dir cannot be read, so the caller skips it.
    private static Window? ReadWindow(string dir)
    {
        var file = Path.Combine(dir, "workspace.json");
        if (!File.Exists(file)) return new EmptyWindow();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("folder", out var folder))
                return FileUriPath(folder) is { } p ? new FolderWindow(p.TrimEnd('/')) : null;
            if (doc.RootElement.TryGetProperty("workspace", out var ws) && FileUriPath(ws) is { } wsPath)
                return ReadWorkspaceFile(wsPath);
            return null;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FileUriPath(JsonElement uri) =>
        uri.ValueKind == JsonValueKind.String && Uri.TryCreate(uri.GetString(), UriKind.Absolute, out var u) && u.IsFile ? u.LocalPath : null;

    private static WorkspaceWindow? ReadWorkspaceFile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), Jsonc);
            var baseDir = Path.GetDirectoryName(path) ?? "";
            var members = new List<string>();
            if (doc.RootElement.TryGetProperty("folders", out var folders))
                foreach (var f in folders.EnumerateArray())
                {
                    if (!f.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String) continue;
                    members.Add(Path.GetFullPath(p.GetString()!, baseDir).TrimEnd('/'));
                }
            return new WorkspaceWindow(path, members);
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The lock pid is the VS Code main process, so "alive" means VS Code is running,
    // not that this exact window is still open.
    private (HashSet<string> Folders, bool HasEmptyWindow) LiveWindows()
    {
        var folders = new HashSet<string>();
        var hasEmpty = false;
        if (!Directory.Exists(claudeIdeDir)) return (folders, hasEmpty);
        foreach (var file in Directory.GetFiles(claudeIdeDir, "*.lock"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("pid", out var pid) || pid.ValueKind != JsonValueKind.Number
                    || !pid.TryGetInt32(out var pidValue) || !isPidAlive(pidValue)) continue;
                var list = new List<string>();
                if (doc.RootElement.TryGetProperty("workspaceFolders", out var wf) && wf.ValueKind == JsonValueKind.Array)
                    list.AddRange(wf.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.TrimEnd('/')));
                if (list.Count == 0) hasEmpty = true;
                folders.UnionWith(list);
            }
            catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
            {
            }
        }
        return (folders, hasEmpty);
    }

    public static bool IsPidAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
