# backlog-tools

Backlog-native tooling — skills specific to the Backlog product itself, as opposed to
general-purpose or devbook tooling. Eight skills: one for each direction of a plan,
one for running a whole plan, one for checking it once it has run, one for closing the entries
whose pull request merged, one for bringing another tool's items into the Inbox, one for the
remarks a person leaves while reading, and one that dispatches the session requests the app
queues — and a pane that follows a running plan.

- **`import-plan`** — turns an agreed specification into a Backlog import plan
  (ADR 0007: `.devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md`). Every entry is
  a `prompt` an AI session runs, or a `task` or `test` only the user does — never both in one
  entry. Prompts are the default: while it writes the plan it interviews you — the open
  decisions, the values and access the work needs, what a pass looks like — and writes the
  answers into the prompts, so no step stops to ask when it runs. A manual check is offered
  as an AI-run QA prompt first, and a step stays a `task` or `test` only when you keep it,
  with a `Kept manual:` line saying why. Every step, whatever its kind, names its
  repository with `repo:`. Every prompt — and every task or test that checks what the source
  describes — carries a `Read first:` line naming the sections and designs it was planned
  from, by repository path and `#anchor` or artifact URL, so a session running the pasted
  entry reads the same context; `run-plan-item` reads them before it starts. It always ships a review view next to the raw plan — one HTML page built from
  `skills/import-plan/assets/plan-review.html` that parses the embedded plan
  itself and shows its checks, dependency order and entries, published as an artifact every
  run before the plan text is shown — a local HTML file only when the host has no artifacts. User-invoked only
  (`disable-model-invocation: true`); it never talks to the Backlog app or GitHub.
  A plan imports at two levels (ADR 0013:
  `.devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md`): it opens with
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
- **`run-plan-item`** — runs one entry of such a plan after it is copied out of the
  Backlog app and pasted into a session. The app puts the invocation on the first line of
  every entry it copies — `/backlog-tools:run-plan-item entry `<id>`:`, the entry
  under it — so the paste runs the skill outright; it is also model-invoked on the marker
  line every generated `prompt` entry opens with (`Backlog plan item `…``). Both shapes are
  defined in `skills/import-plan/assets/backlog-import-grammar.md`. It checks the
  item is still outstanding before doing anything — pasting the same item twice is expected
  and must not redo finished work, and declines a `plan` entry in one line, since only
  Import acts on one — and then carries out the instructions the way the
  current repository says work is done. When a `backlog` MCP server is in the session's
  tool list it reads the entry's status from it (`read_item`) and reports Ready → In
  progress → Done back (`transition`) — In progress while the pull request is open, Done once
  it has merged into the default branch or a `plan/<slug>` branch — every call carrying the `repository` read off the
  git remote; without one it falls back to searching git and says the status has to be set
  by hand.
- **`execute-plan`** — runs a whole plan, after the pattern of Matt Pocock's
  `implement-spec`. It reads the plan from the `backlog` MCP server by its `+tag`
  (`get_plan_items`) or from a file `import-plan` wrote, builds the graph its `after:`
  tokens draw, and changes nothing itself: each `prompt` entry whose prerequisites are done
  goes to its own sub-agent, briefed by `skills/execute-plan/assets/item-brief.md`,
  which makes its own worktree and runs the entry through `run-plan-item` — so every
  entry passes the repository's gate and its own Personal Validation. Entries that wait on
  nothing run in parallel. Before it starts anything — attended or unattended — it opens every entry's `Read first:`
  references and linked Devbook chapters, and warns about each one it cannot reach, asking
  once whether to go, hold the entries that name it, or stop to fix the plan.
  An entry is done when its pull request merges — into `plan/<slug>` in
  branch mode, into the base branch otherwise; the orchestrator
  then moves it to Done and starts what waited on it. It stops at `task` and `test` entries,
  listing what they hold up, starts the application in its own worktree for the first one
  that validates landed work, and resumes from the plan's state when invoked again. Three
  landing modes, defined in `skills/execute-plan/assets/landing-modes.md`: *per item*, each
  pull request to the base branch; *branch*, each into one `plan/<tag without its +>` branch that the
  orchestrator merges into, ending in a single pull request to the base branch; and
  *stacked*, each cut from and targeting its predecessor's branch, so a chain runs before
  anything merges and the person merges bottom-up. *Attended*, the orchestrator relays each
  Personal Validation to the person; *unattended* (branch or stacked), each entry parks at
  its gate and lands as a draft pull request whose body is the review handoff, so the whole
  plan runs without anyone present and Personal Validation happens on the drafts — in branch
  mode on the closing draft to the base branch. A failed entry blocks only what depends on
  it. It titles its own session `<tag>:execute-plan` and labels each sub-agent
  `<tag>:<n> - <Title>`, and a ready entry in another repository is handed to a new session
  in that repository's checkout running the same plan, per
  `skills/execute-plan/assets/handoff.md`. User-invoked only. The `UserPromptSubmit` hook
  stays quiet on a prompt that names this skill, so a pasted plan is not mistaken for one item.
