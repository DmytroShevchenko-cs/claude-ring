# ClaudeRing

A Logi Actions SDK plugin for the **MX Master 4 Actions Ring**. It lists your open
Claude Code chats from VS Code and opens them (or related things) in VS Code, Rider,
GitLab and Jira, all from the ring.

The plugin is read-only: it never writes to VS Code, `~/.claude` or your repositories.

## What it does

The plugin adds a ring folder called **Claude** with 9 slots. The slots change as you
move through levels:

- **Chats**: one slot per open Claude Code chat (label is the task key, e.g. `PROJ-123`).
- **Chat actions**: VS Code, Rider ▸, MR ▸, Jira, Repos ▸.
- **Rider / Repos**: pick one of the repositories found for the task.
- **MR**: all merge requests for the task key, or only those for one target branch.
- **Ring re-open**: after you click a slot the plugin opens the ring again, so you can
  go to the next level without moving the mouse.

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
See [`config.example.json`](config.example.json).

| Field | Default | Meaning |
|---|---|---|
| `repoRoots` | `["<home>/Projects"]` | Folders searched for repositories and worktrees |
| `gitLabBaseUrl` | `https://gitlab.com` | GitLab host used for links |
| `gitLabGroup` | none | GitLab group for the MR search. Without it the personal MR dashboard is used |
| `jiraBaseUrl` | none | Jira site, e.g. `https://example.atlassian.net`. Without it the Jira slot is hidden |
| `mrTargets` | develop, main | Target branches shown in the MR level (`label`, `branch`) |
| `riderPath` | Rider.app path | Path to the Rider executable |
| `slotCount` | 8 | Number of slots in the folder (the editor allows up to 9) |
| `reopenDelayMs` | 150 | Delay before the ring is opened again |
| `resetOnRingEvent` | `""` | Service event that resets the menu to the first level. Empty means never |

`~` is not expanded. Use absolute paths in `repoRoots`.

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

- The ring re-open relies on undocumented internals of LogiPluginService. It can break after an update.
- There is no ring-close event, so the current level stays until you run an action or press Back.
- An empty VS Code window cannot be targeted precisely.
- macOS only.

## License

MIT, see [LICENSE](LICENSE).
