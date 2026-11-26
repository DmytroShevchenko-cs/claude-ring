# Configurable ring menu — design (part 1 of 4)

Status: approved in brainstorming, 2026-11-26.

## Goal

Make ClaudeRing usable by any developer, not only the author's stack. Part 1 makes the **menu** configurable: built-in presets for common git hosts, issue trackers and IDEs, custom menu items from templates, item order and visibility, English and Russian labels, and config hot reload.

Later parts (separate specs): 2 — chat sources (VS Code family, terminal sessions); 3 — JetBrains Claude plugin as a chat source; 4 — distribution (`.lplug4`, GitHub releases, first-run setup, maybe Logi Marketplace).

Out of scope for part 1: new chat sources (the `chat` item keeps opening the VS Code chat as today), packaging.

## Config schema

File: `~/.config/claude-ring/config.json` (JSON; comments and trailing commas allowed).

```jsonc
{
  "language": "auto",                         // auto | en | ru; auto = macOS UI language, fallback en
  "repoRoots": ["/Users/me/Projects"],        // absolute paths; "~/" is expanded
  "taskKeyPattern": "[A-Z][A-Z0-9]+-\\d+",    // regex; first match in custom title, tab title, branch, AI title

  "git": {
    "host": "gitlab",                         // gitlab | github
    "baseUrl": "https://gitlab.com",          // self-hosted allowed; default per host
    "group": "acme",                          // gitlab group or github org; optional
    "targets": ["develop", "main"]            // target branches for the reviews submenu
  },
  "tracker": {
    "type": "jira",                           // jira | linear | youtrack | github | none
    "baseUrl": "https://acme.atlassian.net",  // jira, youtrack
    "workspace": "acme"                       // linear
  },
  "ide": {
    "type": "rider"                           // none | rider | idea | webstorm | pycharm | goland | clion | rubymine | phpstorm
                                              // | vscode | cursor | windsurf | custom
    // "app": "/Applications/Some.app"        // required for custom; optional override for others
  },

  "chatMenu": [                               // order of items on the chat level; omitted = default list
    "chat", "ide", "reviews", "issue", "repos",
    { "label": "Sentry", "url": "https://sentry.io/issues/?query={key}" },
    { "label": "Terminal", "run": ["open", "-a", "Terminal", "{repo.path}"], "perRepo": true }
  ],

  "slotCount": 9,
  "reopenDelayMs": 150
}
```

Defaults: `language` auto; `repoRoots` `[~/Projects]`; `git` gitlab / `https://gitlab.com` / no group / targets `["main"]`; `tracker` none; `ide` none (the `ide` item is hidden); `chatMenu` `["chat", "ide", "reviews", "issue", "repos"]`; `slotCount` 8; `reopenDelayMs` 150.

Legacy fields from the current schema (`gitLabBaseUrl`, `gitLabGroup`, `jiraBaseUrl`, `mrTargets`, `riderPath`) are still read for one version and mapped to the new schema (`gitLab*` → `git` with host gitlab; `jiraBaseUrl` → `tracker` jira; `mrTargets[].branch` → `git.targets`; `riderPath` → `ide` rider with that app path). New fields win when both exist. The README documents only the new schema.

## Built-in items (presets)

| Id | Shown when | Click |
|---|---|---|
| `chat` | always | focus the chat where it came from (today: VS Code window + `vscode://anthropic.claude-code/open?session=`) |
| `ide` | `ide` configured and chat has repos | submenu: one slot per repo (open that repo), "All" (open all), back |
| `reviews` | task key present | submenu titled "MR" (gitlab) or "PR" (github): "All", one item per `git.targets`, back |
| `issue` | task key present and tracker ≠ none and its required fields set | open the issue |
| `repos` | chat has repos | submenu: one slot per repo (open its branch page), "All in browser", "Folder in Finder", back |

Links per host:
- GitLab: branch `{webUrl}/-/tree/{branch}`; reviews `{baseUrl}/groups/{group}/-/merge_requests?scope=all&state=all&search={key}[&target_branch={t}]`, without group `{baseUrl}/dashboard/merge_requests?...`.
- GitHub: branch `{webUrl}/tree/{branch}`; reviews `https://github.com/pulls?q=is:pr+{key}[+base:{t}]` (+ `org:{group}` when set; `baseUrl` replaces `https://github.com` for GitHub Enterprise).

Task key extraction: if `taskKeyPattern` has a capture group, the key is the first group; otherwise the whole match.

Links per tracker: Jira `{baseUrl}/browse/{key}`; YouTrack `{baseUrl}/issue/{key}`; Linear `https://linear.app/{workspace}/issue/{key}`; GitHub Issues `{webUrl of the first repo with a web URL}/issues/{key}` — the user sets a pattern that yields the number, e.g. `"taskKeyPattern": "#(\\d+)"` or `"(?:^|/)(\\d+)-"` for branches like `123-fix`; hidden when the key is not all digits or no repo has a web URL.

