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
- **Chat page**: the items from `chatMenu`. By default: VS Code, IDE ▸, MR/PR ▸, issue, Repos ▸. The IDE and issue items appear once `ide` / `tracker` are configured.
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

  "git": {
    "host": "gitlab",                         // gitlab | github
    "baseUrl": "https://gitlab.com",          // self-hosted GitLab or GitHub Enterprise
    "group": "acme",                          // GitLab group or GitHub organization
    "targets": [                              // items of the MR/PR submenu
      "develop",                              // one target branch
      { "label": "prod", "branches": ["master", "main"] }  // one item for repos that use master or main
    ]
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
| `git.targets` | `["main"]` | Items of the MR/PR submenu, shown as `→ <label>`. A string is one target branch. `{ "label": "prod", "branches": ["master", "main"] }` is one item for several branch names: it opens the search for each of them that exists on `origin` in the chat's repositories (all of them when none is found) |
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
| `reviews` | the chat has a task key | Submenu "MR" (GitLab) or "PR" (GitHub): all of them, then one per `git.targets` entry |
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
| `{title}` | Chat title (custom title, else tab title, else AI title) |
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
- If `config.json` is a symlink, edits to the link target are not seen by hot reload. Save the real file or reload the plugin.
- macOS only.

## License

MIT, see [LICENSE](LICENSE).