- **`final-check`** — the one check at the end of a plan, run in the session that wrote it
  with `import-plan`, since that session still holds the specification. It brings the checkout
  up to the plan's landed tip — the base branch, or `origin/plan/<slug>` while its closing
  pull request is open, or the top of a stack not yet merged — by fast-forward, or in a detached scratch worktree when the
  checkout has work of its own. Then it reviews each `prompt` entry against its instructions,
  checks and `Read first:` sections at that tip, with `file:line` for every verdict
  (*implemented*, *partial*, *missing*, *not landed*), looks across entries for duplicated
  work, leftovers and specification nobody covered, and runs the repository's build, tests and
  checks and reads the base branch's CI. It reports one table and offers the fixes, which go
  through the gate as their own change. User-invoked only; it edits, commits and transitions
  nothing.
- **`close-merged`** — moves to Done every entry whose pull request has merged into the
  default branch or a `plan/<slug>` branch: the merges `run-plan-item` and `execute-plan` did
  not see, made on GitHub's own page or after the session ended. It lists the repository's
  `in_progress` and `ready` entries (`list_entries`) and the pull requests merged into either
  since the oldest of them started (`gh pr list`), resolves each pull request to its entry by
  the link `link_change` recorded (`find_item` with `externalId` and `repoId`), and
  `transition`s it. A pull request merged into a stacked parent finishes nothing yet, and a
  `task` or `test` — the plan's sign-off among them — is left to the person and to
  `execute-plan`'s Resume. Model-invoked; it writes nothing to the repository or to GitHub.
- **`import-inbox`** — turns an export from another to-do tool into an inbox import
  manifest (ADR 0017:
  `.devbook/arc42/adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md`).
  The manifest is Markdown with front matter, one `#` item per open task, each with a `meta`
  fence of capture facts. The Inbox's Sources panel imports it through **Import file…**.
  Completed tasks are dropped. The person chooses which labels become tags and which lists
  file into which Inbox Lists. Microsoft To Do is the first tool it knows. Another tool is
  added as a row of the skill's Formats table, never as a second manifest shape, so the
  product needs no converter per tool. Like the plan skill, it always ships a review view,
  built from `skills/import-inbox/assets/inbox-import-review.html`. The view parses
  the embedded manifest itself and runs the checks of the grammar in
  `skills/import-inbox/assets/inbox-import-manifest.md`. That matters more here: a
  hand-edited manifest can break in ways a generated one never does. User-invoked only
  (`disable-model-invocation: true`); it never talks to the Backlog app, the source tool, or
  GitHub.
