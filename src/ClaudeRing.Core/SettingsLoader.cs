namespace ClaudeRing.Core;

using System.Text.Json;
using System.Text.RegularExpressions;

// Settings is null when the config has errors; then the caller keeps what it had.
// Warnings are about skipped menu items and old fields; they never block the config.
public sealed record SettingsResult(Settings? Settings, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public static class SettingsLoader
{
    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly string[] LegacyFields = ["gitLabBaseUrl", "gitLabGroup", "jiraBaseUrl", "mrTargets", "riderPath"];

    private static readonly Dictionary<string, UiLanguage> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auto"] = UiLanguage.Auto,
        ["en"] = UiLanguage.En,
        ["ru"] = UiLanguage.Ru,
    };

    private static readonly Dictionary<string, GitHostType> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gitlab"] = GitHostType.GitLab,
        ["github"] = GitHostType.GitHub,
    };

    private static readonly Dictionary<string, TrackerType> TrackerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = TrackerType.None,
        ["jira"] = TrackerType.Jira,
        ["linear"] = TrackerType.Linear,
        ["youtrack"] = TrackerType.YouTrack,
        ["github"] = TrackerType.GitHub,
    };

    private static readonly Dictionary<string, IdeType> IdeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = IdeType.None,
        ["rider"] = IdeType.Rider,
        ["idea"] = IdeType.Idea,
        ["webstorm"] = IdeType.WebStorm,
        ["pycharm"] = IdeType.PyCharm,
        ["goland"] = IdeType.GoLand,
        ["clion"] = IdeType.CLion,
        ["rubymine"] = IdeType.RubyMine,
        ["phpstorm"] = IdeType.PhpStorm,
        ["vscode"] = IdeType.VsCode,
        ["cursor"] = IdeType.Cursor,
        ["windsurf"] = IdeType.Windsurf,
        ["custom"] = IdeType.Custom,
    };

    // A missing file gives the defaults. Never throws.
    public static SettingsResult Load(string path)
    {
        string json;
        try
        {
            if (!File.Exists(path)) return new SettingsResult(new Settings(), [], []);
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SettingsResult(null, [$"config: cannot read {path}: {e.Message}"], []);
        }
        return Parse(json);
    }

    public static SettingsResult Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Jsonc);
        }
        catch (JsonException e)
        {
            return new SettingsResult(null, [$"config: invalid JSON: {e.Message}"], []);
        }

        using (doc)
        {
            var reader = new Reader();
            var settings = reader.Read(doc.RootElement);
            return new SettingsResult(reader.Errors.Count == 0 ? settings : null, reader.Errors, reader.Warnings);
        }
    }

    private sealed class Reader
    {
        public List<string> Errors { get; } = [];

        public List<string> Warnings { get; } = [];

        public Settings Read(JsonElement root)
        {
            var defaults = new Settings();
            if (root.ValueKind != JsonValueKind.Object)
            {
                Errors.Add("config: expected a JSON object");
                return defaults;
            }

            var legacy = LegacyFields.Where(name => Get(root, name) != null).ToList();
            foreach (var name in legacy) Warnings.Add($"{name}: old field, it still works but see the README for the new schema");

            return new Settings
            {
                Language = Choice(root, "language", "language", Languages) ?? defaults.Language,
                RepoRoots = RepoRoots(root) ?? defaults.RepoRoots,
                TaskKeyPattern = Pattern(root) ?? defaults.TaskKeyPattern,
                Git = Git(root),
                Tracker = Tracker(root),
                Ide = Ide(root, legacy.Count > 0),
                ChatMenu = ChatMenu(root) ?? defaults.ChatMenu,
                SlotCount = SlotCount(root) ?? defaults.SlotCount,
                ReopenDelayMs = Int(root, "reopenDelayMs", "reopenDelayMs") ?? defaults.ReopenDelayMs,
                ResetOnRingEvent = Text(root, "resetOnRingEvent", "resetOnRingEvent") ?? defaults.ResetOnRingEvent,
            };
        }

        private List<string>? RepoRoots(JsonElement root)
        {
            var list = TextList(root, "repoRoots", "repoRoots");
            if (list == null) return null;
            var result = new List<string>();
            for (var i = 0; i < list.Count; i++)
            {
                var path = ExpandHome(list[i]);
                if (Path.IsPathRooted(path)) result.Add(path);
                else Errors.Add($"repoRoots[{i}]: must be an absolute path or start with ~/");
            }
            return result;
        }

        private string? Pattern(JsonElement root)
        {
            var pattern = Text(root, "taskKeyPattern", "taskKeyPattern");
            if (pattern == null) return null;
            try
            {
                _ = new Regex(pattern);
                return pattern;
            }
            catch (ArgumentException e)
            {
                Errors.Add($"taskKeyPattern: invalid regular expression: {e.Message}");
                return null;
            }
        }

        // Each git field falls back to its old field, then to the default of the host.
        private GitSettings Git(JsonElement root)
        {
            var git = Section(root, "git");
            var host = Choice(git, "host", "git.host", Hosts) ?? GitHostType.GitLab;
            var gitLab = host == GitHostType.GitLab;
            var baseUrl = Url(git, "baseUrl", "git.baseUrl")
                ?? (gitLab ? Url(root, "gitLabBaseUrl", "gitLabBaseUrl") : null)
                ?? (gitLab ? "https://gitlab.com" : "https://github.com");
            var group = Text(git, "group", "git.group")
                ?? (gitLab ? Text(root, "gitLabGroup", "gitLabGroup") : null);
            List<ReviewTarget> targets = Targets(git) ?? LegacyTargets(root) ?? [ReviewTarget.Branch("main")];
            return new GitSettings(host, baseUrl, string.IsNullOrEmpty(group) ? null : group, targets);
        }

        private List<ReviewTarget>? Targets(JsonElement? git)
        {
            if (Get(git, "targets") is not { } value) return null;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Errors.Add("git.targets: expected a list");
                return null;
            }
            var result = new List<ReviewTarget>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var path = $"git.targets[{i++}]";
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                {
                    result.Add(ReviewTarget.Branch(name));
                    continue;
                }
                if (item.ValueKind != JsonValueKind.Object)
                {
                    Errors.Add($"{path}: expected a branch name or an object with label and branches");
                    continue;
                }
                var label = Get(item, "label") is { ValueKind: JsonValueKind.String } l ? l.GetString() : null;
                if (string.IsNullOrEmpty(label)) Errors.Add($"{path}.label: expected a non-empty string");
                var errorsBefore = Errors.Count;
                var branches = TextList(item, "branches", $"{path}.branches");
                if (Errors.Count == errorsBefore && (branches == null || branches.Count == 0))
                    Errors.Add($"{path}.branches: expected a non-empty list of branch names");
                if (!string.IsNullOrEmpty(label) && branches is { Count: > 0 }) result.Add(new ReviewTarget(label, branches));
            }
            return result;
        }

        private List<ReviewTarget>? LegacyTargets(JsonElement root)
        {
            if (Get(root, "mrTargets") is not { } value) return null;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Errors.Add("mrTargets: expected a list");
                return null;
            }
            var result = new List<ReviewTarget>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (Get(item, "branch") is { ValueKind: JsonValueKind.String } b && b.GetString() is { Length: > 0 } branch) result.Add(ReviewTarget.Branch(branch));
                else Errors.Add($"mrTargets[{i}].branch: expected a non-empty string");
                i++;
            }
            return result;
        }

        private TrackerSettings Tracker(JsonElement root)
        {
            if (Section(root, "tracker") is { } tracker)
            {
                return new TrackerSettings(
                    Choice(tracker, "type", "tracker.type", TrackerTypes) ?? TrackerType.None,
                    Url(tracker, "baseUrl", "tracker.baseUrl"),
                    Text(tracker, "workspace", "tracker.workspace"));
            }
            return Url(root, "jiraBaseUrl", "jiraBaseUrl") is { } jira
                ? new TrackerSettings(TrackerType.Jira, jira)
                : new TrackerSettings(TrackerType.None);
        }

        private IdeSettings Ide(JsonElement root, bool legacy)
        {
            if (Section(root, "ide") is { } ide)
            {
                var type = Choice(ide, "type", "ide.type", IdeTypes) ?? IdeType.None;
                var app = Text(ide, "app", "ide.app") is { Length: > 0 } a ? ExpandHome(a) : null;
                if (type == IdeType.Custom && app == null) Errors.Add("ide.app: required when ide.type is custom");
                return new IdeSettings(type, app);
            }
            if (!legacy) return new IdeSettings(IdeType.None);
            // Old configs always had Rider.
            return new IdeSettings(IdeType.Rider, Text(root, "riderPath", "riderPath") is { } path ? RiderApp(path) : null);
        }

        // "/Applications/Rider.app/Contents/MacOS/rider" -> "/Applications/Rider.app".
        private string? RiderApp(string riderPath)
        {
            var at = riderPath.IndexOf(".app/", StringComparison.Ordinal);
            if (at >= 0) return riderPath[..(at + 4)];
            if (riderPath.EndsWith(".app", StringComparison.Ordinal)) return riderPath;
            Warnings.Add("riderPath: no .app bundle in the path, the default Rider app is used");
            return null;
        }

        private List<MenuEntry>? ChatMenu(JsonElement root)
        {
            if (Get(root, "chatMenu") is not { } value) return null;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Errors.Add("chatMenu: expected a list");
                return null;
            }
            var result = new List<MenuEntry>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var path = $"chatMenu[{i++}]";
                MenuEntry? entry = item.ValueKind switch
                {
                    JsonValueKind.String => BuiltIn(item.GetString()!, path),
                    JsonValueKind.Object => Custom(item, path),
                    _ => Skip(path, "expected an item id or an object"),
                };
                if (entry != null) result.Add(entry);
            }
            return result;
        }

        private MenuEntry? BuiltIn(string id, string path) =>
            MenuEntry.BuiltInIds.Contains(id)
                ? new MenuEntry.BuiltIn(id)
                : Skip(path, $"unknown item \"{id}\", expected one of {string.Join(", ", MenuEntry.BuiltInIds)} or an object");

        // Problems here only skip this item, so they go to warnings.
        private MenuEntry.Custom? Custom(JsonElement item, string path)
        {
            var label = Get(item, "label") is { ValueKind: JsonValueKind.String } l ? l.GetString() : null;
            if (string.IsNullOrWhiteSpace(label)) return Skip(path, "needs a non-empty \"label\"");

            var url = Get(item, "url") is { ValueKind: JsonValueKind.String } u ? u.GetString() : null;
            List<string>? run = null;
            if (Get(item, "run") is { } runValue)
            {
                if (runValue.ValueKind != JsonValueKind.Array || runValue.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                    return Skip(path, "\"run\" must be a list of strings");
                run = runValue.EnumerateArray().Select(x => x.GetString()!).ToList();
            }
            if ((url == null) == (run == null)) return Skip(path, "needs exactly one of \"url\" and \"run\"");
            if (run != null && (run.Count == 0 || run[0].Length == 0)) return Skip(path, "\"run\" needs a program first");
            if (run != null && Template.Placeholders(run[0]).Count > 0)
                return Skip(path, "placeholders are allowed only in arguments, not in the program");

            var perRepo = Get(item, "perRepo") is { ValueKind: JsonValueKind.True };
            List<string> templates = url != null ? [url] : run!.Skip(1).ToList();
            var unknown = templates.SelectMany(Template.Unknown).Distinct().ToList();
            if (unknown.Count > 0)
                return Skip(path, $"unknown placeholder {string.Join(", ", unknown.Select(p => "{" + p + "}"))}");
            if (!perRepo && templates.SelectMany(Template.Placeholders).Any(p => p.StartsWith("repo.", StringComparison.Ordinal)))
                return Skip(path, "{repo.*} placeholders need \"perRepo\": true");

            return new MenuEntry.Custom(label!, url, run, perRepo);
        }

        private MenuEntry.Custom? Skip(string path, string why)
        {
            Warnings.Add($"{path}: {why}, item skipped");
            return null;
        }

        private int? SlotCount(JsonElement root)
        {
            var n = Int(root, "slotCount", "slotCount");
            if (n is null or (>= 3 and <= 9)) return n;
            Errors.Add("slotCount: must be from 3 to 9");
            return null;
        }

        // Null when the field is missing or null. Names ignore case.
        private static JsonElement? Get(JsonElement? obj, string name)
        {
            if (obj is not { ValueKind: JsonValueKind.Object } o) return null;
            foreach (var p in o.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value;
            }
            return null;
        }

        private JsonElement? Section(JsonElement root, string name)
        {
            if (Get(root, name) is not { } value) return null;
            if (value.ValueKind == JsonValueKind.Object) return value;
            Errors.Add($"{name}: expected an object");
            return null;
        }

        private string? Text(JsonElement? obj, string name, string path)
        {
            if (Get(obj, name) is not { } value) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            Errors.Add($"{path}: expected a string");
            return null;
        }

        private List<string>? TextList(JsonElement? obj, string name, string path)
        {
            if (Get(obj, name) is not { } value) return null;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Errors.Add($"{path}: expected a list of strings");
                return null;
            }
            var result = new List<string>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s) result.Add(s);
                else Errors.Add($"{path}[{i}]: expected a non-empty string");
                i++;
            }
            return result;
        }

        private int? Int(JsonElement? obj, string name, string path)
        {
            if (Get(obj, name) is not { } value) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
            Errors.Add($"{path}: expected a whole number");
            return null;
        }

        private T? Choice<T>(JsonElement? obj, string name, string path, Dictionary<string, T> values) where T : struct
        {
            var text = Text(obj, name, path);
            if (text == null) return null;
            if (values.TryGetValue(text, out var value)) return value;
            Errors.Add($"{path}: unknown value \"{text}\", expected one of {string.Join(", ", values.Keys)}");
            return null;
        }

        private string? Url(JsonElement? obj, string name, string path)
        {
            var text = Text(obj, name, path);
            if (text == null) return null;
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return text.TrimEnd('/');
            Errors.Add($"{path}: expected an absolute http(s) URL");
            return null;
        }

        private static string ExpandHome(string path) =>
            path == "~" ? Settings.Home
            : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Settings.Home, path[2..])
            : path;
    }
}
