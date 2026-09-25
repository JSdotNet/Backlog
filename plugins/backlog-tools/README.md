# backlog-tools

Backlog-native tooling — skills specific to the Backlog product itself, as opposed to
general-purpose or knowledge-folder tooling. Three skills: one for each direction of a plan,
and one for the remarks a person leaves while reading.

- **`backlog-import-plan`** — turns an agreed specification into a Backlog import plan
  (ADR 0007: `.arc42/adr/0007-import-reuses-the-entry-text-grammar.md`). Every entry is
  a `prompt` an AI session runs, or a `task` or `test` only the user does — never both in one
  entry. It always ships a review view next to the raw plan — one HTML page built from
  `skills/backlog-import-plan/assets/plan-review.html` that parses the embedded plan
  itself and shows its checks, dependency order and entries, published as an artifact where
  the host has one and written beside the plan otherwise. User-invoked only
  (`disable-model-invocation: true`); it never talks to the Backlog app or GitHub.
  A plan imports at two levels (ADR 0013:
  `.arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md`): it opens with
  one `plan` entry, which Import turns into the Roadmap Item, and the step entries under it
  share its `+tag`, so the item gathers them:

  ```markdown
  # VS Code desktop rollout

  `plan` `*high` `+vscode-desktop-rollout` `repo:backlog-desktop`

  # Add the export command

  `prompt` `!ready` `+vscode-desktop-rollout` `id:add-command` `repo:backlog-desktop` `effort:5`
  ```

  Asked for the roadmap level, it writes `plan` entries only, one per agreed feature
  chapter, ordered by `after:` from the chapters' `depends-on` lists.
- **`backlog-run-plan-item`** — runs one entry of such a plan after it is copied out of the
  Backlog app and pasted into a session. The app puts the invocation on the first line of
  every entry it copies — `/backlog-tools:backlog-run-plan-item entry `<id>`:`, the entry
  under it — so the paste runs the skill outright; it is also model-invoked on the marker
  line every generated `prompt` entry opens with (`Backlog plan item `…``). Both shapes are
  defined in `skills/backlog-import-plan/assets/backlog-import-grammar.md`. It checks the
  item is still outstanding before doing anything — pasting the same item twice is expected
  and must not redo finished work, and declines a `plan` entry in one line, since only
  Import acts on one — and then carries out the instructions the way the
  current repository says work is done. When a `backlog` MCP server is in the session's
  tool list it reads the entry's status from it (`read_item`) and reports Ready → In
  progress → Done back (`transition`), every call carrying the `repository` read off the
  git remote; without one it falls back to searching git and says the status has to be set
  by hand.
- **`backlog-answer-notes`** — empties the other inbox: the private reading notes a person
  left on a repository's Devbook chapters in the app. It reads them over MCP
  (`list_annotations`), writes each answer into the chapter as a devbook `annotation` fence
  through the devbook plugin's own `annotations.mjs`, and only then resolves the note
  (`resolve_annotation`). Fence first, resolve second, because that is the order whose
  half-done state is recoverable. The two annotation kinds stay two things —
  `.arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md` §6 is the decision,
  and the app is never the fence's writer. `backlog-run-plan-item` carries a short form of
  the same procedure for the notes an item it just ran leaves answerable.

The plan-item marker exists because a plan is written before the app has given its entries
an id, so it restates the `id:`, `+tag`, `repo:` and `after:` the metadata line carries. The
entry line carries what only the app knows — the stored id — and is what makes any copied
entry a runnable paste.

`hooks/hooks.json` adds a `UserPromptSubmit` hook (Claude Code) that notices either line in
a prompt and nudges the session to invoke `backlog-run-plan-item`, so triggering does not
rest on the skill description alone. It needs `grep` on the hook shell, which Claude Code
provides on every platform it runs on.

It also registers `hooks/telemetry-forwarder.mjs` for `PreToolUse`, `PostToolUse`,
`SubagentStop`, `PreCompact`, `Stop` and `SessionEnd`. Each event is posted to the app's
`/telemetry` endpoint — beside `/mcp`, on the same port and behind the same bearer token — and
the app attributes the tool calls, delegated agents, token usage and context gauge to the
delivery run that session is driving, which the Sessions pane shows under the run's row. The
port and token come from `BACKLOG_MCP_PORT` and `BACKLOG_MCP_TOKEN`, else from the app's own
`settings.json`. The tool-call events
are matched to shell, edits, sub-agents, skills and MCP tools, the calls a run's figures are
read for, so a session is not paying a process spawn on every `Read` and `Grep`. The script
needs Node, drops the tool's output before posting, gives up after two seconds and exits 0 on
any error, so a closed app costs a session nothing but a refused connection.

Copilot CLI gets a root `hooks.json` with the plan-item nudge as a `userPromptSubmit` prompt
hook. Its presence is also what keeps Copilot from falling back to `hooks/hooks.json`: the
forwarder reads a Claude Code payload and the app reads a Claude Code transcript, so the
telemetry is Claude Code only.

This plugin ships two manifests so it installs the same way in either host:

- `.claude-plugin/plugin.json` — Claude Code
- `.github/plugin/plugin.json` — GitHub Copilot CLI

Both point at the same `skills/` folder. Keep their `name`/`description`/`version` fields
in sync by hand when either changes — there is no generator here.

## Install

**Claude Code**, inside a session started at the repository root:

```
/plugin marketplace add .
/plugin install backlog-tools@jsdotnet-backlog
```

**Copilot CLI**, from the repository root:

```bash
copilot plugin install ./plugins/backlog-tools
```