- **`handle-remarks`** — empties the other inbox: the private reading notes a person
  left on a repository's Devbook chapters in the app. It reads them over MCP
  (`list_annotations`), writes each answer into the chapter as a devbook `annotation` fence
  through the devbook plugin's own `annotations.mjs`, and only then resolves the note
  (`resolve_annotation`). Fence first, resolve second, because that is the order whose
  half-done state is recoverable. It writes nothing for an archived repository, whose devbook
  is read-only (`list_annotations` answers `archived: true`, local ADR 0029 §4), and neither
  does `run-plan-item`'s short form. The two
  annotation kinds stay two things —
  `.devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md` §6 is the decision,
  and the app is never the fence's writer. `run-plan-item` carries a short form of
  the same procedure for the notes an item it just ran leaves answerable.
- **`dispatch-sessions`** — this machine's Manager session, the first dispatcher of local
  ADR 0038 (`.devbook/arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md`).
  A person starts it once as `/loop /backlog-tools:dispatch-sessions` in a pinned Claude
  Desktop session. A Monitor runs `skills/dispatch-sessions/assets/wait-requests.mjs`, which
  finds the app's port and token the way the telemetry forwarder does, loops on
  `GET /session-requests/wait` and prints each request once, so the Manager spends no turn
  while nothing is queued; it never polls the request tools in a loop. Before each start it
  checks capacity, cheapest first: fewer than 3 of its own linked sessions working, every plan
  limit under 80%, and — through the PowerShell probe `assets/capacity.ps1` — average CPU
  under 80% over 30 seconds and at least 4 GB free; all four are arguments with these
  defaults, and a request that does not fit stays Requested. Oldest first, it claims the
  request (`claim_session_request`), starts a local session in the request's clone folder in
  its own worktree with the app's fixed brief as its whole prompt and `wake_when_done` on, and
  reports it (`mark_session_request` `started`), or gives the claim back (`released`) when
  the start fails. A new request wakes it through the Monitor and a finished session through
  `wake_when_done`; the `/loop` tick is the fallback only, 300 seconds while usage, CPU or
  memory holds the queue and 1800 otherwise. On restart it releases the claims its dispatcher
  still holds. The dispatcher is named for the machine, and the skill runs one Manager per
  machine. A request released after a failed start is skipped for that turn and comes up again
  on the next tick or re-armed Monitor. It writes no brief — the app builds each from the request's kind and id — so
  the session it starts goes through `run-plan-item` or `execute-plan` and the repository's
  gate like any pasted entry: it changes no file, is no flow, and adds no execution path beside
  the gate. It needs the `session-requests` flag on in the app. User-invoked only
  (`disable-model-invocation: true`). `tests/dispatch-sessions.test.ts` drives the wait
  script's decisions under `claude plugin test`; the two processes themselves run under
  `node --test plugins/backlog-tools/tests/dispatch-sessions-assets.test.mjs`, since that
  sandbox spawns nothing.

The plan-item marker exists because a plan is written before the app has given its entries
an id, so it restates the `id:`, `+tag`, `repo:` and `after:` the metadata line carries. The
entry line carries what only the app knows — the stored id — and is what makes any copied
entry a runnable paste.

`hooks/hooks.json` adds a `UserPromptSubmit` hook (Claude Code) that notices either line in
a prompt and nudges the session to invoke `run-plan-item`, so triggering does not
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

### The plan-progress pane

`hooks/hooks.json` also names a hooks module, `hooks/register.tsx` — Claude Code's function
hooks, beside the command hooks above. It draws an `execute-plan` run in two places:

- **The band above the prompt**, while any entry is in flow: the plan's tag and done over
  total, then one row per entry running, at a gate or blocked — its delivery flow's stages
  (✓ done, ● in progress, ✗ blocked, ‖ parked, – skipped, ○ pending) and the stage it stands at, tagged
  `gate` or `blocked`. Entries done or manual fold into one `also:` line. A band another plugin
  draws there, such as `delivery-run-view`'s flow band, stays under it.
