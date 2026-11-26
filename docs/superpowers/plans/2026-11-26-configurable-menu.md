# Configurable Ring Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the ClaudeRing chat menu configurable (git host, issue tracker, IDE, custom items, item order, en/ru labels) with config hot reload, while the ring behaves exactly as today for the owner's config.

**Architecture:** All logic lives in `ClaudeRing.Core` and is unit tested: `SettingsLoader` turns the JSONC file into an immutable `Settings` (or a list of errors with field paths), presets (`IGitHost`, `ITracker`, `IdeLauncher`, `Strings`) turn settings into links, labels and process commands, `MenuBuilder` builds the chat page and its submenus as `MenuPage` values, and `RingMenu` becomes a page stack that only knows slots, paging, back and the click lock. The Logi plugin loads settings, executes actions and rebuilds the menu when `ConfigWatcher` sees the file change.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit 2.9.3, System.Text.Json (`JsonDocument` with comments and trailing commas), Logi Actions SDK (`LogiPluginApi` 6.x), macOS `open`, `/usr/bin/defaults`.

**Spec:** `docs/superpowers/specs/2026-11-26-configurable-menu-design.md`

## Global Constraints

- Platform: macOS only.
- Target framework `net10.0` for Core, tests and plugin. No new NuGet packages.
- No exception may escape a plugin callback (`Load`, `Unload`, `GetCommandDisplayName`, `GetCommandImage`, `RunCommand`, timer and file watcher callbacks).
- Core never throws for bad config input: `SettingsLoader` returns errors and warnings. `MenuBuilder` never throws for a `Settings` value that `SettingsLoader` returned.
- Config file: `~/.config/claude-ring/config.json`; JSON with comments and trailing commas allowed.
- Defaults (verbatim from the spec): `language` auto; `repoRoots` `[~/Projects]`; `git` gitlab / `https://gitlab.com` / no group / targets `["main"]`; `tracker` none; `ide` none (the `ide` item is hidden); `chatMenu` `["chat", "ide", "reviews", "issue", "repos"]`; `slotCount` 8; `reopenDelayMs` 150. Extra: `resetOnRingEvent` `""` (kept from today's config).
- Invalid JSON or invalid values: the new config is not applied, the previous one stays, the log lists each error with its field path. On first load with an invalid config, defaults are used.
- Unknown built-in id or invalid custom item in `chatMenu`: that item is skipped with a warning; the rest works.
- Missing placeholder value: item hidden for that chat/repo, no log.
- `run` program or IDE missing / cannot start: error logged with the program path; the ring keeps working.
- Unchanged behaviours: exactly `slotCount` slots; back in the last slot on non-root pages; root paging with "more" in the last slot; atomic clicks under a lock; a final action returns to root without reloading (the plugin reloads after running the action); reload outside the menu lock; big slot images (`SlotText` + `GetCommandImage`); ring reopen via `RingReopener`; `RingWatcher`; repo display names without the `-<KEY>` suffix; transparent empty slots.
- Labels come from `Strings` (en + ru). Code, identifiers and comments in English (simple B1 level). Plugin log messages stay in Russian like the existing ones.
- Plugin code style follows the existing plugin files: `String`/`Int32`/`Boolean`/`Object`, `this.` on instance members.
- Never put the owner's real values (company names, hosts, local paths) in tracked files. Examples use `acme`, `example.com`, `example.atlassian.net`.
- Commits: English, one line, no trailers. Always `git add` explicit paths. Never commit `.superpowers/`.
- Do NOT run `dotnet build` on the solution or on `ClaudeRingPlugin.csproj` during Tasks 1-10: its post-build step reloads the live plugin in LogiPluginService. To check that the plugin compiles, use the compile-only command below.

Commands used by every task (run from the repo root):

- All Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → expected last line contains `Failed:     0`.
- One test class: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.<ClassName>"`.
- Plugin compile check (no reload): `dotnet msbuild -restore -t:Compile ClaudeRingPlugin/src/ClaudeRingPlugin.csproj -v:q -nologo -clp:ErrorsOnly` → expected: no `error` lines, exit code 0.

---

## File Structure

```
src/ClaudeRing.Core/
  Template.cs          NEW  T1  placeholders: list, unknown, render (url escaping / plain)
  Settings.cs          NEW  T2  Settings record, enums, GitSettings/TrackerSettings/IdeSettings, MenuEntry
  SettingsLoader.cs    NEW  T2  JSONC → Settings | errors (field paths) + warnings; legacy mapping
  GitHosts.cs          NEW  T3  IGitHost, GitLabHost, GitHubHost, GitHosts.Create
  Trackers.cs          NEW  T3  ITracker, Jira/Linear/YouTrack/GitHubIssues/No trackers, Trackers.Create
  IdeLauncher.cs       NEW  T4  IDE table: display name + process commands per type
  Strings.cs           NEW  T5  en/ru slot labels, language choice
  SystemLanguage.cs    NEW  T5  first macOS UI language via `defaults read -g AppleLanguages`
  MenuPage.cs          NEW  T6  MenuItem, MenuPage (page model for RingMenu)
  MenuBuilder.cs       NEW  T7  chat page + submenus from Settings
  RingMenu.cs          MOD  T4 (MenuAction), T6 (page stack), T7 (helpers moved), T8 (uses MenuBuilder)
  Launcher.cs          MOD  T4 (OpenIde, Run), T8 (no RingConfig, no OpenInRider)
  ChatSource.cs        MOD  T8 (Settings), T9 (TaskKey instance)
  TaskKey.cs           MOD  T9  configurable pattern, capture group rule
  Models.cs            MOD  T8  RingConfig and MrTarget removed
  Links.cs             DEL  T8  replaced by GitHosts/Trackers
tests/ClaudeRing.Core.Tests/
  TemplateTests.cs, SettingsLoaderTests.cs, GitHostTests.cs, TrackerTests.cs, IdeLauncherTests.cs,
  StringsTests.cs, MenuBuilderTests.cs, ExampleConfigTests.cs           NEW
  LauncherTests.cs, RingMenuTests.cs, TaskKeyTests.cs                   MOD
  LinksTests.cs, ConfigTests.cs                                         DEL  T8
ClaudeRingPlugin/src/
  ClaudeRingPlugin.cs        MOD  T8 (Settings, MenuBuilder, new actions), T10 (hot reload)
  Actions/SlotCommand.cs     MOD  T8 (slot count and reopen delay from the new types)
  Helpers/ConfigWatcher.cs   NEW  T10 FileSystemWatcher + 300 ms debounce
README.md, config.example.json   MOD  T11
```

Task order and dependencies: T1 → T2 (validates templates) → T3 → T4 → T5 → T6 → T7 (uses T1-T6) → T8 (switch) → T9 → T10 → T11.

---

### Task 1: Template placeholders

**Files:**
- Create: `src/ClaudeRing.Core/Template.cs`
- Test: `tests/ClaudeRing.Core.Tests/TemplateTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public static partial class Template`
  - `public static readonly IReadOnlySet<string> Known` — `key`, `branch`, `title`, `sessionId`, `repos.parent`, `repo.name`, `repo.path`, `repo.webUrl`, `repo.branch`.
  - `public static IReadOnlyList<string> Placeholders(string template)` — names in order of first use, no duplicates.
  - `public static IReadOnlyList<string> Unknown(string template)` — names not in `Known`.
  - `public static string? Render(string template, IReadOnlyDictionary<string, string?> values, bool forUrl)` — null when any placeholder has no value (missing key, null or empty). With `forUrl`: `repo.webUrl` as is; `branch`, `repo.branch`, `repo.path`, `repos.parent` escaped per path segment (`/` kept); all others `Uri.EscapeDataString`. Without `forUrl`: values as is.
  - `public static string EscapePath(string value)` — `Uri.EscapeDataString` per `/`-segment.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudeRing.Core.Tests/TemplateTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TemplateTests
{
    private static readonly Dictionary<string, string?> Values = new()
    {
        ["key"] = "A-1",
        ["branch"] = "feature/A-1 fix",
        ["title"] = "Fix & ship",
        ["repo.webUrl"] = "https://g/T/api",
        ["repo.path"] = "/p/A-1/api",
        ["sessionId"] = null,
    };

    [Fact]
    public void Lists_placeholders_once_in_order() =>
        Assert.Equal(["key", "branch"], Template.Placeholders("{key}/{branch}/{key}").ToArray());

    [Fact]
    public void Finds_unknown_placeholders() =>
        Assert.Equal(["ticket"], Template.Unknown("{key}-{ticket}").ToArray());

    [Fact]
    public void Json_braces_are_not_placeholders() =>
        Assert.Empty(Template.Placeholders("""{"a": 1}"""));

    [Fact]
    public void Text_without_placeholders_stays() =>
        Assert.Equal("https://x/?a=1", Template.Render("https://x/?a=1", Values, forUrl: true));

    [Fact]
    public void Url_escapes_values_and_keeps_slashes_in_branch() =>
        Assert.Equal("https://x/?q=Fix%20%26%20ship&b=feature/A-1%20fix",
            Template.Render("https://x/?q={title}&b={branch}", Values, forUrl: true));

    [Fact]
    public void Url_keeps_web_url_as_is() =>
        Assert.Equal("https://g/T/api/pipelines", Template.Render("{repo.webUrl}/pipelines", Values, forUrl: true));

    [Fact]
    public void Plain_does_not_escape() =>
        Assert.Equal("Fix & ship /p/A-1/api", Template.Render("{title} {repo.path}", Values, forUrl: false));

    [Fact]
    public void Missing_or_null_value_gives_null()
    {
        Assert.Null(Template.Render("{repo.name}", Values, forUrl: true));
        Assert.Null(Template.Render("{sessionId}", Values, forUrl: false));
    }

    [Fact]
    public void Empty_value_gives_null() =>
        Assert.Null(Template.Render("{key}", new Dictionary<string, string?> { ["key"] = "" }, forUrl: false));

    [Fact]
    public void Escape_path_keeps_slashes() =>
        Assert.Equal("feature/A-1%23x", Template.EscapePath("feature/A-1#x"));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.TemplateTests"`
Expected: build FAIL with `error CS0103: The name 'Template' does not exist in the current context`.

- [ ] **Step 3: Implement `Template`**

Create `src/ClaudeRing.Core/Template.cs`:

```csharp
namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

// Templates for custom menu items, e.g. "https://sentry.io/issues/?query={key}".
public static partial class Template
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "key", "branch", "title", "sessionId", "repos.parent", "repo.name", "repo.path", "repo.webUrl", "repo.branch",
    };

    // Values that are paths: "/" stays as is in URLs.
    private static readonly HashSet<string> PathValues = ["branch", "repo.branch", "repo.path", "repos.parent"];

    // Values that are URLs already.
    private static readonly HashSet<string> UrlValues = ["repo.webUrl"];

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z.]*)\}")]
    private static partial Regex Placeholder();

    public static IReadOnlyList<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static IReadOnlyList<string> Unknown(string template) =>
        Placeholders(template).Where(p => !Known.Contains(p)).ToList();

    // Null when a placeholder has no value. With forUrl, values are escaped for a URL.
    public static string? Render(string template, IReadOnlyDictionary<string, string?> values, bool forUrl)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (!values.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
            {
                missing = true;
                return "";
            }
            return forUrl ? Escape(name, value) : value;
        });
        return missing ? null : result;
    }

    public static string EscapePath(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));

    private static string Escape(string name, string value) =>
        UrlValues.Contains(name) ? value
        : PathValues.Contains(name) ? EscapePath(value)
        : Uri.EscapeDataString(value);
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.TemplateTests"`
Expected: `Passed!  - Failed:     0, Passed:    10`.

Then run all Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.

- [ ] **Step 5: Commit**

```bash
git add src/ClaudeRing.Core/Template.cs tests/ClaudeRing.Core.Tests/TemplateTests.cs
git commit -m "Add templates with placeholders for custom menu items"
```

---

### Task 2: Settings and SettingsLoader

**Files:**
- Create: `src/ClaudeRing.Core/Settings.cs`
- Create: `src/ClaudeRing.Core/SettingsLoader.cs`
- Test: `tests/ClaudeRing.Core.Tests/SettingsLoaderTests.cs`

**Interfaces:**
- Consumes: `Template.Placeholders(string)`, `Template.Unknown(string)` (Task 1).
- Produces (all in namespace `ClaudeRing.Core`):
  - `public enum UiLanguage { Auto, En, Ru }`
  - `public enum GitHostType { GitLab, GitHub }`
  - `public enum TrackerType { None, Jira, Linear, YouTrack, GitHub }`
  - `public enum IdeType { None, Rider, Idea, WebStorm, PyCharm, GoLand, CLion, RubyMine, PhpStorm, VsCode, Cursor, Windsurf, Custom }`
  - `public sealed record GitSettings(GitHostType Host, string BaseUrl, string? Group, IReadOnlyList<string> Targets)` — `BaseUrl` has no trailing `/`.
  - `public sealed record TrackerSettings(TrackerType Type, string? BaseUrl = null, string? Workspace = null)`
  - `public sealed record IdeSettings(IdeType Type, string? App = null)` — `App` null = default app for the type.
  - `public abstract record MenuEntry` with `public static readonly IReadOnlyList<string> BuiltInIds` (`chat`, `ide`, `reviews`, `issue`, `repos`), `public sealed record BuiltIn(string Id) : MenuEntry`, `public sealed record Custom(string Label, string? Url, IReadOnlyList<string>? Run, bool PerRepo) : MenuEntry` (exactly one of `Url`/`Run` is set; `Run[0]` is the program).
  - `public sealed record Settings` with init properties `Language`, `RepoRoots`, `TaskKeyPattern`, `Git`, `Tracker`, `Ide`, `ChatMenu`, `SlotCount`, `ReopenDelayMs`, `ResetOnRingEvent`; `public const string DefaultTaskKeyPattern`; `public static string Home`; `public static string DefaultPath`.
  - `public sealed record SettingsResult(Settings? Settings, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)` — `Settings` is null exactly when `Errors` is not empty.
  - `public static class SettingsLoader` with `public static SettingsResult Load(string path)` (missing file → defaults) and `public static SettingsResult Parse(string json)`. Never throw.
  - Error and warning format: `"<field path>: <message>"`, e.g. `git.host: unknown value "bitbucket", expected one of gitlab, github`, `repoRoots[0]: ...`, `chatMenu[1]: ..., item skipped`. Whole-file problems use the path `config`.

Rules implemented here (decisions on top of the spec):
- Property names are matched case-insensitively; enum values too.
- `repoRoots` items and `ide.app`: `~` and `~/...` are expanded; a repo root must be absolute after that.
- `slotCount` must be 3..9 (the plugin has 9 slot commands; a page needs at least items + back).
- `taskKeyPattern` default keeps today's boundaries: `(?<![A-Za-z0-9])[A-Z][A-Z0-9]+-\d+(?!\d)`.
- Legacy fields, field by field, new fields win: `git.host` default gitlab; `git.baseUrl` ← `gitLabBaseUrl` (gitlab only); `git.group` ← `gitLabGroup` (gitlab only); `git.targets` ← `mrTargets[].branch` (labels ignored, the submenu shows `→ <branch>`); `tracker` (whole section) ← `jiraBaseUrl`; `ide` (whole section) ← rider when the config has any legacy field, with `ide.app` taken from `riderPath` up to `.app`. Each legacy field present gives a warning.
- A custom item is skipped with a warning when: no label; not exactly one of `url`/`run`; `run` empty or not all strings; a placeholder in the program (`run[0]`); an unknown placeholder; a `{repo.*}` placeholder without `"perRepo": true`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudeRing.Core.Tests/SettingsLoaderTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class SettingsLoaderTests
{
    private static Settings Valid(string json)
    {
        var r = SettingsLoader.Parse(json);
        Assert.Empty(r.Errors);
        Assert.NotNull(r.Settings);
        return r.Settings!;
    }

    private static string Error(string json)
    {
        var r = SettingsLoader.Parse(json);
        Assert.Null(r.Settings);
        return Assert.Single(r.Errors);
    }

    private static string[] Ids(Settings s) => s.ChatMenu.OfType<MenuEntry.BuiltIn>().Select(b => b.Id).ToArray();

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var r = SettingsLoader.Load("/nonexistent/config.json");
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
        var s = r.Settings!;
        Assert.Equal(UiLanguage.Auto, s.Language);
        Assert.Equal([Path.Combine(Settings.Home, "Projects")], s.RepoRoots.ToArray());
        Assert.Equal(Settings.DefaultTaskKeyPattern, s.TaskKeyPattern);
        Assert.Equal(GitHostType.GitLab, s.Git.Host);
        Assert.Equal("https://gitlab.com", s.Git.BaseUrl);
        Assert.Null(s.Git.Group);
        Assert.Equal(["main"], s.Git.Targets.ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.None), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.None), s.Ide);
        Assert.Equal(["chat", "ide", "reviews", "issue", "repos"], Ids(s));
        Assert.Equal(8, s.SlotCount);
        Assert.Equal(150, s.ReopenDelayMs);
        Assert.Equal("", s.ResetOnRingEvent);
    }

    [Fact]
    public void File_is_read()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """{ "slotCount": 6 }""");
        Assert.Equal(6, SettingsLoader.Load(path).Settings!.SlotCount);
    }

    [Fact]
    public void Empty_object_gives_defaults()
    {
        var s = Valid("{}");
        Assert.Equal(8, s.SlotCount);
        Assert.Equal(GitHostType.GitLab, s.Git.Host);
        Assert.Equal(new IdeSettings(IdeType.None), s.Ide);
    }

    [Fact]
    public void Full_example_with_comments_and_trailing_commas()
    {
        var s = Valid("""
            {
              // labels
              "language": "ru",
              "repoRoots": ["~/Projects", "/work"],
              "taskKeyPattern": "#(\\d+)",
              "git": { "host": "github", "group": "acme", "targets": ["develop", "main"], },
              "tracker": { "type": "linear", "workspace": "acme" },
              "ide": { "type": "cursor" },
              "chatMenu": [
                "chat", "reviews",
                { "label": "Sentry", "url": "https://sentry.io/issues/?query={key}" },
                { "label": "Terminal", "run": ["open", "-a", "Terminal", "{repo.path}"], "perRepo": true },
              ],
              "slotCount": 9,
              "reopenDelayMs": 200,
              "resetOnRingEvent": "MouseLeave",
            }
            """);

        Assert.Equal(UiLanguage.Ru, s.Language);
        Assert.Equal([Path.Combine(Settings.Home, "Projects"), "/work"], s.RepoRoots.ToArray());
        Assert.Equal(@"#(\d+)", s.TaskKeyPattern);
        Assert.Equal(GitHostType.GitHub, s.Git.Host);
        Assert.Equal("https://github.com", s.Git.BaseUrl);
        Assert.Equal("acme", s.Git.Group);
        Assert.Equal(["develop", "main"], s.Git.Targets.ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.Linear, null, "acme"), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.Cursor), s.Ide);
        Assert.Equal(4, s.ChatMenu.Count);
        Assert.Equal(["chat", "reviews"], Ids(s));
        var sentry = Assert.IsType<MenuEntry.Custom>(s.ChatMenu[2]);
        Assert.Equal("Sentry", sentry.Label);
        Assert.Equal("https://sentry.io/issues/?query={key}", sentry.Url);
        Assert.Null(sentry.Run);
        Assert.False(sentry.PerRepo);
        var terminal = Assert.IsType<MenuEntry.Custom>(s.ChatMenu[3]);
        Assert.Null(terminal.Url);
        Assert.Equal(["open", "-a", "Terminal", "{repo.path}"], terminal.Run!.ToArray());
        Assert.True(terminal.PerRepo);
        Assert.Equal(9, s.SlotCount);
        Assert.Equal(200, s.ReopenDelayMs);
        Assert.Equal("MouseLeave", s.ResetOnRingEvent);
    }

    [Fact]
    public void Names_and_values_ignore_case()
    {
        var s = Valid("""{ "Git": { "Host": "GitHub" }, "SLOTCOUNT": 5 }""");
        Assert.Equal(GitHostType.GitHub, s.Git.Host);
        Assert.Equal(5, s.SlotCount);
    }

    [Fact]
    public void Home_is_expanded()
    {
        var s = Valid("""{ "repoRoots": ["~/Projects", "~"], "ide": { "type": "custom", "app": "~/Applications/Zed.app" } }""");
        Assert.Equal([Path.Combine(Settings.Home, "Projects"), Settings.Home], s.RepoRoots.ToArray());
        Assert.Equal(Path.Combine(Settings.Home, "Applications/Zed.app"), s.Ide.App);
    }

    [Fact]
    public void Github_enterprise_base_url_has_no_trailing_slash()
    {
        var s = Valid("""{ "git": { "host": "github", "baseUrl": "https://ghe.example.com/" } }""");
        Assert.Equal("https://ghe.example.com", s.Git.BaseUrl);
    }

    [Fact]
    public void Legacy_fields_map_to_the_new_schema()
    {
        var r = SettingsLoader.Parse("""
            {
              "repoRoots": ["/work"],
              "gitLabBaseUrl": "https://gitlab.example.com/",
              "gitLabGroup": "acme",
              "jiraBaseUrl": "https://example.atlassian.net",
              "mrTargets": [ { "label": "→ Dev", "branch": "Dev" }, { "label": "→ RC", "branch": "RC" } ],
              "slotCount": 9,
              "reopenDelayMs": 150,
              "resetOnRingEvent": ""
            }
            """);

        Assert.Empty(r.Errors);
        var s = r.Settings!;
        Assert.Equal(GitHostType.GitLab, s.Git.Host);
        Assert.Equal("https://gitlab.example.com", s.Git.BaseUrl);
        Assert.Equal("acme", s.Git.Group);
        Assert.Equal(["Dev", "RC"], s.Git.Targets.ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.Jira, "https://example.atlassian.net"), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.Rider), s.Ide);
        Assert.Equal(9, s.SlotCount);
        Assert.Contains(r.Warnings, w => w.StartsWith("gitLabBaseUrl:"));
        Assert.Contains(r.Warnings, w => w.StartsWith("mrTargets:"));
    }

    [Fact]
    public void Legacy_rider_path_gives_the_app()
    {
        var s = Valid("""{ "riderPath": "/Apps/Rider 2025.app/Contents/MacOS/rider" }""");
        Assert.Equal(new IdeSettings(IdeType.Rider, "/Apps/Rider 2025.app"), s.Ide);
    }

    [Fact]
    public void Legacy_rider_path_without_app_bundle_uses_the_default_app()
    {
        var r = SettingsLoader.Parse("""{ "riderPath": "/usr/local/bin/rider" }""");
        Assert.Equal(new IdeSettings(IdeType.Rider), r.Settings!.Ide);
        Assert.Contains(r.Warnings, w => w.StartsWith("riderPath:") && w.Contains(".app"));
    }

    [Fact]
    public void New_fields_win_over_legacy_fields()
    {
        var s = Valid("""
            {
              "gitLabBaseUrl": "https://old.example.com",
              "gitLabGroup": "old",
              "jiraBaseUrl": "https://old.atlassian.net",
              "mrTargets": [ { "branch": "old" } ],
              "riderPath": "/Applications/Rider.app/Contents/MacOS/rider",
              "git": { "baseUrl": "https://new.example.com", "targets": ["new"] },
              "tracker": { "type": "youtrack", "baseUrl": "https://yt.example.com" },
              "ide": { "type": "idea" }
            }
            """);
        Assert.Equal("https://new.example.com", s.Git.BaseUrl);
        Assert.Equal("old", s.Git.Group);
        Assert.Equal(["new"], s.Git.Targets.ToArray());
        Assert.Equal(new TrackerSettings(TrackerType.YouTrack, "https://yt.example.com"), s.Tracker);
        Assert.Equal(new IdeSettings(IdeType.Idea), s.Ide);
    }

    [Theory]
    [InlineData("""{ "git": { "host": "bitbucket" } }""", "git.host:")]
    [InlineData("""{ "tracker": { "type": "trello" } }""", "tracker.type:")]
    [InlineData("""{ "ide": { "type": "notepad" } }""", "ide.type:")]
    [InlineData("""{ "ide": { "type": "custom" } }""", "ide.app:")]
    [InlineData("""{ "taskKeyPattern": "([A-Z" }""", "taskKeyPattern:")]
    [InlineData("""{ "language": "de" }""", "language:")]
    [InlineData("""{ "slotCount": 12 }""", "slotCount:")]
    [InlineData("""{ "slotCount": "nine" }""", "slotCount:")]
    [InlineData("""{ "repoRoots": ["Projects"] }""", "repoRoots[0]:")]
    [InlineData("""{ "repoRoots": "/work" }""", "repoRoots:")]
    [InlineData("""{ "git": { "baseUrl": "gitlab.example.com" } }""", "git.baseUrl:")]
    [InlineData("""{ "git": { "targets": ["main", 5] } }""", "git.targets[1]:")]
    [InlineData("""{ "git": "gitlab" }""", "git:")]
    [InlineData("""{ "chatMenu": "chat" }""", "chatMenu:")]
    [InlineData("""{ "mrTargets": [ { "label": "x" } ] }""", "mrTargets[0].branch:")]
    [InlineData("[]", "config:")]
    [InlineData("{ not json", "config:")]
    public void Invalid_value_is_an_error_with_its_field_path(string json, string path) =>
        Assert.StartsWith(path, Error(json));

    [Fact]
    public void All_errors_are_reported()
    {
        var r = SettingsLoader.Parse("""{ "git": { "host": "x" }, "slotCount": 0 }""");
        Assert.Null(r.Settings);
        Assert.Equal(2, r.Errors.Count);
    }

    [Fact]
    public void Unknown_menu_item_is_skipped_with_a_warning()
    {
        var r = SettingsLoader.Parse("""{ "chatMenu": ["chat", "pipelines", "repos"] }""");
        Assert.Empty(r.Errors);
        Assert.Equal(["chat", "repos"], Ids(r.Settings!));
        Assert.StartsWith("chatMenu[1]:", Assert.Single(r.Warnings));
    }

    [Theory]
    [InlineData("""{ "url": "https://x/{key}" }""")]
    [InlineData("""{ "label": "X" }""")]
    [InlineData("""{ "label": "X", "url": "https://x", "run": ["open"] }""")]
    [InlineData("""{ "label": "X", "run": [] }""")]
    [InlineData("""{ "label": "X", "run": ["open", 5] }""")]
    [InlineData("""{ "label": "X", "url": "https://x/{ticket}" }""")]
    [InlineData("""{ "label": "X", "run": ["{repo.path}/run.sh"], "perRepo": true }""")]
    [InlineData("""{ "label": "X", "url": "{repo.webUrl}/pipelines" }""")]
    [InlineData("5")]
    public void Invalid_custom_item_is_skipped_with_a_warning(string item)
    {
        var r = SettingsLoader.Parse($$"""{ "chatMenu": ["chat", {{item}}] }""");
        Assert.Empty(r.Errors);
        Assert.Single(r.Settings!.ChatMenu);
        Assert.Equal(["chat"], Ids(r.Settings!));
        Assert.StartsWith("chatMenu[1]:", Assert.Single(r.Warnings));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.SettingsLoaderTests"`
Expected: build FAIL with `error CS0246: The type or namespace name 'Settings' could not be found`.

- [ ] **Step 3: Implement `Settings`**

Create `src/ClaudeRing.Core/Settings.cs`:

```csharp
namespace ClaudeRing.Core;

public enum UiLanguage { Auto, En, Ru }

public enum GitHostType { GitLab, GitHub }

public enum TrackerType { None, Jira, Linear, YouTrack, GitHub }

public enum IdeType { None, Rider, Idea, WebStorm, PyCharm, GoLand, CLion, RubyMine, PhpStorm, VsCode, Cursor, Windsurf, Custom }

// BaseUrl has no trailing "/".
public sealed record GitSettings(GitHostType Host, string BaseUrl, string? Group, IReadOnlyList<string> Targets);

public sealed record TrackerSettings(TrackerType Type, string? BaseUrl = null, string? Workspace = null);

// App null means the default app of the IDE type.
public sealed record IdeSettings(IdeType Type, string? App = null);

// One item of the chat page.
public abstract record MenuEntry
{
    public static readonly IReadOnlyList<string> BuiltInIds = ["chat", "ide", "reviews", "issue", "repos"];

    public sealed record BuiltIn(string Id) : MenuEntry;

    // Exactly one of Url and Run is set. Run holds the program first, then argument templates.
    public sealed record Custom(string Label, string? Url, IReadOnlyList<string>? Run, bool PerRepo) : MenuEntry;
}

public sealed record Settings
{
    // A Jira-like key such as PROJ-123, not inside a longer word or number.
    public const string DefaultTaskKeyPattern = @"(?<![A-Za-z0-9])[A-Z][A-Z0-9]+-\d+(?!\d)";

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DefaultPath => Path.Combine(Home, ".config", "claude-ring", "config.json");

    public UiLanguage Language { get; init; } = UiLanguage.Auto;

    public IReadOnlyList<string> RepoRoots { get; init; } = [Path.Combine(Home, "Projects")];

    public string TaskKeyPattern { get; init; } = DefaultTaskKeyPattern;

    public GitSettings Git { get; init; } = new(GitHostType.GitLab, "https://gitlab.com", null, ["main"]);

    public TrackerSettings Tracker { get; init; } = new(TrackerType.None);

    public IdeSettings Ide { get; init; } = new(IdeType.None);

    public IReadOnlyList<MenuEntry> ChatMenu { get; init; } =
        MenuEntry.BuiltInIds.Select(id => (MenuEntry)new MenuEntry.BuiltIn(id)).ToList();

    public int SlotCount { get; init; } = 8;

    public int ReopenDelayMs { get; init; } = 150;

    // Ring service event that means "the ring was closed". Empty = never reset by itself.
    public string ResetOnRingEvent { get; init; } = "";
}
```

- [ ] **Step 4: Implement `SettingsLoader`**

Create `src/ClaudeRing.Core/SettingsLoader.cs`:

```csharp
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
            List<string> targets = TextList(git, "targets", "git.targets") ?? LegacyTargets(root) ?? ["main"];
            return new GitSettings(host, baseUrl, string.IsNullOrEmpty(group) ? null : group, targets);
        }

        private List<string>? LegacyTargets(JsonElement root)
        {
            if (Get(root, "mrTargets") is not { } value) return null;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Errors.Add("mrTargets: expected a list");
                return null;
            }
            var result = new List<string>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (Get(item, "branch") is { ValueKind: JsonValueKind.String } b && b.GetString() is { Length: > 0 } branch) result.Add(branch);
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
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.SettingsLoaderTests"`
Expected: `Failed:     0`.

Then all Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeRing.Core/Settings.cs src/ClaudeRing.Core/SettingsLoader.cs tests/ClaudeRing.Core.Tests/SettingsLoaderTests.cs
git commit -m "Add settings schema and loader with validation and legacy mapping"
```

---

### Task 3: Git hosts and issue trackers

**Files:**
- Create: `src/ClaudeRing.Core/GitHosts.cs`
- Create: `src/ClaudeRing.Core/Trackers.cs`
- Test: `tests/ClaudeRing.Core.Tests/GitHostTests.cs`, `tests/ClaudeRing.Core.Tests/TrackerTests.cs`

`Links.cs` stays for now (RingMenu still uses it); Task 8 deletes it. The GitLab code is copied from `Links`.

**Interfaces:**
- Consumes: `GitSettings`, `GitHostType`, `TrackerSettings`, `TrackerType` (Task 2); `Template.EscapePath` (Task 1); `RepoRef` (Models.cs).
- Produces:
  - `public interface IGitHost { string ReviewsLabel { get; } string? BranchUrl(RepoRef repo); string ReviewsUrl(string key, string? target); }`
  - `public sealed class GitLabHost(GitSettings git) : IGitHost` — label `MR`.
  - `public sealed class GitHubHost(GitSettings git) : IGitHost` — label `PR`.
  - `public static class GitHosts { public static IGitHost Create(GitSettings git); }`
  - `public interface ITracker { string Name { get; } string? IssueUrl(string key, IReadOnlyList<RepoRef> repos); }`
  - `JiraTracker(string? baseUrl)` name `Jira`; `YouTrackTracker(string? baseUrl)` name `YouTrack`; `LinearTracker(string? workspace)` name `Linear`; `GitHubIssuesTracker()` name `Issue`; `NoTracker()` name `""`.
  - `public static class Trackers { public static ITracker Create(TrackerSettings tracker); }`

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudeRing.Core.Tests/GitHostTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class GitHostTests
{
    private static readonly GitSettings GitLab = new(GitHostType.GitLab, "https://gitlab.example.com", "acme", ["main"]);
    private static readonly GitSettings GitHub = new(GitHostType.GitHub, "https://github.com", null, ["main"]);

    [Fact]
    public void Factory_picks_the_host()
    {
        Assert.IsType<GitLabHost>(GitHosts.Create(GitLab));
        Assert.IsType<GitHubHost>(GitHosts.Create(GitHub));
    }

    [Fact]
    public void Review_labels()
    {
        Assert.Equal("MR", new GitLabHost(GitLab).ReviewsLabel);
        Assert.Equal("PR", new GitHubHost(GitHub).ReviewsLabel);
    }

    [Fact]
    public void Gitlab_group_mrs_by_key() => Assert.Equal(
        "https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069",
        new GitLabHost(GitLab).ReviewsUrl("PROJ-1069", null));

    [Fact]
    public void Gitlab_group_mrs_by_key_and_target() => Assert.Equal(
        "https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=RC",
        new GitLabHost(GitLab).ReviewsUrl("PROJ-1069", "RC"));

    [Fact]
    public void Gitlab_dashboard_mrs_without_group() => Assert.Equal(
        "https://gitlab.example.com/dashboard/merge_requests?scope=all&state=all&search=PROJ-1069&target_branch=develop",
        new GitLabHost(GitLab with { Group = null }).ReviewsUrl("PROJ-1069", "develop"));

    [Fact]
    public void Gitlab_branch_link() => Assert.Equal(
        "https://gitlab.example.com/acme/api-service/-/tree/feature/PROJ-1069",
        new GitLabHost(GitLab).BranchUrl(new RepoRef("api-service", "/p", "feature/PROJ-1069", "https://gitlab.example.com/acme/api-service")));

    [Fact]
    public void Branch_link_is_null_without_web_url()
    {
        Assert.Null(new GitLabHost(GitLab).BranchUrl(new RepoRef("a", "/p", "b")));
        Assert.Null(new GitHubHost(GitHub).BranchUrl(new RepoRef("a", "/p", "b")));
    }

    [Fact]
    public void Github_pulls_by_key() => Assert.Equal(
        "https://github.com/pulls?q=is:pr+PROJ-1069",
        new GitHubHost(GitHub).ReviewsUrl("PROJ-1069", null));

    [Fact]
    public void Github_pulls_by_key_target_and_org() => Assert.Equal(
        "https://github.com/pulls?q=is:pr+PROJ-1069+base:develop+org:acme",
        new GitHubHost(GitHub with { Group = "acme" }).ReviewsUrl("PROJ-1069", "develop"));

    [Fact]
    public void Github_enterprise_uses_base_url() => Assert.Equal(
        "https://ghe.example.com/pulls?q=is:pr+PROJ-1069",
        new GitHubHost(GitHub with { BaseUrl = "https://ghe.example.com" }).ReviewsUrl("PROJ-1069", null));

    [Fact]
    public void Github_branch_link() => Assert.Equal(
        "https://github.com/acme/api/tree/feature/PROJ-1069",
        new GitHubHost(GitHub).BranchUrl(new RepoRef("api", "/p", "feature/PROJ-1069", "https://github.com/acme/api")));
}
```

Create `tests/ClaudeRing.Core.Tests/TrackerTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TrackerTests
{
    private static readonly IReadOnlyList<RepoRef> NoRepos = [];

    [Fact]
    public void Factory_picks_the_tracker()
    {
        Assert.IsType<JiraTracker>(Trackers.Create(new TrackerSettings(TrackerType.Jira, "https://j")));
        Assert.IsType<YouTrackTracker>(Trackers.Create(new TrackerSettings(TrackerType.YouTrack, "https://y")));
        Assert.IsType<LinearTracker>(Trackers.Create(new TrackerSettings(TrackerType.Linear, Workspace: "acme")));
        Assert.IsType<GitHubIssuesTracker>(Trackers.Create(new TrackerSettings(TrackerType.GitHub)));
        Assert.IsType<NoTracker>(Trackers.Create(new TrackerSettings(TrackerType.None)));
    }

    [Fact]
    public void Jira_link()
    {
        var jira = new JiraTracker("https://example.atlassian.net/");
        Assert.Equal("Jira", jira.Name);
        Assert.Equal("https://example.atlassian.net/browse/PROJ-1069", jira.IssueUrl("PROJ-1069", NoRepos));
    }

    [Fact]
    public void Jira_without_base_url_has_no_link() => Assert.Null(new JiraTracker(null).IssueUrl("A-1", NoRepos));

    [Fact]
    public void Youtrack_link()
    {
        var youTrack = new YouTrackTracker("https://acme.youtrack.cloud");
        Assert.Equal("YouTrack", youTrack.Name);
        Assert.Equal("https://acme.youtrack.cloud/issue/PROJ-7", youTrack.IssueUrl("PROJ-7", NoRepos));
    }

    [Fact]
    public void Linear_link()
    {
        var linear = new LinearTracker("acme");
        Assert.Equal("Linear", linear.Name);
        Assert.Equal("https://linear.app/acme/issue/ENG-12", linear.IssueUrl("ENG-12", NoRepos));
    }

    [Fact]
    public void Linear_without_workspace_has_no_link() => Assert.Null(new LinearTracker(null).IssueUrl("ENG-12", NoRepos));

    [Fact]
    public void Github_issue_uses_the_first_repo_with_a_web_url()
    {
        var repos = new[] { new RepoRef("local", "/p/local", "123-fix"), new RepoRef("api", "/p/api", "123-fix", "https://github.com/acme/api") };
        var issues = new GitHubIssuesTracker();
        Assert.Equal("Issue", issues.Name);
        Assert.Equal("https://github.com/acme/api/issues/123", issues.IssueUrl("123", repos));
    }

    [Fact]
    public void Github_issue_needs_a_number_and_a_web_url()
    {
        var withUrl = new[] { new RepoRef("api", "/p/api", "b", "https://github.com/acme/api") };
        Assert.Null(new GitHubIssuesTracker().IssueUrl("PROJ-1", withUrl));
        Assert.Null(new GitHubIssuesTracker().IssueUrl("123", [new RepoRef("api", "/p/api", "b")]));
    }

    [Fact]
    public void No_tracker_has_no_link() => Assert.Null(new NoTracker().IssueUrl("A-1", NoRepos));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.GitHostTests|FullyQualifiedName~ClaudeRing.Core.Tests.TrackerTests"`
Expected: build FAIL with `error CS0246: The type or namespace name 'GitLabHost' could not be found`.

- [ ] **Step 3: Implement git hosts**

Create `src/ClaudeRing.Core/GitHosts.cs`:

```csharp
namespace ClaudeRing.Core;

public interface IGitHost
{
    // Slot label of the reviews item: "MR" or "PR".
    string ReviewsLabel { get; }

    // Repo page on its current branch. Null when the repo has no web URL.
    string? BranchUrl(RepoRef repo);

    // All reviews for the task key, or only those into the target branch.
    string ReviewsUrl(string key, string? target);
}

public static class GitHosts
{
    public static IGitHost Create(GitSettings git) =>
        git.Host == GitHostType.GitHub ? new GitHubHost(git) : new GitLabHost(git);
}

public sealed class GitLabHost(GitSettings git) : IGitHost
{
    public string ReviewsLabel => "MR";

    public string? BranchUrl(RepoRef repo) =>
        repo.WebUrl == null ? null : $"{repo.WebUrl}/-/tree/{Template.EscapePath(repo.Branch)}";

    public string ReviewsUrl(string key, string? target)
    {
        var search = $"scope=all&state=all&search={Uri.EscapeDataString(key)}";
        var url = string.IsNullOrEmpty(git.Group)
            ? $"{git.BaseUrl}/dashboard/merge_requests?{search}"
            : $"{git.BaseUrl}/groups/{git.Group}/-/merge_requests?{search}";
        return target == null ? url : url + "&target_branch=" + Uri.EscapeDataString(target);
    }
}

public sealed class GitHubHost(GitSettings git) : IGitHost
{
    public string ReviewsLabel => "PR";

    public string? BranchUrl(RepoRef repo) =>
        repo.WebUrl == null ? null : $"{repo.WebUrl}/tree/{Template.EscapePath(repo.Branch)}";

    public string ReviewsUrl(string key, string? target)
    {
        var query = "is:pr+" + Uri.EscapeDataString(key);
        if (target != null) query += "+base:" + Uri.EscapeDataString(target);
        if (!string.IsNullOrEmpty(git.Group)) query += "+org:" + Uri.EscapeDataString(git.Group);
        return $"{git.BaseUrl}/pulls?q={query}";
    }
}
```

- [ ] **Step 4: Implement trackers**

Create `src/ClaudeRing.Core/Trackers.cs`:

```csharp
namespace ClaudeRing.Core;

public interface ITracker
{
    // Slot label of the issue item.
    string Name { get; }

    // Null when the link cannot be built (missing settings or a key the tracker does not use).
    string? IssueUrl(string key, IReadOnlyList<RepoRef> repos);
}

public static class Trackers
{
    public static ITracker Create(TrackerSettings tracker) => tracker.Type switch
    {
        TrackerType.Jira => new JiraTracker(tracker.BaseUrl),
        TrackerType.YouTrack => new YouTrackTracker(tracker.BaseUrl),
        TrackerType.Linear => new LinearTracker(tracker.Workspace),
        TrackerType.GitHub => new GitHubIssuesTracker(),
        _ => new NoTracker(),
    };
}

public sealed class JiraTracker(string? baseUrl) : ITracker
{
    public string Name => "Jira";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) =>
        string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(key)}";
}

public sealed class YouTrackTracker(string? baseUrl) : ITracker
{
    public string Name => "YouTrack";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) =>
        string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/issue/{Uri.EscapeDataString(key)}";
}

public sealed class LinearTracker(string? workspace) : ITracker
{
    public string Name => "Linear";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) =>
        string.IsNullOrEmpty(workspace)
            ? null
            : $"https://linear.app/{Uri.EscapeDataString(workspace)}/issue/{Uri.EscapeDataString(key)}";
}

// The key must be the issue number, e.g. from "taskKeyPattern": "#(\\d+)".
public sealed class GitHubIssuesTracker : ITracker
{
    public string Name => "Issue";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos)
    {
        if (key.Length == 0 || !key.All(char.IsAsciiDigit)) return null;
        var webUrl = repos.FirstOrDefault(r => r.WebUrl != null)?.WebUrl;
        return webUrl == null ? null : $"{webUrl}/issues/{key}";
    }
}

public sealed class NoTracker : ITracker
{
    public string Name => "";

    public string? IssueUrl(string key, IReadOnlyList<RepoRef> repos) => null;
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.GitHostTests|FullyQualifiedName~ClaudeRing.Core.Tests.TrackerTests"`
Expected: `Failed:     0`.

Then all Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeRing.Core/GitHosts.cs src/ClaudeRing.Core/Trackers.cs tests/ClaudeRing.Core.Tests/GitHostTests.cs tests/ClaudeRing.Core.Tests/TrackerTests.cs
git commit -m "Add GitLab and GitHub hosts and issue tracker presets"
```

---

### Task 4: IDE launch and the Run action

**Files:**
- Create: `src/ClaudeRing.Core/IdeLauncher.cs`
- Modify: `src/ClaudeRing.Core/RingMenu.cs` (add two `MenuAction` records)
- Modify: `src/ClaudeRing.Core/Launcher.cs` (add `OpenIde`, `Run`; keep `OpenInRider` and the `RingConfig` parameter until Task 8)
- Test: `tests/ClaudeRing.Core.Tests/IdeLauncherTests.cs`, `tests/ClaudeRing.Core.Tests/LauncherTests.cs`

**Interfaces:**
- Consumes: `IdeSettings`, `IdeType` (Task 2); `IProcessRunner`, `FakeProcessRunner`.
- Produces:
  - `MenuAction.OpenIde(IdeSettings Ide, IReadOnlyList<RepoRef> Repos)` and `MenuAction.Run(string File, IReadOnlyList<string> Args)` (nested records in `MenuAction`, file `RingMenu.cs`).
  - `public static class IdeLauncher` with `public static string DisplayName(IdeSettings ide)` and `public static IReadOnlyList<MenuAction.Run> Commands(IdeSettings ide, IReadOnlyList<string> paths)`.
  - `Launcher.OpenIde(IdeSettings ide, IReadOnlyList<RepoRef> repos)` and `Launcher.Run(string file, IReadOnlyList<string> args)`. Both let process start errors through (the plugin logs them; the .NET message names the program path).

IDE table (display name / default app / binary):

| Type | Name | Default app | Command |
|---|---|---|---|
| Rider | Rider | `/Applications/Rider.app` | `{app}/Contents/MacOS/rider <all paths>` |
| Idea | IDEA | `/Applications/IntelliJ IDEA.app` | `.../MacOS/idea` |
| WebStorm | WebStorm | `/Applications/WebStorm.app` | `.../MacOS/webstorm` |
| PyCharm | PyCharm | `/Applications/PyCharm.app` | `.../MacOS/pycharm` |
| GoLand | GoLand | `/Applications/GoLand.app` | `.../MacOS/goland` |
| CLion | CLion | `/Applications/CLion.app` | `.../MacOS/clion` |
| RubyMine | RubyMine | `/Applications/RubyMine.app` | `.../MacOS/rubymine` |
| PhpStorm | PhpStorm | `/Applications/PhpStorm.app` | `.../MacOS/phpstorm` |
| VsCode | VS Code | `/Applications/Visual Studio Code.app` | `{app}/Contents/Resources/app/bin/code -n <path>` per path |
| Cursor | Cursor | `/Applications/Cursor.app` | `.../bin/cursor -n <path>` per path |
| Windsurf | Windsurf | `/Applications/Windsurf.app` | `.../bin/windsurf -n <path>` per path |
| Custom | app file name without `.app` | none (`app` required) | `open -a {app} <path>` per path |

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudeRing.Core.Tests/IdeLauncherTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class IdeLauncherTests
{
    [Theory]
    [InlineData(IdeType.Rider, "/Applications/Rider.app/Contents/MacOS/rider")]
    [InlineData(IdeType.Idea, "/Applications/IntelliJ IDEA.app/Contents/MacOS/idea")]
    [InlineData(IdeType.WebStorm, "/Applications/WebStorm.app/Contents/MacOS/webstorm")]
    [InlineData(IdeType.PyCharm, "/Applications/PyCharm.app/Contents/MacOS/pycharm")]
    [InlineData(IdeType.GoLand, "/Applications/GoLand.app/Contents/MacOS/goland")]
    [InlineData(IdeType.CLion, "/Applications/CLion.app/Contents/MacOS/clion")]
    [InlineData(IdeType.RubyMine, "/Applications/RubyMine.app/Contents/MacOS/rubymine")]
    [InlineData(IdeType.PhpStorm, "/Applications/PhpStorm.app/Contents/MacOS/phpstorm")]
    public void Jetbrains_opens_all_paths_in_one_call(IdeType type, string binary)
    {
        var command = Assert.Single(IdeLauncher.Commands(new IdeSettings(type), ["/p/a", "/p/b"]));
        Assert.Equal(binary, command.File);
        Assert.Equal(["/p/a", "/p/b"], command.Args);
    }

    [Fact]
    public void App_setting_replaces_the_default_app()
    {
        var command = Assert.Single(IdeLauncher.Commands(new IdeSettings(IdeType.Rider, "/Users/me/Applications/Rider.app/"), ["/p/a"]));
        Assert.Equal("/Users/me/Applications/Rider.app/Contents/MacOS/rider", command.File);
    }

    [Theory]
    [InlineData(IdeType.VsCode, "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code")]
    [InlineData(IdeType.Cursor, "/Applications/Cursor.app/Contents/Resources/app/bin/cursor")]
    [InlineData(IdeType.Windsurf, "/Applications/Windsurf.app/Contents/Resources/app/bin/windsurf")]
    public void Vs_code_family_opens_one_window_per_path(IdeType type, string cli)
    {
        var commands = IdeLauncher.Commands(new IdeSettings(type), ["/p/a", "/p/b"]);
        Assert.Equal(2, commands.Count);
        Assert.All(commands, c => Assert.Equal(cli, c.File));
        Assert.Equal(["-n", "/p/a"], commands[0].Args);
        Assert.Equal(["-n", "/p/b"], commands[1].Args);
    }

    [Fact]
    public void Custom_ide_uses_open_once_per_path()
    {
        var commands = IdeLauncher.Commands(new IdeSettings(IdeType.Custom, "/Applications/Zed.app"), ["/p/a", "/p/b"]);
        Assert.Equal(2, commands.Count);
        Assert.Equal("open", commands[0].File);
        Assert.Equal(["-a", "/Applications/Zed.app", "/p/a"], commands[0].Args);
        Assert.Equal(["-a", "/Applications/Zed.app", "/p/b"], commands[1].Args);
    }

    [Fact]
    public void No_ide_or_no_paths_gives_no_commands()
    {
        Assert.Empty(IdeLauncher.Commands(new IdeSettings(IdeType.None), ["/p/a"]));
        Assert.Empty(IdeLauncher.Commands(new IdeSettings(IdeType.Rider), []));
    }

    [Theory]
    [InlineData(IdeType.Rider, "Rider")]
    [InlineData(IdeType.Idea, "IDEA")]
    [InlineData(IdeType.VsCode, "VS Code")]
    [InlineData(IdeType.Cursor, "Cursor")]
    public void Display_name(IdeType type, string name) =>
        Assert.Equal(name, IdeLauncher.DisplayName(new IdeSettings(type)));

    [Fact]
    public void Custom_display_name_is_the_app_name() =>
        Assert.Equal("Zed", IdeLauncher.DisplayName(new IdeSettings(IdeType.Custom, "/Applications/Zed.app")));
}
```

Add to `tests/ClaudeRing.Core.Tests/LauncherTests.cs` (inside the class, after `Open_urls_does_nothing_for_empty_list`):

```csharp
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
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.IdeLauncherTests|FullyQualifiedName~ClaudeRing.Core.Tests.LauncherTests"`
Expected: build FAIL with `error CS0103: The name 'IdeLauncher' does not exist in the current context`.

- [ ] **Step 3: Add the two actions**

In `src/ClaudeRing.Core/RingMenu.cs`, replace the `MenuAction` record:

```csharp
public abstract record MenuAction
{
    public sealed record FocusChat(ChatTab Tab) : MenuAction;
    public sealed record OpenRider(IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenIde(IdeSettings Ide, IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenUrl(string Url) : MenuAction;
    public sealed record OpenUrls(IReadOnlyList<string> Urls) : MenuAction;
    public sealed record Reveal(IReadOnlyList<string> Paths) : MenuAction;
    // Starts a program directly, without a shell.
    public sealed record Run(string File, IReadOnlyList<string> Args) : MenuAction;
}
```

- [ ] **Step 4: Implement `IdeLauncher`**

Create `src/ClaudeRing.Core/IdeLauncher.cs`:

```csharp
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
```

- [ ] **Step 5: Add `OpenIde` and `Run` to `Launcher`**

In `src/ClaudeRing.Core/Launcher.cs`, add after `Reveal` (keep everything else):

```csharp
    public void OpenIde(IdeSettings ide, IReadOnlyList<RepoRef> repos)
    {
        foreach (var command in IdeLauncher.Commands(ide, repos.Select(r => r.Path).ToList())) Run(command.File, command.Args);
    }

    // Start errors go to the caller; their message names the program path.
    public void Run(string file, IReadOnlyList<string> args) => runner.Start(file, [.. args]);
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.IdeLauncherTests|FullyQualifiedName~ClaudeRing.Core.Tests.LauncherTests"`
Expected: `Failed:     0`.

Then all Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.
Then the plugin compile check → no errors.

- [ ] **Step 7: Commit**

```bash
git add src/ClaudeRing.Core/IdeLauncher.cs src/ClaudeRing.Core/RingMenu.cs src/ClaudeRing.Core/Launcher.cs tests/ClaudeRing.Core.Tests/IdeLauncherTests.cs tests/ClaudeRing.Core.Tests/LauncherTests.cs
git commit -m "Add IDE presets and a Run action that starts a program"
```

---

### Task 5: Strings and system language

**Files:**
- Create: `src/ClaudeRing.Core/Strings.cs`
- Create: `src/ClaudeRing.Core/SystemLanguage.cs`
- Test: `tests/ClaudeRing.Core.Tests/StringsTests.cs`

**Interfaces:**
- Consumes: `UiLanguage` (Task 2); `IProcessRunner`, `FakeProcessRunner`.
- Produces:
  - `public sealed record Strings(string Chat, string Repos, string All, string AllInBrowser, string FolderInFinder, string More, string Back, string AllReviewsFormat)`
  - `public static readonly Strings En` = `"VS Code", "Repos", "All", "All in browser", "Folder in Finder", "more ›", "← Back", "All {0}s"`
  - `public static readonly Strings Ru` = `"VS Code", "Репо", "Все", "Все в браузере", "Папка в Finder", "ещё ›", "← назад", "Все {0}"`
  - `public string AllReviews(string reviewsLabel)` → `All MRs` / `All PRs` / `Все MR` / `Все PR`.
  - `public string Target(string branch)` → `→ {branch}`.
  - `public static Strings For(UiLanguage language, Func<string?> systemLanguage)` — `Auto`: `Ru` when the system language starts with `ru`, otherwise `En`. The function is called only for `Auto`.
  - `public static partial class SystemLanguage { public static string? Read(IProcessRunner runner); }` — runs `/usr/bin/defaults read -g AppleLanguages`, returns the first entry (e.g. `ru-RU`), or null. Never throws.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudeRing.Core.Tests/StringsTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class StringsTests
{
    [Fact]
    public void English_labels()
    {
        var s = Strings.En;
        Assert.Equal("VS Code", s.Chat);
        Assert.Equal("Repos", s.Repos);
        Assert.Equal("All", s.All);
        Assert.Equal("All in browser", s.AllInBrowser);
        Assert.Equal("Folder in Finder", s.FolderInFinder);
        Assert.Equal("more ›", s.More);
        Assert.Equal("← Back", s.Back);
        Assert.Equal("All MRs", s.AllReviews("MR"));
        Assert.Equal("All PRs", s.AllReviews("PR"));
        Assert.Equal("→ main", s.Target("main"));
    }

    [Fact]
    public void Russian_labels()
    {
        var s = Strings.Ru;
        Assert.Equal("VS Code", s.Chat);
        Assert.Equal("Репо", s.Repos);
        Assert.Equal("Все", s.All);
        Assert.Equal("Все в браузере", s.AllInBrowser);
        Assert.Equal("Папка в Finder", s.FolderInFinder);
        Assert.Equal("ещё ›", s.More);
        Assert.Equal("← назад", s.Back);
        Assert.Equal("Все MR", s.AllReviews("MR"));
        Assert.Equal("Все PR", s.AllReviews("PR"));
        Assert.Equal("→ Dev", s.Target("Dev"));
    }

    [Theory]
    [InlineData(UiLanguage.En, "ru-RU", "en")]
    [InlineData(UiLanguage.Ru, "en-US", "ru")]
    [InlineData(UiLanguage.Auto, "ru-RU", "ru")]
    [InlineData(UiLanguage.Auto, "uk-UA", "en")]
    [InlineData(UiLanguage.Auto, null, "en")]
    public void Picks_the_language(UiLanguage language, string? system, string expected) =>
        Assert.Same(expected == "ru" ? Strings.Ru : Strings.En, Strings.For(language, () => system));

    [Fact]
    public void Explicit_language_does_not_read_the_system() =>
        Assert.Same(Strings.En, Strings.For(UiLanguage.En, () => throw new InvalidOperationException("must not be called")));

    [Fact]
    public void Reads_the_first_quoted_system_language()
    {
        var runner = new FakeProcessRunner();
        string[]? call = null;
        runner.OnRun = (file, args) =>
        {
            call = [file, .. args];
            return "(\n    \"ru-RU\",\n    \"en-US\"\n)\n";
        };
        Assert.Equal("ru-RU", SystemLanguage.Read(runner));
        Assert.Equal(["/usr/bin/defaults", "read", "-g", "AppleLanguages"], call!);
    }

    [Fact]
    public void Reads_an_unquoted_system_language()
    {
        var runner = new FakeProcessRunner { OnRun = (_, _) => "(\n    en,\n    ru\n)\n" };
        Assert.Equal("en", SystemLanguage.Read(runner));
    }

    [Fact]
    public void System_language_is_null_when_defaults_fails()
    {
        var runner = new FakeProcessRunner { OnRun = (_, _) => throw new InvalidOperationException("exited with 1") };
        Assert.Null(SystemLanguage.Read(runner));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.StringsTests"`
Expected: build FAIL with `error CS0103: The name 'Strings' does not exist in the current context`.

- [ ] **Step 3: Implement `Strings`**

Create `src/ClaudeRing.Core/Strings.cs`:

```csharp
namespace ClaudeRing.Core;

using System.Globalization;

// Slot labels. Product names (VS Code, Rider, MR, Jira) are the same in every language.
public sealed record Strings(
    string Chat,
    string Repos,
    string All,
    string AllInBrowser,
    string FolderInFinder,
    string More,
    string Back,
    string AllReviewsFormat)
{
    public static readonly Strings En = new("VS Code", "Repos", "All", "All in browser", "Folder in Finder", "more ›", "← Back", "All {0}s");

    public static readonly Strings Ru = new("VS Code", "Репо", "Все", "Все в браузере", "Папка в Finder", "ещё ›", "← назад", "Все {0}");

    // "All MRs" / "Все MR".
    public string AllReviews(string reviewsLabel) => string.Format(CultureInfo.InvariantCulture, AllReviewsFormat, reviewsLabel);

    // Reviews into one target branch.
    public string Target(string branch) => "→ " + branch;

    // Auto uses the first macOS UI language: Russian for "ru", English for anything else.
    public static Strings For(UiLanguage language, Func<string?> systemLanguage) => language switch
    {
        UiLanguage.Ru => Ru,
        UiLanguage.En => En,
        _ => systemLanguage()?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true ? Ru : En,
    };
}
```

- [ ] **Step 4: Implement `SystemLanguage`**

Create `src/ClaudeRing.Core/SystemLanguage.cs`:

```csharp
namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

public static partial class SystemLanguage
{
    // One entry of the "defaults" list output: "ru-RU", or en without quotes.
    [GeneratedRegex(@"^\s*""?([A-Za-z]{2,3}(?:[-_][A-Za-z0-9]+)*)""?,?\s*$", RegexOptions.Multiline)]
    private static partial Regex Entry();

    // First macOS UI language, e.g. "ru-RU". Null when it cannot be read.
    public static string? Read(IProcessRunner runner)
    {
        try
        {
            var match = Entry().Match(runner.Run("/usr/bin/defaults", "read", "-g", "AppleLanguages"));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException
                                      or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.StringsTests"`
Expected: `Failed:     0`.

Then all Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeRing.Core/Strings.cs src/ClaudeRing.Core/SystemLanguage.cs tests/ClaudeRing.Core.Tests/StringsTests.cs
git commit -m "Add English and Russian slot labels with system language detection"
```

---

### Task 6: RingMenu page stack (refactor, same behaviour)

`RingMenu` replaces its fixed levels (`Root/Chat/Mr/Repos/Rider`) with a stack of `MenuPage` values. The constructor, labels and every existing test stay the same. New characterization tests pin back/reset/paging behaviour first; they pass before and after the change.

**Files:**
- Create: `src/ClaudeRing.Core/MenuPage.cs`
- Modify: `src/ClaudeRing.Core/RingMenu.cs` (whole file)
- Test: `tests/ClaudeRing.Core.Tests/RingMenuTests.cs` (add tests)

**Interfaces:**
- Consumes: `MenuAction` with `OpenIde` and `Run` (Task 4).
- Produces:
  - `public sealed record MenuItem(string Label, MenuAction? Action = null, MenuPage? Submenu = null)` — a final action or a submenu.
  - `public sealed record MenuPage(IReadOnlyList<MenuItem> Items)` — items without "back"; `RingMenu` adds back.
  - `RingMenu` public API unchanged: `RingMenu(IChatSource source, Links links, RingConfig config)`, `Reset()`, `Render()`, `Click(int)`, `LastLoadError`.
  - Private `static string DisplayName(RepoRef r, string? key)` and `static string CommonParent(IReadOnlyList<RepoRef> repos)` (Task 7 moves them to `MenuBuilder`).

- [ ] **Step 1: Add characterization tests**

Add to `tests/ClaudeRing.Core.Tests/RingMenuTests.cs` (inside the class, at the end):

```csharp
    [Fact]
    public void Back_twice_from_submenu_returns_to_root()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        menu.Click(2);
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("VS Code", Labels(menu)[0]);
        Assert.True(menu.Click(7).Reopen);
        Assert.Equal("PROJ-1069", Labels(menu)[0]);
    }

    [Fact]
    public void Reset_from_submenu_returns_to_root()
    {
        var menu = Menu(Chat("PROJ-1069"));
        menu.Click(0);
        menu.Click(2);
        menu.Reset();
        Assert.Equal("PROJ-1069", Labels(menu)[0]);
    }

    [Fact]
    public void Back_to_root_keeps_the_chat_page()
    {
        var menu = MenuN(8, false, Enumerable.Range(1, 10).Select(i => Chat($"A-{i}")).ToArray());
        menu.Click(7);
        menu.Click(0);
        Assert.Equal("VS Code", Labels(menu)[0]);
        menu.Click(7);
        Assert.Equal("A-8", Labels(menu)[0]);
    }

    [Fact]
    public void Final_action_from_a_later_page_returns_to_the_first_page()
    {
        var menu = MenuN(8, false, Enumerable.Range(1, 10).Select(i => Chat($"A-{i}")).ToArray());
        menu.Click(7);
        menu.Click(0);
        var r = menu.Click(0);
        Assert.IsType<MenuAction.FocusChat>(r.Action);
        Assert.Equal("A-1", Labels(menu)[0]);
    }
```

- [ ] **Step 2: Run them on the old code**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.RingMenuTests"`
Expected: `Failed:     0` (they describe today's behaviour).

- [ ] **Step 3: Add the page model**

Create `src/ClaudeRing.Core/MenuPage.cs`:

```csharp
namespace ClaudeRing.Core;

// One item on a menu page: a final action or a submenu.
public sealed record MenuItem(string Label, MenuAction? Action = null, MenuPage? Submenu = null);

// Items of a page in slot order, without "back"; RingMenu adds it.
public sealed record MenuPage(IReadOnlyList<MenuItem> Items);
```

- [ ] **Step 4: Rewrite `RingMenu` as a page stack**

Replace the whole content of `src/ClaudeRing.Core/RingMenu.cs`:

```csharp
namespace ClaudeRing.Core;

public abstract record MenuAction
{
    public sealed record FocusChat(ChatTab Tab) : MenuAction;
    public sealed record OpenRider(IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenIde(IdeSettings Ide, IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenUrl(string Url) : MenuAction;
    public sealed record OpenUrls(IReadOnlyList<string> Urls) : MenuAction;
    public sealed record Reveal(IReadOnlyList<string> Paths) : MenuAction;
    // Starts a program directly, without a shell.
    public sealed record Run(string File, IReadOnlyList<string> Args) : MenuAction;
}

public sealed record ClickResult(MenuAction? Action, bool Reopen);

public sealed record SlotView(string Label, bool Enabled);

public sealed class RingMenu(IChatSource source, Links links, RingConfig config)
{
    // One ring slot: what it shows and what a click does.
    private sealed record Slot(string Label, Func<ClickResult> OnClick);

    private const string Back = "← назад";
    private const string More = "ещё ›";

    private readonly object _lock = new();
    // Pages opened from the root; the top one is shown. Empty means the root page.
    private readonly Stack<MenuPage> _pages = new();
    private IReadOnlyList<ChatContext> _chats = [];
    private int _page;

    private int N => config.SlotCount;

    // The plugin logs this after Reset. Null after a successful load.
    public Exception? LastLoadError { get; private set; }

    // Never throws. Loads outside the lock; if loading fails, the previous chat list stays.
    public void Reset()
    {
        IReadOnlyList<ChatContext>? loaded = null;
        Exception? error = null;
        try
        {
            loaded = source.Load();
        }
        catch (Exception e)
        {
            error = e;
        }

        lock (_lock)
        {
            if (loaded != null) _chats = loaded;
            LastLoadError = error;
            ToRoot();
        }
    }

    public IReadOnlyList<SlotView> Render()
    {
        lock (_lock)
        {
            var slots = Slots();
            return Enumerable.Range(0, N)
                .Select(i => i < slots.Count && slots[i] != null ? new SlotView(slots[i]!.Label, true) : new SlotView("", false))
                .ToList();
        }
    }

    // A final action switches to the root page at once, under the lock. It does not reload chats:
    // the caller reloads chats after running the action, so the action never waits for the reload.
    public ClickResult Click(int slot)
    {
        ClickResult result;
        lock (_lock)
        {
            var slots = Slots();
            if (slot < 0 || slot >= slots.Count || slots[slot] == null) return new ClickResult(null, false);

            result = slots[slot]!.OnClick();
            if (!result.Reopen) ToRoot();
        }
        return result;
    }

    // Slots of the shown page. Null means an empty slot.
    private List<Slot?> Slots() => _pages.Count == 0 ? RootSlots() : PageSlots(_pages.Peek());

    private List<Slot?> RootSlots()
    {
        var perPage = PerPage();
        var page = CurrentPage();
        var pageChats = _chats.Skip(page * perPage).Take(perPage)
            .Select(c => (Slot?)new Slot(c.Label, () => Push(ChatPage(c)))).ToList();
        if (_chats.Count > N)
        {
            while (pageChats.Count < N - 1) pageChats.Add(null);
            pageChats.Add(new Slot(More, () => { _page = (CurrentPage() + 1) % PageCount(); return new ClickResult(null, true); }));
        }
        return pageChats;
    }

    private int PerPage() => _chats.Count > N ? N - 1 : N;

    private int PageCount() => Math.Max(1, (int)Math.Ceiling(_chats.Count / (double)PerPage()));

    // An out-of-range page falls back to the first one.
    private int CurrentPage() => _page >= 0 && _page < PageCount() ? _page : 0;

    // Page items in order; "back" goes into the last slot.
    private List<Slot?> PageSlots(MenuPage page)
    {
        var slots = page.Items.Take(N - 1).Select(i => (Slot?)new Slot(i.Label, () => Open(i))).ToList();
        while (slots.Count < N - 1) slots.Add(null);
        slots.Add(new Slot(Back, Pop));
        return slots;
    }

    // A submenu goes on top of the stack; a final action goes back to the caller.
    private ClickResult Open(MenuItem item) =>
        item.Submenu != null ? Push(item.Submenu) : new ClickResult(item.Action, false);

    private ClickResult Push(MenuPage page)
    {
        _pages.Push(page);
        return new ClickResult(null, true);
    }

    private ClickResult Pop()
    {
        _pages.TryPop(out _);
        return new ClickResult(null, true);
    }

    private void ToRoot()
    {
        _pages.Clear();
        _page = 0;
    }

    private MenuPage ChatPage(ChatContext c)
    {
        var items = new List<MenuItem> { new("VS Code", new MenuAction.FocusChat(c.Tab)) };
        if (c.TaskKey == null) return new MenuPage(items);
        if (c.Repos.Count > 0) items.Add(new("Rider", Submenu: RiderPage(c)));
        items.Add(new("MR", Submenu: MrPage(c)));
        if (links.JiraIssue(c.TaskKey) is { } jira) items.Add(new("Jira", new MenuAction.OpenUrl(jira)));
        if (c.Repos.Count > 0) items.Add(new("Репо", Submenu: RepoPage(c)));
        return new MenuPage(items);
    }

    private MenuPage MrPage(ChatContext c)
    {
        var items = new List<MenuItem> { new("Все MR", new MenuAction.OpenUrl(links.GroupMrs(c.TaskKey!))) };
        items.AddRange(config.MrTargets.Select(t => new MenuItem(t.Label, new MenuAction.OpenUrl(links.GroupMrs(c.TaskKey!, t.Branch)))));
        return new MenuPage(items);
    }

    // One repo per slot, then "Все"; repos that do not fit are reachable only through "Все".
    private MenuPage RiderPage(ChatContext c)
    {
        var items = c.Repos.Take(N - 2).Select(r => new MenuItem(DisplayName(r, c.TaskKey), new MenuAction.OpenRider([r]))).ToList();
        items.Add(new("Все", new MenuAction.OpenRider(c.Repos)));
        return new MenuPage(items);
    }

    // Repo folder name without the trailing "-<TaskKey>" (old flat layout).
    private static string DisplayName(RepoRef r, string? key)
    {
        var suffix = "-" + key;
        return key != null && r.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && r.Name.Length > suffix.Length
            ? r.Name[..^suffix.Length]
            : r.Name;
    }

    private MenuPage RepoPage(ChatContext c)
    {
        var items = c.Repos.Take(N - 3).Select(r => new MenuItem(DisplayName(r, c.TaskKey),
            links.RepoBranch(r) is { } url ? new MenuAction.OpenUrl(url) : new MenuAction.OpenRider([r]))).ToList();
        var urls = c.Repos.Select(links.RepoBranch).OfType<string>().ToList();
        if (urls.Count > 0) items.Add(new("Все в GitLab", new MenuAction.OpenUrls(urls)));
        items.Add(new("Папка в Finder", new MenuAction.Reveal([CommonParent(c.Repos)])));
        return new MenuPage(items);
    }

    // Deepest directory that contains the parent folders of all repos.
    private static string CommonParent(IReadOnlyList<RepoRef> repos)
    {
        var parts = repos
            .Select(r => (Path.GetDirectoryName(r.Path) ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var common = parts[0].AsEnumerable();
        foreach (var p in parts.Skip(1)) common = common.Zip(p).TakeWhile(x => x.First == x.Second).Select(x => x.First);
        return "/" + string.Join('/', common);
    }
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/ClaudeRing.Core.Tests`
Expected: `Failed:     0` (all old RingMenu tests and the 4 new ones pass).
Then the plugin compile check → no errors.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeRing.Core/MenuPage.cs src/ClaudeRing.Core/RingMenu.cs tests/ClaudeRing.Core.Tests/RingMenuTests.cs
git commit -m "Turn ring menu levels into a page stack"
```

---

### Task 7: MenuBuilder

`MenuBuilder` builds the chat page from `Settings.ChatMenu`: built-in items with their hiding rules, custom `url`/`run` items, `perRepo` submenus. It is not wired into `RingMenu` yet (Task 8 does that). `DisplayName` and `CommonParent` move (cut and paste) from `RingMenu` into `MenuBuilder`.

**Files:**
- Create: `src/ClaudeRing.Core/MenuBuilder.cs`
- Modify: `src/ClaudeRing.Core/RingMenu.cs` (helpers moved out)
- Test: `tests/ClaudeRing.Core.Tests/MenuBuilderTests.cs`

**Interfaces:**
- Consumes: `Settings`, `MenuEntry`, `IdeSettings`, `IdeType` (T2); `Template.Render` (T1); `GitHosts.Create`, `IGitHost`, `Trackers.Create`, `ITracker` (T3); `IdeLauncher.DisplayName`, `MenuAction.OpenIde`, `MenuAction.Run` (T4); `Strings` (T5); `MenuItem`, `MenuPage` (T6).
- Produces:
  - `public sealed class MenuBuilder(Settings settings, Strings strings)` (regular constructor) with:
    - `public Strings Strings { get; }`
    - `public int SlotCount { get; }` (= `settings.SlotCount`)
    - `public MenuPage ChatPage(ChatContext chat)` — never throws.
    - `internal static string DisplayName(RepoRef r, string? key)`
    - `internal static string CommonParent(IReadOnlyList<RepoRef> repos)`

Rules (from the spec, plus the decisions noted):
- `chat`: always; label `Strings.Chat`; `FocusChat(chat.Tab)`.
- `ide`: shown when `Ide.Type != None` and the chat has repos; label `IdeLauncher.DisplayName`; submenu: first `slotCount - 2` repos (`OpenIde(ide, [repo])`), then `Strings.All` (`OpenIde(ide, chat.Repos)`).
- `reviews`: shown when the chat has a task key; label `IGitHost.ReviewsLabel`; submenu: `Strings.AllReviews(label)` then `Strings.Target(t)` per `git.targets`.
- `issue`: shown when the key is set and `ITracker.IssueUrl` is not null; label `ITracker.Name`.
- `repos`: shown when the chat has repos; label `Strings.Repos`; submenu: first `slotCount - 3` repos (branch page; without a web URL: `OpenIde` when an IDE is set, otherwise `Reveal([repo.Path])`), `Strings.AllInBrowser` (only when some repo has a link), `Strings.FolderInFinder`.
- Custom `url`: `Template.Render(url, values, forUrl: true)`; custom `run`: program as is, each argument `Template.Render(arg, values, forUrl: false)`. Null render → item hidden.
- `perRepo`: submenu of the chat's repos (display names), at most `slotCount - 1`; a repo whose render fails is left out; no repos left → item hidden.
- Values: `key` = task key; `branch` = session git branch, else first repo branch; `title` = custom title, else tab title, else AI title; `sessionId`; `repos.parent` = `CommonParent` (only with repos); per repo: `repo.name` = display name, `repo.path`, `repo.webUrl`, `repo.branch`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudeRing.Core.Tests/MenuBuilderTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class MenuBuilderTests
{
    private static readonly IdeSettings Rider = new(IdeType.Rider);

    // Repo folders carry the key suffix ("repo0-A-1") to check that labels drop it.
    private static ChatContext Chat(string? key = "A-1", int repos = 2, bool webUrls = true) => new(
        new ChatTab("sess-1", key ?? "Some chat", "/w"),
        new SessionInfo(null, null, key == null ? null : $"feature/{key}", "/w"),
        key,
        Enumerable.Range(0, key == null ? 0 : repos)
            .Select(i => new RepoRef($"repo{i}-{key}", $"/p/{key}/repo{i}", $"feature/{key}", webUrls ? $"https://g/T/repo{i}" : null))
            .ToList());

    private static Settings Config(params MenuEntry[] menu) => new()
    {
        SlotCount = 8,
        Git = new GitSettings(GitHostType.GitLab, "https://gitlab.example.com", "acme", ["develop", "main"]),
        Tracker = new TrackerSettings(TrackerType.Jira, "https://j"),
        Ide = Rider,
        ChatMenu = menu.Length == 0 ? new Settings().ChatMenu : menu,
    };

    private static MenuPage Page(Settings settings, ChatContext chat) => new MenuBuilder(settings, Strings.En).ChatPage(chat);

    private static string[] Labels(MenuPage page) => page.Items.Select(i => i.Label).ToArray();

    private static MenuItem Find(MenuPage page, string label) => page.Items.Single(i => i.Label == label);

    private static MenuEntry Id(string id) => new MenuEntry.BuiltIn(id);

    private static MenuEntry UrlItem(string label, string url, bool perRepo = false) => new MenuEntry.Custom(label, url, null, perRepo);

    private static MenuEntry RunItem(string label, bool perRepo, params string[] run) => new MenuEntry.Custom(label, null, run, perRepo);

    [Fact]
    public void Default_menu_in_english() =>
        Assert.Equal(["VS Code", "Rider", "MR", "Jira", "Repos"], Labels(Page(Config(), Chat())));

    [Fact]
    public void Default_menu_in_russian() =>
        Assert.Equal(["VS Code", "Rider", "MR", "Jira", "Репо"], Labels(new MenuBuilder(Config(), Strings.Ru).ChatPage(Chat())));

    [Fact]
    public void Items_follow_the_chat_menu_order() =>
        Assert.Equal(["Repos", "VS Code"], Labels(Page(Config(Id("repos"), Id("chat")), Chat())));

    [Fact]
    public void Chat_item_focuses_the_chat()
    {
        var chat = Chat();
        Assert.Equal(new MenuAction.FocusChat(chat.Tab), Find(Page(Config(), chat), "VS Code").Action);
    }

    [Fact]
    public void Chat_without_key_shows_only_the_chat_item() =>
        Assert.Equal(["VS Code"], Labels(Page(Config(), Chat(key: null))));

    [Fact]
    public void Ide_item_is_hidden_without_ide() =>
        Assert.DoesNotContain("Rider", Labels(Page(Config() with { Ide = new IdeSettings(IdeType.None) }, Chat())));

    [Fact]
    public void Ide_and_repos_are_hidden_without_repos() =>
        Assert.Equal(["VS Code", "MR", "Jira"], Labels(Page(Config(), Chat(repos: 0))));

    [Fact]
    public void Ide_item_uses_the_ide_name() =>
        Assert.Contains("Cursor", Labels(Page(Config() with { Ide = new IdeSettings(IdeType.Cursor) }, Chat())));

    [Fact]
    public void Ide_submenu_lists_repos_then_all()
    {
        var chat = Chat(repos: 10);
        var ide = Find(Page(Config(), chat), "Rider").Submenu!;
        Assert.Equal(["repo0", "repo1", "repo2", "repo3", "repo4", "repo5", "All"], Labels(ide));
        Assert.Equal([chat.Repos[0]], ((MenuAction.OpenIde)ide.Items[0].Action!).Repos);
        Assert.Equal(new MenuAction.OpenIde(Rider, chat.Repos), ide.Items[^1].Action);
    }

    [Fact]
    public void Gitlab_reviews_submenu()
    {
        var reviews = Find(Page(Config(), Chat()), "MR").Submenu!;
        Assert.Equal(["All MRs", "→ develop", "→ main"], Labels(reviews));
        Assert.Equal(
            new MenuAction.OpenUrl("https://gitlab.example.com/groups/acme/-/merge_requests?scope=all&state=all&search=A-1&target_branch=main"),
            reviews.Items[2].Action);
    }

    [Fact]
    public void Github_reviews_submenu()
    {
        var settings = Config() with { Git = new GitSettings(GitHostType.GitHub, "https://github.com", "acme", ["main"]) };
        var reviews = Find(Page(settings, Chat()), "PR").Submenu!;
        Assert.Equal(["All PRs", "→ main"], Labels(reviews));
        Assert.Equal(new MenuAction.OpenUrl("https://github.com/pulls?q=is:pr+A-1+org:acme"), reviews.Items[0].Action);
    }

    [Fact]
    public void Issue_item_uses_the_tracker()
    {
        var settings = Config() with { Tracker = new TrackerSettings(TrackerType.Linear, Workspace: "acme") };
        Assert.Equal(new MenuAction.OpenUrl("https://linear.app/acme/issue/A-1"), Find(Page(settings, Chat()), "Linear").Action);
    }

    [Fact]
    public void Issue_item_is_hidden_when_the_tracker_cannot_link()
    {
        Assert.DoesNotContain("Jira", Labels(Page(Config() with { Tracker = new TrackerSettings(TrackerType.Jira) }, Chat())));
        Assert.Equal(["VS Code", "Rider", "MR", "Repos"], Labels(Page(Config() with { Tracker = new TrackerSettings(TrackerType.None) }, Chat())));
    }

    [Fact]
    public void Repos_submenu_lists_repos_then_browser_and_finder()
    {
        var repos = Find(Page(Config(), Chat(repos: 10)), "Repos").Submenu!;
        Assert.Equal(["repo0", "repo1", "repo2", "repo3", "repo4", "All in browser", "Folder in Finder"], Labels(repos));
        Assert.Equal(new MenuAction.OpenUrl("https://g/T/repo1/-/tree/feature/A-1"), repos.Items[1].Action);
        Assert.Equal(10, ((MenuAction.OpenUrls)repos.Items[5].Action!).Urls.Count);
        Assert.Equal(["/p/A-1"], ((MenuAction.Reveal)repos.Items[6].Action!).Paths);
    }

    [Fact]
    public void Repo_without_web_url_opens_in_ide_or_finder()
    {
        var chat = Chat(webUrls: false);
        var withIde = Find(Page(Config(), chat), "Repos").Submenu!;
        Assert.Equal(["repo0", "repo1", "Folder in Finder"], Labels(withIde));
        Assert.Equal([chat.Repos[0]], ((MenuAction.OpenIde)withIde.Items[0].Action!).Repos);

        var noIde = Find(Page(Config() with { Ide = new IdeSettings(IdeType.None) }, chat), "Repos").Submenu!;
        Assert.Equal(["/p/A-1/repo0"], ((MenuAction.Reveal)noIde.Items[0].Action!).Paths);
    }

    [Fact]
    public void Custom_url_item_renders_the_template() =>
        Assert.Equal(new MenuAction.OpenUrl("https://sentry.io/issues/?query=A-1"),
            Find(Page(Config(Id("chat"), UrlItem("Sentry", "https://sentry.io/issues/?query={key}")), Chat()), "Sentry").Action);

    [Fact]
    public void Custom_item_is_hidden_when_a_value_is_missing() =>
        Assert.Equal(["VS Code"], Labels(Page(Config(Id("chat"), UrlItem("Sentry", "https://sentry.io/issues/?query={key}")), Chat(key: null))));

    [Fact]
    public void Custom_run_item_passes_arguments_as_is()
    {
        var run = (MenuAction.Run)Find(Page(Config(RunItem("Finder", false, "open", "-R", "{repos.parent}")), Chat()), "Finder").Action!;
        Assert.Equal("open", run.File);
        Assert.Equal(["-R", "/p/A-1"], run.Args);
    }

    [Fact]
    public void Per_repo_item_opens_a_repo_submenu()
    {
        var submenu = Find(Page(Config(RunItem("Terminal", true, "open", "-a", "Terminal", "{repo.path}")), Chat()), "Terminal").Submenu!;
        Assert.Equal(["repo0", "repo1"], Labels(submenu));
        var run = (MenuAction.Run)submenu.Items[1].Action!;
        Assert.Equal("open", run.File);
        Assert.Equal(["-a", "Terminal", "/p/A-1/repo1"], run.Args);
    }

    [Fact]
    public void Per_repo_item_leaves_out_repos_without_values()
    {
        var chat = Chat() with
        {
            Repos = [new RepoRef("api", "/p/api", "feature/A-1", "https://g/T/api"), new RepoRef("local", "/p/local", "feature/A-1")],
        };
        var submenu = Find(Page(Config(UrlItem("CI", "{repo.webUrl}/-/pipelines", perRepo: true)), chat), "CI").Submenu!;
        Assert.Equal(["api"], Labels(submenu));
        Assert.Equal(new MenuAction.OpenUrl("https://g/T/api/-/pipelines"), submenu.Items[0].Action);
    }

    [Fact]
    public void Per_repo_item_is_hidden_without_repos() =>
        Assert.Empty(Labels(Page(Config(UrlItem("CI", "{repo.webUrl}/-/pipelines", perRepo: true)), Chat(repos: 0))));

    [Fact]
    public void Per_repo_submenu_fits_the_slots() =>
        Assert.Equal(7, Find(Page(Config(RunItem("T", true, "open", "{repo.path}")), Chat(repos: 10)), "T").Submenu!.Items.Count);
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.MenuBuilderTests"`
Expected: build FAIL with `error CS0246: The type or namespace name 'MenuBuilder' could not be found`.

- [ ] **Step 3: Implement `MenuBuilder`**

Create `src/ClaudeRing.Core/MenuBuilder.cs` (the two helpers at the end are cut from `RingMenu.cs` in Step 4):

```csharp
namespace ClaudeRing.Core;

// Builds the chat page and its submenus from settings.
// Items whose data is missing are left out, so a page never shows dead slots.
public sealed class MenuBuilder
{
    private readonly Settings _settings;
    private readonly IGitHost _git;
    private readonly ITracker _tracker;

    public MenuBuilder(Settings settings, Strings strings)
    {
        _settings = settings;
        Strings = strings;
        _git = GitHosts.Create(settings.Git);
        _tracker = Trackers.Create(settings.Tracker);
    }

    public Strings Strings { get; }

    public int SlotCount => _settings.SlotCount;

    private int N => _settings.SlotCount;

    public MenuPage ChatPage(ChatContext chat)
    {
        var values = ChatValues(chat);
        var items = new List<MenuItem>();
        foreach (var entry in _settings.ChatMenu)
        {
            var item = entry switch
            {
                MenuEntry.BuiltIn b => BuiltIn(b.Id, chat),
                MenuEntry.Custom c => Custom(c, chat, values),
                _ => null,
            };
            if (item != null) items.Add(item);
        }
        return new MenuPage(items);
    }

    private MenuItem? BuiltIn(string id, ChatContext c) => id switch
    {
        "chat" => new MenuItem(Strings.Chat, new MenuAction.FocusChat(c.Tab)),
        "ide" => IdeItem(c),
        "reviews" => ReviewsItem(c),
        "issue" => IssueItem(c),
        "repos" => ReposItem(c),
        _ => null,
    };

    // One repo per slot, then "All"; repos that do not fit are reachable only through "All".
    private MenuItem? IdeItem(ChatContext c)
    {
        if (_settings.Ide.Type == IdeType.None || c.Repos.Count == 0) return null;
        var items = c.Repos.Take(N - 2)
            .Select(r => new MenuItem(DisplayName(r, c.TaskKey), new MenuAction.OpenIde(_settings.Ide, [r])))
            .ToList();
        items.Add(new MenuItem(Strings.All, new MenuAction.OpenIde(_settings.Ide, c.Repos)));
        return new MenuItem(IdeLauncher.DisplayName(_settings.Ide), Submenu: new MenuPage(items));
    }

    private MenuItem? ReviewsItem(ChatContext c)
    {
        if (c.TaskKey is not { } key) return null;
        var items = new List<MenuItem>
        {
            new(Strings.AllReviews(_git.ReviewsLabel), new MenuAction.OpenUrl(_git.ReviewsUrl(key, null))),
        };
        items.AddRange(_settings.Git.Targets.Select(t => new MenuItem(Strings.Target(t), new MenuAction.OpenUrl(_git.ReviewsUrl(key, t)))));
        return new MenuItem(_git.ReviewsLabel, Submenu: new MenuPage(items));
    }

    private MenuItem? IssueItem(ChatContext c) =>
        c.TaskKey is { } key && _tracker.IssueUrl(key, c.Repos) is { } url
            ? new MenuItem(_tracker.Name, new MenuAction.OpenUrl(url))
            : null;

    private MenuItem? ReposItem(ChatContext c)
    {
        if (c.Repos.Count == 0) return null;
        var items = c.Repos.Take(N - 3).Select(r => new MenuItem(DisplayName(r, c.TaskKey), RepoAction(r))).ToList();
        var urls = c.Repos.Select(_git.BranchUrl).OfType<string>().ToList();
        if (urls.Count > 0) items.Add(new MenuItem(Strings.AllInBrowser, new MenuAction.OpenUrls(urls)));
        items.Add(new MenuItem(Strings.FolderInFinder, new MenuAction.Reveal([CommonParent(c.Repos)])));
        return new MenuItem(Strings.Repos, Submenu: new MenuPage(items));
    }

    // Branch page in the browser. Without a web URL: the IDE, or Finder when no IDE is set.
    private MenuAction RepoAction(RepoRef r)
    {
        if (_git.BranchUrl(r) is { } url) return new MenuAction.OpenUrl(url);
        if (_settings.Ide.Type != IdeType.None) return new MenuAction.OpenIde(_settings.Ide, [r]);
        return new MenuAction.Reveal([r.Path]);
    }

    // A perRepo item opens a submenu of repos; repos without the needed values are left out.
    private MenuItem? Custom(MenuEntry.Custom item, ChatContext c, Dictionary<string, string?> chatValues)
    {
        if (!item.PerRepo)
            return CustomAction(item, chatValues) is { } action ? new MenuItem(item.Label, action) : null;

        var items = new List<MenuItem>();
        foreach (var r in c.Repos)
        {
            if (items.Count == N - 1) break;
            if (CustomAction(item, RepoValues(chatValues, r, c.TaskKey)) is { } action)
                items.Add(new MenuItem(DisplayName(r, c.TaskKey), action));
        }
        return items.Count == 0 ? null : new MenuItem(item.Label, Submenu: new MenuPage(items));
    }

    // Null when a placeholder has no value.
    private static MenuAction? CustomAction(MenuEntry.Custom item, IReadOnlyDictionary<string, string?> values)
    {
        if (item.Url != null)
            return Template.Render(item.Url, values, forUrl: true) is { } url ? new MenuAction.OpenUrl(url) : null;
        if (item.Run is not { Count: > 0 } run) return null;
        var args = new List<string>();
        foreach (var arg in run.Skip(1))
        {
            if (Template.Render(arg, values, forUrl: false) is not { } value) return null;
            args.Add(value);
        }
        return new MenuAction.Run(run[0], args);
    }

    private static Dictionary<string, string?> ChatValues(ChatContext c) => new()
    {
        ["key"] = c.TaskKey,
        ["branch"] = c.Session.GitBranch ?? c.Repos.FirstOrDefault()?.Branch,
        ["title"] = c.Session.CustomTitle ?? (c.Tab.Title.Length > 0 ? c.Tab.Title : c.Session.AiTitle),
        ["sessionId"] = c.Tab.SessionId,
        ["repos.parent"] = c.Repos.Count > 0 ? CommonParent(c.Repos) : null,
    };

    private static Dictionary<string, string?> RepoValues(Dictionary<string, string?> chat, RepoRef r, string? key) =>
        new(chat)
        {
            ["repo.name"] = DisplayName(r, key),
            ["repo.path"] = r.Path,
            ["repo.webUrl"] = r.WebUrl,
            ["repo.branch"] = r.Branch,
        };

    // Repo folder name without the trailing "-<TaskKey>" (old flat layout).
    internal static string DisplayName(RepoRef r, string? key)
    {
        var suffix = "-" + key;
        return key != null && r.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && r.Name.Length > suffix.Length
            ? r.Name[..^suffix.Length]
            : r.Name;
    }

    // Deepest directory that contains the parent folders of all repos.
    internal static string CommonParent(IReadOnlyList<RepoRef> repos)
    {
        var parts = repos
            .Select(r => (Path.GetDirectoryName(r.Path) ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var common = parts[0].AsEnumerable();
        foreach (var p in parts.Skip(1)) common = common.Zip(p).TakeWhile(x => x.First == x.Second).Select(x => x.First);
        return "/" + string.Join('/', common);
    }
}
```

- [ ] **Step 4: Remove the moved helpers from `RingMenu`**

In `src/ClaudeRing.Core/RingMenu.cs`:
1. Delete the method `DisplayName` together with its comment (`// Repo folder name without the trailing "-<TaskKey>" (old flat layout).` ... closing `}`).
2. Delete the method `CommonParent` together with its comment (`// Deepest directory that contains the parent folders of all repos.` ... closing `}`).
3. In `RiderPage` and `RepoPage`, replace `DisplayName(r, c.TaskKey)` with `MenuBuilder.DisplayName(r, c.TaskKey)` (2 places) and `CommonParent(c.Repos)` with `MenuBuilder.CommonParent(c.Repos)` (1 place). The two methods then read:

```csharp
    // One repo per slot, then "Все"; repos that do not fit are reachable only through "Все".
    private MenuPage RiderPage(ChatContext c)
    {
        var items = c.Repos.Take(N - 2).Select(r => new MenuItem(MenuBuilder.DisplayName(r, c.TaskKey), new MenuAction.OpenRider([r]))).ToList();
        items.Add(new("Все", new MenuAction.OpenRider(c.Repos)));
        return new MenuPage(items);
    }

    private MenuPage RepoPage(ChatContext c)
    {
        var items = c.Repos.Take(N - 3).Select(r => new MenuItem(MenuBuilder.DisplayName(r, c.TaskKey),
            links.RepoBranch(r) is { } url ? new MenuAction.OpenUrl(url) : new MenuAction.OpenRider([r]))).ToList();
        var urls = c.Repos.Select(links.RepoBranch).OfType<string>().ToList();
        if (urls.Count > 0) items.Add(new("Все в GitLab", new MenuAction.OpenUrls(urls)));
        items.Add(new("Папка в Finder", new MenuAction.Reveal([MenuBuilder.CommonParent(c.Repos)])));
        return new MenuPage(items);
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.MenuBuilderTests"`
Expected: `Failed:     0`.

Then all Core tests: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.
Then the plugin compile check → no errors.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeRing.Core/MenuBuilder.cs src/ClaudeRing.Core/RingMenu.cs tests/ClaudeRing.Core.Tests/MenuBuilderTests.cs
git commit -m "Add MenuBuilder for built-in and custom chat menu items"
```

---

### Task 8: Switch RingMenu and the plugin to Settings and MenuBuilder

`RingMenu` takes a `MenuBuilder`; the plugin loads `Settings` with `SettingsLoader` (errors and warnings to the log, defaults when the config has errors) and runs the new actions. After this task nothing uses `RingConfig`, `MrTarget`, `Links`, `MenuAction.OpenRider` or `Launcher.OpenInRider`, so they are removed here. No hot reload yet (Task 10).

**Files:**
- Modify: `src/ClaudeRing.Core/RingMenu.cs` (whole file)
- Modify: `src/ClaudeRing.Core/Models.cs` (remove `MrTarget`, `RingConfig`)
- Modify: `src/ClaudeRing.Core/Launcher.cs` (whole file)
- Modify: `src/ClaudeRing.Core/ChatSource.cs` (`CreateDefault`)
- Delete: `src/ClaudeRing.Core/Links.cs`, `tests/ClaudeRing.Core.Tests/LinksTests.cs`, `tests/ClaudeRing.Core.Tests/ConfigTests.cs`
- Modify: `tests/ClaudeRing.Core.Tests/RingMenuTests.cs`, `tests/ClaudeRing.Core.Tests/LauncherTests.cs`
- Modify: `ClaudeRingPlugin/src/ClaudeRingPlugin.cs` (whole file), `ClaudeRingPlugin/src/Actions/SlotCommand.cs`

**Interfaces:**
- Consumes: everything from Tasks 2-7.
- Produces:
  - `public sealed class RingMenu(IChatSource source, MenuBuilder builder)` with `public int SlotCount`, `Reset()`, `Render()`, `Click(int)`, `LastLoadError`.
  - `MenuAction` = `FocusChat`, `OpenIde`, `OpenUrl`, `OpenUrls`, `Reveal`, `Run` (no `OpenRider`).
  - `public sealed class Launcher(IProcessRunner runner, Action<int> sleep)` with `FocusChat`, `OpenIde`, `OpenUrl`, `OpenUrls`, `Reveal`, `Run`.
  - `ChatSource.CreateDefault(Settings settings, IProcessRunner runner)`.
  - Plugin: `internal static RingMenu Menu`, `internal static Settings CurrentSettings`, `internal static event Action SlotsChanged`, `internal static void Refresh()`, `internal static void Execute(MenuAction action)`, `private static Settings ReadSettings()` (null on errors), `private static RingMenu CreateMenu(Settings settings)`.

- [ ] **Step 1: Migrate the tests (they stop compiling)**

In `tests/ClaudeRing.Core.Tests/RingMenuTests.cs`:

1. Replace the block from `private static readonly RingConfig Config = new()` through the end of the `Menu(params ChatContext[] chats)` method (old lines 18-31) with:

```csharp
    private static readonly IdeSettings Rider = new(IdeType.Rider);

    // Russian labels, like the ring had before settings existed.
    private static Settings Config(int n = 8, bool jira = true) => new()
    {
        Language = UiLanguage.Ru,
        SlotCount = n,
        Git = new GitSettings(GitHostType.GitLab, "https://gitlab.example.com", "acme", ["Dev", "RC", "master"]),
        Tracker = jira ? new TrackerSettings(TrackerType.Jira, "https://j") : new TrackerSettings(TrackerType.None),
        Ide = Rider,
    };

    private static RingMenu Create(IChatSource source, Settings settings) => new(source, new MenuBuilder(settings, Strings.Ru));

    private static RingMenu Menu(params ChatContext[] chats)
    {
        var menu = Create(new Source(chats), Config());
        menu.Reset();
        return menu;
    }
```

2. Replace the whole `MenuN` method with:

```csharp
    private static RingMenu MenuN(int n, bool jira, params ChatContext[] chats)
    {
        var menu = Create(new Source(chats), Config(n, jira));
        menu.Reset();
        return menu;
    }
```

3. In `Chat_level_then_rider_all_action_resets_to_root`, replace
`Assert.Equal(new MenuAction.OpenRider(chat.Repos), r.Action);` with
`Assert.Equal(new MenuAction.OpenIde(Rider, chat.Repos), r.Action);`

4. In `Rider_level_lists_repos_then_all_then_back`, replace both `(MenuAction.OpenRider)` casts with `(MenuAction.OpenIde)`.

5. In `Repo_click_falls_back_to_rider_without_web_url`, replace
`Assert.Equal(new MenuAction.OpenRider([repo]).Repos, ((MenuAction.OpenRider)menu.Click(0).Action!).Repos);` with
`Assert.Equal([repo], ((MenuAction.OpenIde)menu.Click(0).Action!).Repos);`

6. In `Load_failure_keeps_previous_chats_and_sets_error`, replace
`var menu = new RingMenu(new FlakySource(chats), new Links(Config), Config);` with
`var menu = Create(new FlakySource(chats), Config());`

7. In `Final_action_returns_to_root_without_reloading`, replace
`var menu = new RingMenu(source, new Links(Config), Config);` with
`var menu = Create(source, Config());`

8. The "all in browser" label changes: run
`sed -i '' 's/Все в GitLab/Все в браузере/g' tests/ClaudeRing.Core.Tests/RingMenuTests.cs`
(4 places: `Nine_slots_repos_level_shows_six_repos_then_gitlab_and_finder`, `All_in_gitlab_opens_every_repo_link`, `All_in_gitlab_hidden_without_links`, `Back_from_repos_returns_to_chat`).

9. Add one test at the end of the class:

```csharp
    [Fact]
    public void Slot_count_comes_from_settings() => Assert.Equal(9, MenuN(9, true, Chat("A-1")).SlotCount);
```

In `tests/ClaudeRing.Core.Tests/LauncherTests.cs`:
1. Replace `private Launcher Launcher() => new(_runner, new RingConfig(), _ => { });` with `private Launcher Launcher() => new(_runner, _ => { });`
2. Delete the test `Rider_gets_all_paths_in_one_call` (the IDE commands are covered by `IdeLauncherTests` and `Open_ide_starts_every_command`).

Delete the replaced tests:

```bash
git rm tests/ClaudeRing.Core.Tests/LinksTests.cs tests/ClaudeRing.Core.Tests/ConfigTests.cs
```

(`LinksTests` is covered by `GitHostTests`/`TrackerTests`, `ConfigTests` by `SettingsLoaderTests`.)

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests`
Expected: build FAIL with `error CS1503` / `CS1729` on `new RingMenu(...)` and `new Launcher(...)` (constructor arguments do not match).

- [ ] **Step 3: Rewrite `RingMenu` on top of `MenuBuilder`**

Replace the whole content of `src/ClaudeRing.Core/RingMenu.cs`:

```csharp
namespace ClaudeRing.Core;

public abstract record MenuAction
{
    public sealed record FocusChat(ChatTab Tab) : MenuAction;
    public sealed record OpenIde(IdeSettings Ide, IReadOnlyList<RepoRef> Repos) : MenuAction;
    public sealed record OpenUrl(string Url) : MenuAction;
    public sealed record OpenUrls(IReadOnlyList<string> Urls) : MenuAction;
    public sealed record Reveal(IReadOnlyList<string> Paths) : MenuAction;
    // Starts a program directly, without a shell.
    public sealed record Run(string File, IReadOnlyList<string> Args) : MenuAction;
}

public sealed record ClickResult(MenuAction? Action, bool Reopen);

public sealed record SlotView(string Label, bool Enabled);

public sealed class RingMenu(IChatSource source, MenuBuilder builder)
{
    // One ring slot: what it shows and what a click does.
    private sealed record Slot(string Label, Func<ClickResult> OnClick);

    private readonly object _lock = new();
    // Pages opened from the root; the top one is shown. Empty means the root page.
    private readonly Stack<MenuPage> _pages = new();
    private IReadOnlyList<ChatContext> _chats = [];
    private int _page;

    public int SlotCount => builder.SlotCount;

    private int N => builder.SlotCount;

    // The plugin logs this after Reset. Null after a successful load.
    public Exception? LastLoadError { get; private set; }

    // Never throws. Loads outside the lock; if loading fails, the previous chat list stays.
    public void Reset()
    {
        IReadOnlyList<ChatContext>? loaded = null;
        Exception? error = null;
        try
        {
            loaded = source.Load();
        }
        catch (Exception e)
        {
            error = e;
        }

        lock (_lock)
        {
            if (loaded != null) _chats = loaded;
            LastLoadError = error;
            ToRoot();
        }
    }

    public IReadOnlyList<SlotView> Render()
    {
        lock (_lock)
        {
            var slots = Slots();
            return Enumerable.Range(0, N)
                .Select(i => i < slots.Count && slots[i] != null ? new SlotView(slots[i]!.Label, true) : new SlotView("", false))
                .ToList();
        }
    }

    // A final action switches to the root page at once, under the lock. It does not reload chats:
    // the caller reloads chats after running the action, so the action never waits for the reload.
    public ClickResult Click(int slot)
    {
        ClickResult result;
        lock (_lock)
        {
            var slots = Slots();
            if (slot < 0 || slot >= slots.Count || slots[slot] == null) return new ClickResult(null, false);

            result = slots[slot]!.OnClick();
            if (!result.Reopen) ToRoot();
        }
        return result;
    }

    // Slots of the shown page. Null means an empty slot.
    private List<Slot?> Slots() => _pages.Count == 0 ? RootSlots() : PageSlots(_pages.Peek());

    private List<Slot?> RootSlots()
    {
        var perPage = PerPage();
        var page = CurrentPage();
        var pageChats = _chats.Skip(page * perPage).Take(perPage)
            .Select(c => (Slot?)new Slot(c.Label, () => Push(builder.ChatPage(c)))).ToList();
        if (_chats.Count > N)
        {
            while (pageChats.Count < N - 1) pageChats.Add(null);
            pageChats.Add(new Slot(builder.Strings.More, () => { _page = (CurrentPage() + 1) % PageCount(); return new ClickResult(null, true); }));
        }
        return pageChats;
    }

    private int PerPage() => _chats.Count > N ? N - 1 : N;

    private int PageCount() => Math.Max(1, (int)Math.Ceiling(_chats.Count / (double)PerPage()));

    // An out-of-range page falls back to the first one.
    private int CurrentPage() => _page >= 0 && _page < PageCount() ? _page : 0;

    // Page items in order; "back" goes into the last slot.
    private List<Slot?> PageSlots(MenuPage page)
    {
        var slots = page.Items.Take(N - 1).Select(i => (Slot?)new Slot(i.Label, () => Open(i))).ToList();
        while (slots.Count < N - 1) slots.Add(null);
        slots.Add(new Slot(builder.Strings.Back, Pop));
        return slots;
    }

    // A submenu goes on top of the stack; a final action goes back to the caller.
    private ClickResult Open(MenuItem item) =>
        item.Submenu != null ? Push(item.Submenu) : new ClickResult(item.Action, false);

    private ClickResult Push(MenuPage page)
    {
        _pages.Push(page);
        return new ClickResult(null, true);
    }

    private ClickResult Pop()
    {
        _pages.TryPop(out _);
        return new ClickResult(null, true);
    }

    private void ToRoot()
    {
        _pages.Clear();
        _page = 0;
    }
}
```

- [ ] **Step 4: Remove old Core types and update `Launcher` and `ChatSource`**

Delete `Links.cs`:

```bash
git rm src/ClaudeRing.Core/Links.cs
```

In `src/ClaudeRing.Core/Models.cs`: delete the line `using System.Text.Json;`, the record `MrTarget` and the whole record `RingConfig` (old lines 3 and 17-41). The file then contains only `ChatTab`, `SessionInfo`, `RepoRef`, `ChatContext`.

Replace the whole content of `src/ClaudeRing.Core/Launcher.cs`:

```csharp
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
        foreach (var command in IdeLauncher.Commands(ide, repos.Select(r => r.Path).ToList())) Run(command.File, command.Args);
    }

    // Start errors go to the caller; their message names the program path.
    public void Run(string file, IReadOnlyList<string> args) => runner.Start(file, [.. args]);
}
```

In `src/ClaudeRing.Core/ChatSource.cs`, replace `CreateDefault`:

```csharp
    public static ChatSource CreateDefault(Settings settings, IProcessRunner runner)
    {
        var home = Settings.Home;
        return new ChatSource(
            new OpenChatsReader(Path.Combine(home, "Library", "Application Support", "Code", "User"),
                Path.Combine(home, ".claude", "ide"), runner, OpenChatsReader.IsPidAlive),
            new SessionInfoReader(Path.Combine(home, ".claude", "projects")),
            new RepoFinder(settings.RepoRoots));
    }
```

- [ ] **Step 5: Run the Core tests**

Run: `dotnet test tests/ClaudeRing.Core.Tests`
Expected: `Failed:     0`.

Check that nothing refers to the removed types:
Run: `grep -rn "RingConfig\|MrTarget\|new Links\|OpenRider\|OpenInRider" src tests ClaudeRingPlugin/src --include='*.cs'`
Expected: matches only under `ClaudeRingPlugin/src/` (fixed in Step 6).

- [ ] **Step 6: Update the plugin**

Replace the whole content of `ClaudeRingPlugin/src/ClaudeRingPlugin.cs`:

```csharp
namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.Linq;
    using System.Threading;
    using ClaudeRing.Core;

    public class ClaudeRingPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        internal static RingMenu Menu { get; private set; }

        internal static Settings CurrentSettings { get; private set; }

        internal static event Action SlotsChanged;

        private static ProcessRunner _runner;

        private static Launcher _launcher;

        public ClaudeRingPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            try
            {
                _runner = new ProcessRunner();
                _launcher = new Launcher(_runner, Thread.Sleep);
                var settings = ReadSettings();
                if (settings == null)
                {
                    PluginLog.Warning("Конфиг с ошибками, беру значения по умолчанию");
                    settings = new Settings();
                }
                CurrentSettings = settings;
                Menu = CreateMenu(settings);
                Refresh();

                var labels = Menu.Render().Select(s => s.Label).Where(l => !String.IsNullOrEmpty(l)).ToList();
                PluginLog.Info($"Открытых чатов: {labels.Count}: {String.Join(", ", labels)}");

                RingWatcher.Start(settings.ResetOnRingEvent, onClosedByUser: Refresh);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Плагин не загрузился");
            }
        }

        public override void Unload()
        {
            try { RingWatcher.Stop(); }
            catch (Exception ex) { PluginLog.Error(ex, "Не удалось отписаться от событий кольца"); }

            // Statics may survive a plugin reload, so drop old references.
            SlotsChanged = null;
            Menu = null;
        }

        // Reads open chats again and shows the root level.
        internal static void Refresh()
        {
            try
            {
                Menu.Reset();
                if (Menu.LastLoadError != null)
                {
                    PluginLog.Error(Menu.LastLoadError, "Не удалось прочитать открытые чаты");
                }
                SlotsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось обновить слоты");
            }
        }

        internal static void Execute(MenuAction action)
        {
            try
            {
                switch (action)
                {
                    case MenuAction.FocusChat a: _launcher.FocusChat(a.Tab); break;
                    case MenuAction.OpenIde a: _launcher.OpenIde(a.Ide, a.Repos); break;
                    case MenuAction.OpenUrl a: _launcher.OpenUrl(a.Url); break;
                    case MenuAction.OpenUrls a: _launcher.OpenUrls(a.Urls); break;
                    case MenuAction.Reveal a: _launcher.Reveal(a.Paths); break;
                    case MenuAction.Run a: _launcher.Run(a.File, a.Args); break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Действие не выполнилось: {Describe(action)}");
            }
        }

        // Records print lists as type names, so show programs and paths in full.
        private static String Describe(MenuAction action) => action switch
        {
            MenuAction.Run a => $"{a.File} {String.Join(" ", a.Args)}",
            MenuAction.OpenIde a => $"{IdeLauncher.DisplayName(a.Ide)}: {String.Join(" ", a.Repos.Select(r => r.Path))}",
            _ => action.ToString(),
        };

        // Writes warnings and errors to the log. Null when the config has errors.
        private static Settings ReadSettings()
        {
            var result = SettingsLoader.Load(Settings.DefaultPath);
            foreach (var warning in result.Warnings)
            {
                PluginLog.Warning($"Конфиг: {warning}");
            }
            foreach (var error in result.Errors)
            {
                PluginLog.Error($"Ошибка в конфиге: {error}");
            }
            return result.Settings;
        }

        private static RingMenu CreateMenu(Settings settings)
        {
            var strings = Strings.For(settings.Language, () => SystemLanguage.Read(_runner));
            return new RingMenu(ChatSource.CreateDefault(settings, _runner), new MenuBuilder(settings, strings));
        }
    }
}
```

In `ClaudeRingPlugin/src/Actions/SlotCommand.cs`:

1. In `GetCommandDisplayName`, replace
```csharp
                return i < ClaudeRingPlugin.Config.SlotCount ? String.IsNullOrEmpty(slots[i].Label) ? " " : slots[i].Label : " ";
```
with
```csharp
                return String.IsNullOrEmpty(slots[i].Label) ? " " : slots[i].Label;
```
(`Render()` returns exactly `SlotCount` items and `i < slots.Count` is checked above.)

2. Replace the body of the `try` block in `RunCommand` with:
```csharp
                var menu = ClaudeRingPlugin.Menu;
                if (menu == null || !Int32.TryParse(actionParameter, out var i) || i < 0 || i >= menu.SlotCount)
                {
                    return;
                }

                var result = menu.Click(i);
                this.ActionImageChanged();
                if (result.Action != null)
                {
                    var action = result.Action;
                    // Run the action first, then reload chats (logs load errors, raises SlotsChanged).
                    Task.Run(() =>
                    {
                        ClaudeRingPlugin.Execute(action);
                        ClaudeRingPlugin.Refresh();
                    });
                }
                var reopenDelayMs = ClaudeRingPlugin.CurrentSettings.ReopenDelayMs;
                if (result.Reopen && reopenDelayMs >= 0)
                {
                    // The ring closes after this click; do not treat that close as "user closed it".
                    RingWatcher.ExpectPluginReopen();
                    RingReopener.ShowAfter(reopenDelayMs);
                }
```

- [ ] **Step 7: Check everything**

Run: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.
Run the plugin compile check → no errors.
Run: `grep -rn "RingConfig\|MrTarget\|OpenRider\|OpenInRider\|new Links" src tests ClaudeRingPlugin/src --include='*.cs'` → no output.

- [ ] **Step 8: Commit**

```bash
git add src/ClaudeRing.Core/RingMenu.cs src/ClaudeRing.Core/Models.cs src/ClaudeRing.Core/Launcher.cs src/ClaudeRing.Core/ChatSource.cs \
  tests/ClaudeRing.Core.Tests/RingMenuTests.cs tests/ClaudeRing.Core.Tests/LauncherTests.cs \
  ClaudeRingPlugin/src/ClaudeRingPlugin.cs ClaudeRingPlugin/src/Actions/SlotCommand.cs
git commit -m "Build the ring menu from settings and remove the old config"
```

(The `git rm` calls in Steps 1 and 4 already staged the deleted files.)

---

### Task 9: Configurable task key pattern

`TaskKey` becomes a class built from `Settings.TaskKeyPattern`. With a capture group, the key is group 1; otherwise the whole match. `ChatSource` gets a `TaskKey` instance.

**Files:**
- Modify: `src/ClaudeRing.Core/TaskKey.cs` (whole file)
- Modify: `src/ClaudeRing.Core/ChatSource.cs`
- Test: `tests/ClaudeRing.Core.Tests/TaskKeyTests.cs` (whole file)

**Interfaces:**
- Consumes: `Settings.DefaultTaskKeyPattern`, `Settings.TaskKeyPattern` (Task 2); `ChatSource.CreateDefault(Settings, IProcessRunner)` (Task 8).
- Produces:
  - `public sealed class TaskKey(string pattern)` — throws `ArgumentException` for an invalid pattern (`SettingsLoader` rejects such patterns first); `public string? Find(params string?[]? sources)` — never throws (a regex timeout of 200 ms counts as no match).
  - `public sealed class ChatSource(OpenChatsReader tabs, SessionInfoReader sessions, RepoFinder repos, TaskKey taskKey)`.

- [ ] **Step 1: Write the failing tests**

Replace the whole content of `tests/ClaudeRing.Core.Tests/TaskKeyTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TaskKeyTests
{
    private static readonly TaskKey Default = new(Settings.DefaultTaskKeyPattern);

    [Theory]
    [InlineData("PROJ-1173 эмодзи из п…", "PROJ-1173")]
    [InlineData("feature/TEAM-1210", "TEAM-1210")]
    [InlineData("Потерянный клик в меню…", null)]
    public void Finds_first_key(string text, string? expected) => Assert.Equal(expected, Default.Find(text));

    [Fact]
    public void Checks_sources_in_order() => Assert.Equal("ABC-2", Default.Find(null, "no key", "ABC-2 then ABC-3"));

    [Theory]
    [InlineData("feature/PROJ-1173_fix", "PROJ-1173")]
    [InlineData("PROJ-1173эмодзи", "PROJ-1173")]
    [InlineData("abcDEF-12", null)]
    public void Respects_boundaries(string text, string? expected) => Assert.Equal(expected, Default.Find(text));

    [Fact]
    public void Null_array_gives_null() => Assert.Null(Default.Find(null));

    [Fact]
    public void Capture_group_gives_the_key() => Assert.Equal("123", new TaskKey(@"#(\d+)").Find("Fix #123 now"));

    [Fact]
    public void Branch_number_pattern() => Assert.Equal("123", new TaskKey(@"(?:^|/)(\d+)-").Find(null, "feature/123-fix"));

    [Fact]
    public void Whole_match_without_a_group() => Assert.Equal("ab-12", new TaskKey(@"[a-z]+-\d+").Find("x ab-12"));

    [Fact]
    public void Non_capturing_group_is_not_the_key_group() =>
        Assert.Equal("PROJ-7", new TaskKey(@"(?:PROJ|OPS)-\d+").Find("PROJ-7"));

    [Fact]
    public void Empty_group_moves_to_the_next_source() => Assert.Equal("5", new TaskKey(@"#(\d*)").Find("# none", "#5"));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.TaskKeyTests"`
Expected: build FAIL with `error CS0712: Cannot create an instance of the static class 'TaskKey'`.

- [ ] **Step 3: Implement `TaskKey`**

Replace the whole content of `src/ClaudeRing.Core/TaskKey.cs`:

```csharp
namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

// Finds the task key with the pattern from settings.
// With a capture group, the key is the first group; otherwise the whole match.
public sealed class TaskKey
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly Regex _pattern;
    private readonly bool _useGroup;

    // Throws ArgumentException for an invalid pattern; SettingsLoader rejects those first.
    public TaskKey(string pattern)
    {
        _pattern = new Regex(pattern, RegexOptions.None, MatchTimeout);
        _useGroup = _pattern.GetGroupNumbers().Length > 1;
    }

    // First key in the first source that has one. Never throws.
    public string? Find(params string?[]? sources)
    {
        foreach (var source in sources ?? [])
        {
            if (source == null) continue;
            Match match;
            try
            {
                match = _pattern.Match(source);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
            if (!match.Success) continue;
            var key = _useGroup ? match.Groups[1].Value : match.Value;
            if (key.Length > 0) return key;
        }
        return null;
    }
}
```

- [ ] **Step 4: Wire `TaskKey` into `ChatSource`**

Replace the whole content of `src/ClaudeRing.Core/ChatSource.cs`:

```csharp
namespace ClaudeRing.Core;

public interface IChatSource
{
    IReadOnlyList<ChatContext> Load();
}

public sealed class ChatSource(OpenChatsReader tabs, SessionInfoReader sessions, RepoFinder repos, TaskKey taskKey) : IChatSource
{
    public IReadOnlyList<ChatContext> Load() =>
        tabs.Read().Select(tab =>
        {
            var info = sessions.Read(tab.SessionId);
            var key = taskKey.Find(info.CustomTitle, tab.Title, info.GitBranch, info.AiTitle);
            return new ChatContext(tab, info, key, key == null ? [] : repos.Find(key));
        }).ToList();

    public static ChatSource CreateDefault(Settings settings, IProcessRunner runner)
    {
        var home = Settings.Home;
        return new ChatSource(
            new OpenChatsReader(Path.Combine(home, "Library", "Application Support", "Code", "User"),
                Path.Combine(home, ".claude", "ide"), runner, OpenChatsReader.IsPidAlive),
            new SessionInfoReader(Path.Combine(home, ".claude", "projects")),
            new RepoFinder(settings.RepoRoots),
            new TaskKey(settings.TaskKeyPattern));
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.
Run the plugin compile check → no errors.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudeRing.Core/TaskKey.cs src/ClaudeRing.Core/ChatSource.cs tests/ClaudeRing.Core.Tests/TaskKeyTests.cs
git commit -m "Make the task key pattern configurable"
```

---

### Task 10: Config hot reload in the plugin

`ConfigWatcher` watches the config file and calls back once the file has been quiet for 300 ms. The plugin then reads the settings again: with errors it keeps the previous settings and menu and logs why; otherwise it builds a new menu, loads chats into it and swaps it in, then refreshes the slots. Plugin code has no unit tests; Task 11 has the manual check.

**Files:**
- Create: `ClaudeRingPlugin/src/Helpers/ConfigWatcher.cs`
- Modify: `ClaudeRingPlugin/src/ClaudeRingPlugin.cs` (whole file)

**Interfaces:**
- Consumes: `SettingsLoader`, `Settings.DefaultPath`, `RingMenu`, `ReadSettings()`, `CreateMenu(Settings)` (Task 8).
- Produces:
  - `internal sealed class ConfigWatcher : IDisposable` with `public static ConfigWatcher Start(String path, Action onChanged)` (null when the folder is missing or cannot be watched) and `Dispose()`.
  - Plugin: `private static void ReloadSettings()`.

- [ ] **Step 1: Add `ConfigWatcher`**

Create `ClaudeRingPlugin/src/Helpers/ConfigWatcher.cs`:

```csharp
namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.IO;
    using System.Threading;

    // Watches the config file. Calls onChanged once the file has been quiet for 300 ms,
    // because editors often save in several steps (temp file, rename, write).
    internal sealed class ConfigWatcher : IDisposable
    {
        private const Int32 DebounceMs = 300;

        private readonly FileSystemWatcher _watcher;

        private readonly Timer _timer;

        private ConfigWatcher(String path, Action onChanged)
        {
            this._timer = new Timer(_ => RunSafely(onChanged), null, Timeout.Infinite, Timeout.Infinite);
            this._watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            this._watcher.Changed += this.OnChanged;
            this._watcher.Created += this.OnChanged;
            this._watcher.Deleted += this.OnChanged;
            this._watcher.Renamed += this.OnChanged;
            this._watcher.Error += (_, e) => PluginLog.Error(e.GetException(), "Слежение за конфигом прервалось");
            this._watcher.EnableRaisingEvents = true;
        }

        // Null when the config folder does not exist or cannot be watched: then the config is read only at start.
        public static ConfigWatcher Start(String path, Action onChanged)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    PluginLog.Info($"Папки {dir} нет, изменения конфига не отслеживаются");
                    return null;
                }
                var watcher = new ConfigWatcher(path, onChanged);
                PluginLog.Info($"Слежу за конфигом {path}");
                return watcher;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось следить за конфигом");
                return null;
            }
        }

        public void Dispose()
        {
            this._watcher.Dispose();
            this._timer.Dispose();
        }

        // Every event moves the timer, so only the last event of a burst counts.
        private void OnChanged(Object sender, FileSystemEventArgs e)
        {
            try
            {
                this._timer.Change(DebounceMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // The plugin is unloading.
            }
        }

        private static void RunSafely(Action onChanged)
        {
            try
            {
                onChanged();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Ошибка при перечитывании конфига");
            }
        }
    }
}
```

- [ ] **Step 2: Reload settings in the plugin**

Replace the whole content of `ClaudeRingPlugin/src/ClaudeRingPlugin.cs`:

```csharp
namespace Loupedeck.ClaudeRingPlugin
{
    using System;
    using System.Linq;
    using System.Threading;
    using ClaudeRing.Core;

    public class ClaudeRingPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        internal static RingMenu Menu { get; private set; }

        internal static Settings CurrentSettings { get; private set; }

        internal static event Action SlotsChanged;

        // Serializes config reloads with each other and with Unload.
        private static readonly Object ReloadLock = new Object();

        private static ProcessRunner _runner;

        private static Launcher _launcher;

        private static ConfigWatcher _configWatcher;

        public ClaudeRingPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            try
            {
                _runner = new ProcessRunner();
                _launcher = new Launcher(_runner, Thread.Sleep);
                var settings = ReadSettings();
                if (settings == null)
                {
                    PluginLog.Warning("Конфиг с ошибками, беру значения по умолчанию");
                    settings = new Settings();
                }
                CurrentSettings = settings;
                Menu = CreateMenu(settings);
                Refresh();

                var labels = Menu.Render().Select(s => s.Label).Where(l => !String.IsNullOrEmpty(l)).ToList();
                PluginLog.Info($"Открытых чатов: {labels.Count}: {String.Join(", ", labels)}");

                RingWatcher.Start(settings.ResetOnRingEvent, onClosedByUser: Refresh);
                lock (ReloadLock)
                {
                    _configWatcher = ConfigWatcher.Start(Settings.DefaultPath, ReloadSettings);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Плагин не загрузился");
            }
        }

        public override void Unload()
        {
            lock (ReloadLock)
            {
                try { _configWatcher?.Dispose(); }
                catch (Exception ex) { PluginLog.Error(ex, "Не удалось остановить слежение за конфигом"); }
                _configWatcher = null;
            }

            try { RingWatcher.Stop(); }
            catch (Exception ex) { PluginLog.Error(ex, "Не удалось отписаться от событий кольца"); }

            // Statics may survive a plugin reload, so drop old references.
            SlotsChanged = null;
            Menu = null;
        }

        // Reads open chats again and shows the root level.
        internal static void Refresh()
        {
            try
            {
                var menu = Menu;
                if (menu == null)
                {
                    return;
                }
                menu.Reset();
                if (menu.LastLoadError != null)
                {
                    PluginLog.Error(menu.LastLoadError, "Не удалось прочитать открытые чаты");
                }
                SlotsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось обновить слоты");
            }
        }

        internal static void Execute(MenuAction action)
        {
            try
            {
                switch (action)
                {
                    case MenuAction.FocusChat a: _launcher.FocusChat(a.Tab); break;
                    case MenuAction.OpenIde a: _launcher.OpenIde(a.Ide, a.Repos); break;
                    case MenuAction.OpenUrl a: _launcher.OpenUrl(a.Url); break;
                    case MenuAction.OpenUrls a: _launcher.OpenUrls(a.Urls); break;
                    case MenuAction.Reveal a: _launcher.Reveal(a.Paths); break;
                    case MenuAction.Run a: _launcher.Run(a.File, a.Args); break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Действие не выполнилось: {Describe(action)}");
            }
        }

        // Records print lists as type names, so show programs and paths in full.
        private static String Describe(MenuAction action) => action switch
        {
            MenuAction.Run a => $"{a.File} {String.Join(" ", a.Args)}",
            MenuAction.OpenIde a => $"{IdeLauncher.DisplayName(a.Ide)}: {String.Join(" ", a.Repos.Select(r => r.Path))}",
            _ => action.ToString(),
        };

        // Called by ConfigWatcher on a timer thread after the config file changes.
        // A config with errors is not applied: the previous settings and menu stay.
        private static void ReloadSettings()
        {
            try
            {
                lock (ReloadLock)
                {
                    if (_configWatcher == null)
                    {
                        return;
                    }

                    var settings = ReadSettings();
                    if (settings == null)
                    {
                        PluginLog.Warning("Новый конфиг с ошибками, оставляю прежние настройки");
                        return;
                    }

                    var previous = CurrentSettings;
                    var menu = CreateMenu(settings);
                    menu.Reset();
                    if (menu.LastLoadError != null)
                    {
                        PluginLog.Error(menu.LastLoadError, "Не удалось прочитать открытые чаты");
                    }
                    CurrentSettings = settings;
                    Menu = menu;
                    if (previous?.ResetOnRingEvent != settings.ResetOnRingEvent)
                    {
                        RingWatcher.Start(settings.ResetOnRingEvent, onClosedByUser: Refresh);
                    }
                    PluginLog.Info("Конфиг перечитан");
                }
                SlotsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Не удалось перечитать конфиг");
            }
        }

        // Writes warnings and errors to the log. Null when the config has errors.
        private static Settings ReadSettings()
        {
            var result = SettingsLoader.Load(Settings.DefaultPath);
            foreach (var warning in result.Warnings)
            {
                PluginLog.Warning($"Конфиг: {warning}");
            }
            foreach (var error in result.Errors)
            {
                PluginLog.Error($"Ошибка в конфиге: {error}");
            }
            return result.Settings;
        }

        private static RingMenu CreateMenu(Settings settings)
        {
            var strings = Strings.For(settings.Language, () => SystemLanguage.Read(_runner));
            return new RingMenu(ChatSource.CreateDefault(settings, _runner), new MenuBuilder(settings, strings));
        }
    }
}
```

- [ ] **Step 3: Check that everything compiles**

Run the plugin compile check → no errors.
Run: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.

- [ ] **Step 4: Commit**

```bash
git add ClaudeRingPlugin/src/Helpers/ConfigWatcher.cs ClaudeRingPlugin/src/ClaudeRingPlugin.cs
git commit -m "Reload the config when the file changes and keep the old one on errors"
```

---

### Task 11: Docs, example config, owner config and manual check

**Files:**
- Test: `tests/ClaudeRing.Core.Tests/ExampleConfigTests.cs`
- Modify: `config.example.json` (whole file)
- Modify: `README.md` (whole file)
- Outside the repo (not committed): `~/.config/claude-ring/config.json`

**Interfaces:**
- Consumes: `SettingsLoader.Load` (Task 2).
- Produces: documentation of the new schema only; an example config that loads with no errors and no warnings.

- [ ] **Step 1: Write the failing test**

Create `tests/ClaudeRing.Core.Tests/ExampleConfigTests.cs`:

```csharp
namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class ExampleConfigTests
{
    // The repo root is the first parent folder with the solution file.
    private static string RepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeRing.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, name);
    }

    [Fact]
    public void Example_config_loads_without_errors_or_warnings()
    {
        var r = SettingsLoader.Load(RepoFile("config.example.json"));
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
        Assert.Equal(7, r.Settings!.ChatMenu.Count);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/ClaudeRing.Core.Tests --filter "FullyQualifiedName~ClaudeRing.Core.Tests.ExampleConfigTests"`
Expected: FAIL — `Assert.Empty() Failure: Collection was not empty` (the old example has legacy fields, so there are warnings).

- [ ] **Step 3: Rewrite `config.example.json`**

Replace the whole content of `config.example.json`:

```json
{
  "language": "auto",
  "repoRoots": ["~/Projects"],
  "taskKeyPattern": "(?<![A-Za-z0-9])[A-Z][A-Z0-9]+-\\d+(?!\\d)",
  "git": {
    "host": "gitlab",
    "baseUrl": "https://gitlab.example.com",
    "group": "acme",
    "targets": ["develop", "main"]
  },
  "tracker": {
    "type": "jira",
    "baseUrl": "https://example.atlassian.net"
  },
  "ide": {
    "type": "rider"
  },
  "chatMenu": [
    "chat",
    "ide",
    "reviews",
    "issue",
    "repos",
    { "label": "Sentry", "url": "https://sentry.io/issues/?query={key}" },
    { "label": "Terminal", "run": ["open", "-a", "Terminal", "{repo.path}"], "perRepo": true }
  ],
  "slotCount": 9,
  "reopenDelayMs": 150,
  "resetOnRingEvent": ""
}
```

- [ ] **Step 4: Run the test to see it pass**

Run: `dotnet test tests/ClaudeRing.Core.Tests` → `Failed:     0`.

- [ ] **Step 5: Rewrite `README.md`**

Replace the whole content of `README.md`:

````markdown
# ClaudeRing

A Logi Actions SDK plugin for the **MX Master 4 Actions Ring**. It lists your open
Claude Code chats from VS Code and opens them, or things related to them (your IDE,
merge or pull requests, the issue, repository pages), from the ring.

The plugin is read-only: it never writes to VS Code, `~/.claude` or your repositories.

## What it does

The plugin adds a ring folder called **Claude** with up to 9 slots. The slots change as you
move through pages:

- **Chats**: one slot per open Claude Code chat (label is the task key, e.g. `PROJ-123`).
  With more chats than slots, the last slot is **more ›** and shows the next chats.
- **Chat page**: the items from `chatMenu`. By default: VS Code, IDE ▸, MR/PR ▸, issue, Repos ▸.
  You can reorder them, hide them and add your own links and commands.
- **Submenus**: pick one repository, or all of them.
- **Back** is always in the last slot of a chat page or submenu.
- **Ring re-open**: after you click a slot the plugin opens the ring again, so you can
  go to the next page without moving the mouse.

## Requirements

- macOS
- Logi Options+ with LogiPluginService 6.4 or newer
- .NET 10 SDK
- MX Master 4
- VS Code with the Claude Code extension

## Build and dev install

```sh
dotnet build ClaudeRing.slnx
```

Building `ClaudeRingPlugin` creates a `.link` file in the Logi plugin directory, so
LogiPluginService loads the plugin straight from the build output. After a new build,
reload it:

```sh
open "loupedeck://plugin/ClaudeRing/reload"
```

Plugin log:

```sh
tail -n 50 ~/Library/Application\ Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeRing.log
```

Run the tests with `dotnet test`.

## Configuration

The config file is `~/.config/claude-ring/config.json`. All fields are optional.
Comments and trailing commas are allowed. See [`config.example.json`](config.example.json).

The plugin reads the file again when you save it, so changes apply without a restart.
If the new file has errors, the plugin keeps the previous settings and writes each error
with its field path (for example `git.host: unknown value "bitbucket"`) to the plugin log.

```jsonc
{
  "language": "auto",                         // auto | en | ru
  "repoRoots": ["~/Projects"],                // where to look for repositories
  "taskKeyPattern": "#(\\d+)",                // how to find the task key (here: GitHub issue numbers)

  "git": {
    "host": "gitlab",                         // gitlab | github
    "baseUrl": "https://gitlab.com",          // self-hosted GitLab or GitHub Enterprise
    "group": "acme",                          // GitLab group or GitHub organization
    "targets": ["develop", "main"]            // target branches in the MR/PR submenu
  },
  "tracker": {
    "type": "jira",                           // jira | linear | youtrack | github | none
    "baseUrl": "https://acme.atlassian.net",  // jira, youtrack
    "workspace": "acme"                       // linear
  },
  "ide": {
    "type": "rider"                           // see the IDE table below
    // "app": "/Applications/Some.app"        // required for custom, optional for the others
  },

  "chatMenu": [
    "chat", "ide", "reviews", "issue", "repos",
    { "label": "Sentry", "url": "https://sentry.io/issues/?query={key}" },
    { "label": "Terminal", "run": ["open", "-a", "Terminal", "{repo.path}"], "perRepo": true }
  ],

  "slotCount": 9,
  "reopenDelayMs": 150
}
```

| Field | Default | Meaning |
|---|---|---|
| `language` | `auto` | Slot labels: `en`, `ru`, or `auto` (Russian when the first macOS language is Russian, otherwise English) |
| `repoRoots` | `["~/Projects"]` | Folders searched for repositories and worktrees. Absolute paths or `~/...` |
| `taskKeyPattern` | keys like `PROJ-123` | Regular expression for the task key. It is searched in the chat's custom title, tab title, git branch and AI title, in this order. If the pattern has a capture group, the key is the first group, otherwise the whole match |
| `git.host` | `gitlab` | `gitlab` or `github` |
| `git.baseUrl` | `https://gitlab.com` or `https://github.com` | Address of a self-hosted GitLab or GitHub Enterprise |
| `git.group` | none | GitLab group or GitHub organization for the MR/PR search. Without it GitLab uses your MR dashboard and GitHub searches everywhere |
| `git.targets` | `["main"]` | Target branches shown in the MR/PR submenu as `→ <branch>` |
| `tracker.type` | `none` | `jira`, `linear`, `youtrack`, `github` or `none` |
| `tracker.baseUrl` | none | Jira or YouTrack address |
| `tracker.workspace` | none | Linear workspace |
| `ide.type` | `none` | IDE for the IDE item. With `none` the item is hidden |
| `ide.app` | `/Applications/<Name>.app` | Path to the IDE app. Required for `custom` |
| `chatMenu` | `["chat", "ide", "reviews", "issue", "repos"]` | Items of the chat page, in slot order |
| `slotCount` | 8 | Number of slots in the folder, 3 to 9 (the editor allows up to 9) |
| `reopenDelayMs` | 150 | Delay before the ring is opened again. A negative value turns re-open off |
| `resetOnRingEvent` | `""` | Service event that resets the menu to the chat list. Empty means never |

### Built-in items

| Id | Shown when | Click |
|---|---|---|
| `chat` | always | Opens the chat in VS Code |
| `ide` | `ide.type` is set and the chat has repositories | Submenu: one slot per repository, then "All" |
| `reviews` | the chat has a task key | Submenu "MR" (GitLab) or "PR" (GitHub): all of them, then one per `git.targets` branch |
| `issue` | the chat has a task key and the tracker can build a link | Opens the issue |
| `repos` | the chat has repositories | Submenu: one slot per repository (branch page in the browser), "All in browser", "Folder in Finder" |

Links:

- GitLab: branch `{repo}/-/tree/{branch}`; MRs `{baseUrl}/groups/{group}/-/merge_requests?scope=all&state=all&search={key}`
  (without a group: `{baseUrl}/dashboard/merge_requests?...`), a target adds `&target_branch={target}`.
- GitHub: branch `{repo}/tree/{branch}`; PRs `{baseUrl}/pulls?q=is:pr+{key}`, a target adds `+base:{target}`,
  a group adds `+org:{group}`.
- Jira `{baseUrl}/browse/{key}`, YouTrack `{baseUrl}/issue/{key}`, Linear `https://linear.app/{workspace}/issue/{key}`.
- GitHub Issues: `{repo}/issues/{key}`, using the first repository with a web address. The key must be the
  issue number, so set a pattern that gives the number, e.g. `"taskKeyPattern": "#(\\d+)"`, or
  `"(?:^|/)(\\d+)-"` for branches like `123-fix`.

### IDEs

| `ide.type` | Default app | How repositories open |
|---|---|---|
| `rider`, `idea`, `webstorm`, `pycharm`, `goland`, `clion`, `rubymine`, `phpstorm` | `/Applications/<Name>.app` (`IntelliJ IDEA.app` for `idea`) | All repositories in one call of the app's launcher |
| `vscode`, `cursor`, `windsurf` | `/Applications/Visual Studio Code.app`, `Cursor.app`, `Windsurf.app` | One new window per repository (`-n`) |
| `custom` | none, set `app` | `open -a <app> <repository>` once per repository |

### Custom items

Add objects to `chatMenu`:

- `{ "label": "...", "url": "<template>" }` opens the link.
- `{ "label": "...", "run": ["<program>", "<argument template>", ...] }` starts the program directly, without a
  shell. Placeholders work only in arguments, not in the program.
- Add `"perRepo": true` to get a submenu of the chat's repositories; the click runs the item for that repository.

Placeholders:

| Placeholder | Value |
|---|---|
| `{key}` | Task key |
| `{branch}` | The chat's git branch, or the branch of the first repository |
| `{title}` | Chat title (custom title, else tab title) |
| `{sessionId}` | Claude Code session id |
| `{repos.parent}` | Folder that contains all of the chat's repositories |
| `{repo.name}`, `{repo.path}`, `{repo.webUrl}`, `{repo.branch}` | The repository, only with `"perRepo": true` |

In links, values are escaped (`/` stays in branches and paths; `{repo.webUrl}` is used as is). In `run`
arguments, values are passed as they are. If a placeholder has no value for a chat, the item is hidden for
that chat (or that repository is left out of the submenu). An item with an unknown placeholder, or an unknown
built-in id, is skipped and the log says why; the rest of the menu works.

## Ring setup in Options+

1. Open the Actions Ring settings in Options+, in the general profile and in the VS Code profile.
2. Add a folder named **Claude**.
3. Put the commands **Claude слот 1** ... **Claude слот 9** into the folder, in order.

## How it works

- **Open chats**: reads VS Code `state.vscdb`, key `Anthropic.claude-code`, field
  `panelTabSessions`, through `/usr/bin/sqlite3 -readonly`. The files `~/.claude/ide/*.lock`
  tell which VS Code windows are alive.
- **Chat details**: session `.jsonl` files from `~/.claude` are read incrementally to get
  the custom title, the git branch and the working directory.
- **Repositories**: found in `repoRoots` by the current branch name that contains the task key.
  Worktrees and `<repo>-<KEY>` or `<KEY>/<repo>` layouts both work.
- **Open a chat**: `vscode://anthropic.claude-code/open?session=<id>`.
- **Ring re-open**: the plugin uses reflection inside LogiPluginService:
  `ServiceTelemetrySender` → `Loupedeck7DeviceManager` → `RadialMenuMessageHandler.OnShowDevice(true)`.

More details are in [`docs/spike-results.md`](docs/spike-results.md).

## Known limitations

- Chats come only from the VS Code Claude Code extension.
- The ring re-open relies on undocumented internals of LogiPluginService. It can break after an update.
- There is no ring-close event, so the current page stays until you run an action or press Back.
- An empty VS Code window cannot be targeted precisely.
- Config changes are watched only when the folder `~/.config/claude-ring` exists when the plugin starts.
- macOS only.

## License

MIT, see [LICENSE](LICENSE).
````

Check that the README does not mention old fields:
Run: `grep -n "gitLabBaseUrl\|gitLabGroup\|jiraBaseUrl\|mrTargets\|riderPath" README.md config.example.json`
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add tests/ClaudeRing.Core.Tests/ExampleConfigTests.cs config.example.json README.md
git commit -m "Document the new config schema and update the example config"
```

- [ ] **Step 7: Migrate the owner's config (outside the repo, not committed)**

The owner's `~/.config/claude-ring/config.json` uses the old schema. Convert it with `jq` so the values come from the file itself (this plan is a tracked file and must not contain them):

```bash
cfg=~/.config/claude-ring/config.json
cp "$cfg" "$cfg.legacy.bak"
jq '{
  language: "ru",
  repoRoots: .repoRoots,
  git: { host: "gitlab", baseUrl: .gitLabBaseUrl, group: .gitLabGroup, targets: [.mrTargets[].branch] },
  tracker: { type: "jira", baseUrl: .jiraBaseUrl },
  ide: { type: "rider" },
  slotCount: .slotCount,
  reopenDelayMs: .reopenDelayMs
}' "$cfg.legacy.bak" > "$cfg.new" && mv "$cfg.new" "$cfg"
cat "$cfg"
```

Expected: a JSON object with exactly the keys `language`, `repoRoots`, `git` (`host`, `baseUrl`, `group`, `targets` with three branches), `tracker` (`type`, `baseUrl`), `ide` (`type`), `slotCount` 9, `reopenDelayMs` 150; no `null` values. The controller compares the values with the ones in its brief. Do not `git add` anything from `~/.config`.

- [ ] **Step 8: Manual check (ask the owner before this step: building the plugin reloads the live plugin)**

0. Keep a copy of the migrated config to restore it at the end: `cp ~/.config/claude-ring/config.json ~/.config/claude-ring/config.json.migrated.bak`.
1. Build and reload the plugin: `dotnet build ClaudeRingPlugin/src/ClaudeRingPlugin.csproj`.
2. Log: `tail -n 50 ~/Library/Application\ Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeRing.log`
   → `Слежу за конфигом ...`, `Открытых чатов: ...`; no `Ошибка в конфиге`, no `Конфиг:` warnings.
3. Ring, owner config (Russian labels): a chat → `VS Code`, `Rider`, `MR`, `Jira`, `Репо`, `← назад` in the last slot.
   `Rider` → repos without the `-<KEY>` suffix, `Все`. `MR` → `Все MR`, `→ Dev`, `→ RC`, `→ master`, links open
   the GitLab MR search. `Jira` opens the issue. `Репо` → repos, `Все в браузере`, `Папка в Finder`.
   More chats than slots → `ещё ›`. A final action returns the ring to the chat list. Empty slots are transparent.
4. Hot reload: set `"language": "en"` and save. Log → `Конфиг перечитан`; the ring shows `Repos`, `All MRs`,
   `← Back` without a plugin reload. Set it back to `"ru"`.
5. Broken config: delete a closing `}` and save. Log → `Ошибка в конфиге: config: invalid JSON: ...` and
   `Новый конфиг с ошибками, оставляю прежние настройки`; the ring keeps working as before. Fix the file → `Конфиг перечитан`.
6. Custom items: add to `chatMenu` (after the five built-in ids)
   `{ "label": "Sentry", "url": "https://sentry.io/issues/?query={key}" }` and
   `{ "label": "Terminal", "run": ["open", "-a", "Terminal", "{repo.path}"], "perRepo": true }`.
   Sentry opens the link with the task key; Terminal shows a repo submenu and opens Terminal in the chosen repo.
   A chat without a task key does not show Sentry. Remove both items.
7. Other presets: temporarily set `"git": { "host": "github", "group": "acme" }`, `"tracker": { "type": "linear", "workspace": "acme" }`,
   `"ide": { "type": "cursor" }`. The chat page shows `PR`, `Linear`, `Cursor`; `PR` links go to `github.com/pulls?q=is:pr+...`.
   If Cursor is not installed, clicking a repo under `Cursor` logs `Действие не выполнилось: Cursor: ...` with the program
   path, and the ring keeps working.
8. Restore the migrated config: `cp ~/.config/claude-ring/config.json.migrated.bak ~/.config/claude-ring/config.json`
   → log `Конфиг перечитан`, the ring is back to step 3. The `*.bak` files stay for the owner to delete.
9. `git status --short` → clean (no `.superpowers/`, nothing from `~/.config`).