IDE launch:
- JetBrains types: `{app}/Contents/MacOS/{binary}` with all repo paths as arguments (binary: rider, idea, webstorm, pycharm, goland, clion, rubymine, phpstorm); default app `/Applications/<Name>.app`.
- vscode / cursor / windsurf: the app's CLI inside the bundle (`Contents/Resources/app/bin/code|cursor|windsurf`) with `-n <path>` once per repo (one window each).
- custom: `open -a {app} <path>` once per repo.

## Custom items

- `{ "label": "...", "url": "<template>" }` — open the rendered URL.
- `{ "label": "...", "run": ["<file>", "<arg template>", ...] }` — start the program directly (no shell); placeholders only inside arguments, not in `file`.
- `"perRepo": true` on either — the item opens a submenu listing the chat's repos (display names without the `-<KEY>` suffix); clicking a repo runs the item with `{repo.*}` bound to it.

Placeholders: `{key}`, `{branch}` (the chat's git branch or the first repo's branch), `{title}`, `{sessionId}`, `{repos.parent}`, and in perRepo context `{repo.name}`, `{repo.path}`, `{repo.webUrl}`, `{repo.branch}`. URL templates escape values (`Uri.EscapeDataString`, keeping `/` in branches); `run` arguments are passed as is. If a placeholder has no value, the item is hidden (for that chat, or for that repo in a perRepo submenu). An unknown placeholder name makes the item invalid (see errors).

## Code structure (Core, unit tested)

| Unit | Responsibility |
|---|---|
| `Settings`, `SettingsLoader` | Parse the new schema (JSONC), map legacy fields, validate, return `Settings` or a list of errors with field paths. |
| `Template` | Render a template with a value map; report missing and unknown placeholders. |
| `IGitHost` → `GitLabHost`, `GitHubHost` | Branch URL, reviews URL (key, optional target), reviews label ("MR"/"PR"). |
| `ITracker` → `JiraTracker`, `LinearTracker`, `YouTrackTracker`, `GitHubIssuesTracker`, `NoTracker` | Issue URL for a key (or null). |
| `IdeLauncher` | IDE table and the process commands per type. |
| `Strings` | Label texts in en and ru; language from settings or system. |
| `MenuBuilder` | From settings + presets + chat context, build the chat-level items and their submenus. |
| `RingMenu` | Page stack instead of fixed levels: root page (chats, paging "more ›"), pushing a submenu page, "back" pops. Unchanged: exactly `slotCount` slots, back in the last slot on non-root pages, atomic clicks under a lock, final action → root without reload, reload outside the lock. |
| `MenuAction` | Adds `Run(string File, IReadOnlyList<string> Args)`. |

Plugin: `ConfigWatcher` watches the config file (FileSystemWatcher, 300 ms debounce), reloads settings, rebuilds the menu, refreshes slots. Existing plugin pieces (slot command, big-text images, ring reopen, ring watcher) stay.

Data flow: config file → `SettingsLoader` → `Settings` → presets (`IGitHost`, `ITracker`, `IdeLauncher`, `Strings`) → `MenuBuilder` → `RingMenu` → slot labels/images.

## Errors

No exception may reach the Logitech service.
- Invalid JSON or invalid values (unknown host/tracker/ide type, bad regex, custom IDE without `app`): the new config is not applied; the previous one stays; the log lists each error with its field path. On first load with an invalid config, defaults are used.
- Unknown built-in id or invalid custom item in `chatMenu`: that item is skipped with a warning; the rest works.
- Missing placeholder value: item hidden for that chat/repo (no log noise).
- `run` program or IDE missing / cannot start: error logged with the program path; the ring keeps working.

## Testing

Core (xUnit): settings parsing (full example, minimal, legacy mapping, every error kind); templates (escaping, missing, unknown); each git host and tracker URL; IDE commands per type via `FakeProcessRunner`; `MenuBuilder` (order, hiding rules, perRepo submenus, custom url/run); `RingMenu` page stack (all current scenarios migrated, nested pages, back at any depth, paging); `Strings` both languages. Plugin: manual check — edit the config while the plugin runs, the ring changes without a reload.

## Success criteria

- With the author's local config migrated to the new schema, the ring behaves as today (VS Code, Rider submenu, MR targets Dev/RC/master, Jira, Repos).
- A GitHub + Linear + Cursor config shows "PR", Linear issue links and opens repos in Cursor, with no code change.
- A custom `url` item and a custom `run` perRepo item work from config alone.
- Editing the config applies without restarting the plugin; a broken config keeps the previous menu and logs why.