- **The Plan progress pane**, the band's **details** button or `/backlog-tools:plan-progress`:
  the whole plan as a matrix — every entry a row, every stage a column under its short name
  (`scp`, `imp`, `b&t`, `pv`, …) — with the gate's review link and each block's reason under it.
  A surface has no `parked` status, so an unattended entry reports the Personal Validation it
  moved onto its draft as `blocked`; the pane draws that as parked, and keeps ✗ for a block the
  run stopped at.

The status line carries the counts, and a toast says when an entry reaches its gate or blocks.

It writes nothing and calls nothing: it reads the calls the run already makes.

| What it shows | Read from |
| --- | --- |
| The entries and their statuses | `get_plan_items` and `transition`, on any MCP server |
| Which sub-agent runs which entry | The first line of `skills/execute-plan/assets/item-brief.md`, in the `Agent` call's prompt — change it and `parseBrief` in `hooks/parse.ts` with it |
| Each entry's stages | `start_run`, `update_stage` and `finish_run` on any delivery surface, from the sub-agent's loop or one it spawned. A call the surface answered with an error still counts, since the pane mirrors what the worker said; a call a function hook refused with `deny` never ran and does not |
| How the entry ended | The sub-agent's one-line return: `gate`, `pr <url>`, `blocked <stage>: …`, `done-before` |

The module's state is declared in `types/index.d.ts`. `claude plugin validate plugins/backlog-tools`
checks the module against it, and `claude plugin test plugins/backlog-tools` runs `tests/`,
which drive the hooks against the engine and a scripted run (`tests/demo.ts`), and the
`dispatch-sessions` wait script's decisions. Once Claude
Code has loaded the plugin from this checkout it lays its declarations in the ignored
`.claude-plugin/types/`, and `tsc -p plugins/backlog-tools` type-checks the module. Function hooks
are early access in Claude Code: a release can change the API, and the pane is what breaks.

The plugin ships one manifest, `.claude-plugin/plugin.json`, for Claude Code.

## Microsoft To Do export

Microsoft To Do has no export of its own. What a route has to deliver is a **stable id per
task**, because the import knows an item by `{tool}:{external_id}`. Without an id, every
re-import duplicates. The routes compared on 2026-09-26:

| Route | Stable id per task | Completed flag | List name | Who sees the data |
|---|---|---|---|---|
| **Microsoft Graph, read with Microsoft's `Microsoft.Graph.Authentication` PowerShell module** (chosen) | yes: the `todoTask` `id` | yes: `status` | yes: the enclosing list | only Microsoft and the person's machine; the scope is read-only `Tasks.Read` |
| Graph Explorer, copying responses by hand | yes | yes | yes, one list per query | only Microsoft. But each list and every `@odata.nextLink` page is a separate copy, which does not scale past a handful of tasks |
| `Microsoft-To-Do-Export` (open source CLI, daylamtayari) | its raw JSON is Graph's shape; its CSV and Todoist formats are not | yes, with `--completed` | yes | the person's machine, but it asks for a Graph Explorer token with `Tasks.ReadWrite` pasted into it |
| Microsoft To Do Exporter (hosted web app) | not documented | not documented | yes | a third-party site that reads every task with `Tasks.Read` |
| To Do Vo Do (browser app) | yes: its CSV carries `id` | yes | not documented for its CSV | a third-party app; by its own account the data goes between Microsoft and the browser only |
| Classic Outlook's CSV export (work accounts, which sync To Do into Outlook Tasks) | no | yes | the Outlook folder | only the person's machine; unavailable for personal accounts |

The Graph route wins. It is first-party, read-only, works for work, school, and personal
accounts, pages through every list, and keeps Graph's `id`. The steps and the script are in
the skill's own Inputs section, and they are what the plan's round-trip test runs. One
limitation applies to every route, because it comes from Graph itself: a task's `id`
changes when the task is moved to another list. Graph's immutable ids cover Outlook items
but not `todoTask`. So a task moved between two exports is imported a second time.

## Install

**Claude Code**, inside a session started at the repository root:

```
/plugin marketplace add .
/plugin install backlog-tools@jsdotnet-backlog
```
