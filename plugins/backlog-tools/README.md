# backlog-tools

Backlog-native tooling — skills specific to the Backlog product itself, as opposed to
general-purpose or devbook tooling — that works on every host, so it is the plugin both Claude
Desktop and herdr install. Twelve skills: one for each direction of a plan, one for checking a
plan once it has run, one for bringing a long-running session's work level with its base branch,
one for closing the entries whose pull request merged, one for bringing another tool's items into
the Inbox, one for the remarks a person leaves while reading, one that tends a person's Brain, one
that runs the weekly review, one that reports what the week's runs cost, one that levels a devbook
and its code in the direction Backlog keeps, and one that keeps a person's goals current.

What only works in Claude Desktop — `execute-plan`, `dispatch-sessions`, the scheduled-run report
hooks and the plan-progress pane — is in [`backlog-desktop`](../backlog-desktop/README.md); what only
works in herdr will be in [`backlog-herdr`](../backlog-herdr/README.md). The parts moved out are
the ones `grep -rlE "start_session|ccd_|send_message|wake_when_done|local_"` listed here, with what
they import. **Install** below says which to install where.

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
  (`disable-model-invocation: true`); it never talks to the Backlog app or GitHub, except to
  read a Brain project note given as its source (`#brain:projects/<note>.md`, local ADR 0053)
  through the knowledge read tool under `#brain` — the plan then names the note on its `plan`
  entry and its steps as a `devbook:#brain:` token, and Import never writes into the note.
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
  it has merged into the default branch or a plan integration branch — every call carrying the `repository` read off the
  git remote; without one it falls back to searching git and says the status has to be set
  by hand.
- **`final-check`** — the one check at the end of a plan, run in the session that wrote it
  with `import-plan`, since that session still holds the specification — the artifacts it
  published, a design canvas, the plan text. It brings the checkout
  up to the plan's landed tip — the base branch, or `origin/<integration>` while its closing
  pull request is open, or the top of a stack not yet merged — by fast-forward, or in a detached scratch worktree when the
  checkout has work of its own. Then it reviews each `prompt` entry against its instructions,
  checks and `Read first:` sections at that tip, with `file:line` for every verdict
  (*implemented*, *partial*, *missing*, *not landed*), looks across entries for duplicated
  work, leftovers and specification nobody covered, and runs the repository's build, tests and
  checks and reads the base branch's CI. It reports one table and asks which numbered gaps
  to fix; on a yes it fixes them in that same session as one run of the repository's gate, on a
  branch cut from the tip. User-invoked only; until that yes it edits and commits nothing, and it
  never transitions an entry.
- **`catch-up`** — brings a session that has already produced work level with its base branch,
  for the ask "update the branch and see if the artifacts need further updates". It fetches and
  merges the base — through the host's own sync-with-base tool in a worktree session, by
  fast-forward on a plain checkout with no commits of its own — and never switches a folder that
  has become the main checkout, which happens once the session's worktree is removed. Then it
  reads what landed since the session's old merge base — decision records, domain chapters, the
  code areas the outputs name, pull requests covering the same ground — and re-checks each output
  against it: a published artifact is read back by URL, edited where stale and republished to the
  same URL (also the way back to a source the scratch directory lost); an `import-plan` plan is
  updated against the current template with every `id:` and its `+tag` kept and its review view
  republished to the same link; the branch's diff is checked for duplicates of or contradictions
  with what landed; an open pull request for conflicts, checks and a description that no longer
  matches. It reports one table: what moved, the output it touches, what changed or why nothing
  did. Model- or user-invoked; it is no flow, and any repository change it makes runs through the
  repository's gate.
- **`close-merged`** — moves to Done every entry whose pull request has merged into the
  default branch or a plan integration branch: the merges `run-plan-item` and `execute-plan` did
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
  left on a repository's Devbook chapters in the app. It reads them over MCP, one chapter at a
  time (`devbook_list_notes` per address, `list_annotations` per chapter path). A
  default-branch note it answers with a reply through `devbook_post_note` (`replyTo`) and then
  resolves with `devbook_resolve_note`; a note those refuse — `unknown-note`, a branch-snapshot
  note, a Brain note — it answers in the session report and closes with `resolve_annotation`.
  Answer first, resolve second, because that is the order whose half-done state is
  recoverable. A chapter or code fix an answer needs goes through the delivery gate; the skill
  itself writes and commits nothing. It never writes a fence: devbook
  contract 38 retired the `annotation` fence, the checker rejects one, and a review note is a
  thread in Backlog's store (local ADRs 0044 and 0011). It writes nothing for an archived
  repository, whose devbook is read-only (`list_annotations` answers `archived: true`, local
  ADR 0029 §4), and neither does `run-plan-item`'s short form. `run-plan-item` carries a
  short form of the same procedure for the notes an item it just ran leaves answerable.
- **`tend-brain`** — tends a person's Brain, the personal notes folder of local ADR 0026
  (`.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`). Run
  from that folder as `/loop /backlog-tools:tend-brain` (or pass its path). It brings no
  layout of its own: every tick it reads the Brain's root rules — `AGENTS.md`, else
  `CLAUDE.md`, plus `CONVENTIONS.md` and the templates they point at. The person's words are
  never changed: each new input in the inbox gets a structured copy, filed where the rules
  put it with their metadata and a `source:` link back to the original, the words kept
  verbatim. The original is then stamped with the date — `filed: <ISO date>` in its metadata,
  unless the rules name a field or a filename form — and moved out of the inbox to the folder the
  rules keep for processed input, else `archive/processed/`. The folders and the `source:` links
  are its only state, so a tick over an unchanged folder changes nothing — and costs little: it
  re-reads the rules only when one changed, and stops after one line when the inbox is empty,
  no follow-up is open and the app's inbox holds nothing. With the `backlog` MCP server present it
  also triages the Backlog app's inbox (`list_inbox_items`, `read_inbox_item`). A follow-up first
  gets its `task` entry, or a `plan` when it has several steps — found by its title or created, and
  with two matches it waits, nothing written. Then every reference, capture or follow-up gets one
  file per item id where the rules file it, else `_inbox/` — where the next tick files it as input —
  with the item's words verbatim, its source URL, tags and attachments by path, `source:
  backlog-inbox <id>` and any task id in `tasks:`. An unclear item, or one whose attachments are
  not downloaded yet, stays in the app inbox, its reason reported. Only once its file exists is an
  item archived with `archive_inbox_item` — never deleted — and the report says, per item, where
  its file went, the task id it created, and whether it was archived or left waiting.
  A remark the person routes to the rules' Backlog follow-up heading, with
  no reference yet, becomes a `task` entry through the `backlog` MCP server — `find_item` by
  its title first, so a second tick files no duplicate — and the new id is reported for the
  person to fill in; the line itself is never edited. An input with more than one possible home, or whose filing needs a fact it does not
  hold (a customer, a date), is listed for the person, as is every OneDrive conflict copy or
  ` copy` duplicate. A copy, once written, is the person's too and is never rewritten,
  and a status such as a meeting's `processed` is reported
  as due rather than set. For continuous improvement it suggests, at most three a tick, the
  rules the inputs show a need for — as checkboxes in `AGENTS.suggestions.md`, unless the rules
  name another file. The person ticks one to adopt it into `AGENTS.md`, or strikes it through to
  reject it; neither comes back. It never edits the rule files, and writes no state file, index
  or log into the folder. Model-invoked as well — "tend my second brain" runs one tick — and no
  flow.
- **`weekly-review`** — the GTD weekly review of local ADR 0054 §8
  (`.devbook/arc42/adr/0054-clarify-outcomes-follow-gtd-and-each-item-has-one-home.md`), over the
  three homes: the Inbox, the Brain and the backlog. It reads them through the `backlog` MCP
  server — `list_inbox_items` (unprocessed and deferred), `list_entries`, `get_planned_work` and
  `get_plan_items`, `list_goals`, `list_brain_reviews`, and `read_knowledge_chapter` under
  `#brain` for each note's `tasks` and `roadmap` fields. The server says whether a Brain is set
  but never where it is, so the skill lists an area's notes from the folder given as
  `brain:<path>`, or from the working directory when it holds a `CONVENTIONS.md`. Without either,
  the note-listing lines say so, and the review dates still come from `list_brain_reviews`. It reports three sections. **Get clear** covers the
  open Inbox items, the notes in the Brain's inbox area, and each deferred item whose review day
  falls this week or that has none ("No review date: look at it again", as the app's review
  says). **Get current**
  covers each plan with open entries but no next action (an entry ready or in progress, not
  waiting, not marked blocked, every `after:` done), each roadmap plan with no entry at all, each `projects/` note no plan gathers, each
  finished plan's project note still to archive, each waiting-for past its follow-up date, each
  overdue due date, and each goal with open items whose forecast is at risk. **Get creative**
  covers the `someday` area, the notes whose `review` date has come (and a `review` value that is
  no date), and each `areas/` note with no linked entry created, started or completed in 30 days.
  Every line names the entry or note and the action to take. The review day is the Inbox
  answer's `deferredUntil`, the forecast the goals answer's `forecast` — the one the Goals view
  draws; `node --test plugins/backlog-tools/tests/weekly-review-fields.test.mjs` holds the skill to
  both. Model- and user-invoked. It writes nothing anywhere, so it is no flow.
- **`weekly-cost-analysis`** — reports what the delivery runs on this machine cost in tokens
  over a window — 7 days, or an ISO range — across every repository, grouped by repository
  first, with the changes that would save the most. It reads the `backlog` server's
  `list_run_usage`, which answers each run's measured totals and uncached totals, per stage,
  per sub-agent and per model, and never `get_run`, which leaves those figures out on purpose.
  It ranks on the uncached figure, never estimates a number, and ends on one message in the
  shape of `skills/weekly-cost-analysis/assets/cost-analysis-shape.md`, `summary` or `full`,
  with the top tips. Copied from `delivery-schedule`'s `schedule-weekly-cost-analysis`, which
  reads one delivery surface's runs and leaves that plugin in a later devbook release.
  Model-invoked; it writes nothing.
- **`devbook-sweep`** — levels a repository's devbook chapters and its code in the direction
  Backlog keeps for each sync unit: `pull` carries code into chapters, `push` builds agreed
  chapters. Copied from `delivery-schedule`'s `schedule-devbook-sweep` when devbook's branch-true
  decision took the `sync` field out of the chapter (`JSdotNet/devbook:.devbook/arc42/adr/chapter-schema.md#schedules-and-sync`),
  with its three workflow scripts, its `report.md`, and `devbook-sweep-contract.md` beside it.
  The direction is Backlog's direction map, set in the devbook view and read through the
  `backlog` MCP server's `list_sync_directions`; `units.mjs` still groups the chapters, and
  `assets/select-groups.mjs` joins the two deterministically, so the pickup never rests on a
  model's reading. `devbook:verify-change` still gives the verdicts, and `delivery-schedule`
  still supplies the report frame, the draft-pull-request contract, and the push direction's
  resolver. Every pull request it opens is a draft; it never sets a direction. Its test runs
  under `node --test plugins/backlog-tools/tests/devbook-sweep-select.test.mjs`. Not a flow:
  it is the target of the routine below, and run by hand the same way.
- **`track-goals`** — keeps a person's goals current, so the stand-up and the weekly update
  read correct facts and each goal says what to do next (local ADR 0035:
  `.devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md`). Run once ("update my
  goals") or as `/loop /backlog-tools:track-goals`. Each tick reads the last and next
  iteration, each goal's standing and the open impediments through `read_iterations`, and what
  each goal holds through `list_goals`. It finds the work no goal holds yet: tasks in progress
  (`list_entries`), pull requests updated since the last iteration began (`gh pr list`, each
  resolved to its entry with `find_item`), and roadmap plans with an open entry. Work a goal
  already counts — directly, or through a held plan's `+tag` or a held task's pull request — is
  skipped, and a task is never attached past its plan, which would move its count. A pull request
  not yet linked to an entry, whose branch or body names an entry id or plan tag a goal holds, is
  that held work: attaching it would count it twice, so the skill proposes linking it to that entry
  (`link_change`) instead. The skill attaches a task or plan itself only when it depends on at
  least one item and exactly one goal holds all of them. One with no dependencies, a title match,
  or an unlinked pull request that names nothing held is only a proposal. It also proposes raising an impediment for a held task blocked for 2 worked days
  or a held pull request whose checks have failed for a day, and resolving one that reads ready to
  resolve; both thresholds are arguments with these defaults. A proposal is applied only on the person's yes: in chat when they
  started the run, else through `ask` and the answer `get_answer` reads or `dispatch-sessions`
  delivers (local ADR 0041). Raising and resolving stay the person's move, as ADR 0035 sets. It
  reports per goal its standing, what finished in the last iteration, the next step and its open
  impediments, then what needs the person; `weekly` reads a week and words it for a stakeholder.
  It never transitions, tags or edits an entry, never deletes a goal, creates or edits one or links a
  pull request only on a yes, and keeps no state file: Backlog's store is its state. Model-invoked; it changes no file
  and is no flow. `tests/track-goals-tools.test.mjs` holds every tool it names to a tool the
  server has.

`import-plan`, `import-inbox`, `run-plan-item`, `final-check`, `catch-up`,
`close-merged`, `handle-remarks`, `tend-brain`, `weekly-review` and `track-goals` open what they hand back with
the `show-me` skill's orientation card when it has one, and read as before without it. `devbook-sweep`
runs with nobody present and `weekly-cost-analysis` keeps its fixed shape, so those two skip it.

`resources/standing-session.md` is how a skill a person keeps running under `/loop` stands out
as a long-running agent: on its first turn `dispatch-sessions` (`backlog-desktop`'s), `tend-brain` and `track-goals`
title their own Claude Desktop session with an icon and an ALL CAPS name (`📡 SESSION DISPATCHER`,
`🧠 BRAIN TENDER`, `🎯 GOAL TRACKER`), file it under the `Agents` sidebar section and pin it, skipping any step the
host lacks. Every tick of `dispatch-sessions` and `tend-brain` then opens with the `backlog` server's `check_in` (local ADR 0051 §3),
which passes the session's own id (`${CLAUDE_SESSION_ID}`), its state and its heartbeat, and
whose answer carries the person's last command: `pause` does no work and keeps checking in at
the heartbeat, `resume` carries on, and `stop` ends the loop and says so. A loop that cannot
check in — the `sessions` flag off, the app closed — carries on as before. `tend-brain` checks
in only under `/loop`, so a one-off tick never reads as a loop. `track-goals` does not check in
yet: `check_in` accepts no kind for it. `tests/loop-skills.test.mjs`
holds every `/loop`-started skill of this plugin and its siblings in `plugins/` to all of it, and to the 40-line body budget.

`resources/chat-reports.md` is the one statement of how short a skill's chat report is. Eleven
skills here and `backlog-desktop`'s `execute-plan` point to it from their report step — `track-goals` except for its `weekly` report, which a
stakeholder reads in full sentences; `devbook-sweep` and `backlog-desktop`'s `dispatch-sessions`
run with nobody present, so they do not.

`resources/repository-conventions.md` is the one statement of the branch and pull request names a
plan lands under — the keys of a repository's `.devbook/backlog-tools.json` — read by
`final-check`, `close-merged` and `backlog-desktop`'s `execute-plan` alike.

`resources/dispatch/assets/` holds the dispatcher of local ADR 0038 without its host. A
dispatcher acts on sessions only through the **session-host port**, `session-host-port.mjs`:
five operations — start a session in a new worktree with the fixed brief, report its id, list
the sessions, read one's state (working, waiting, idle) and deliver a message — that each host
implements as an adapter in its own plugin. `dispatcher-logic.mjs` holds the steps every adapter
runs the same way: releasing a restart's held claims, answer delivery (local ADR 0041 §3), the
claim and start of a request with the late-report rule, capacity against the check-in answer
with the fixers the app never counts, the queue in the person's order, and the mend of a bot's
red pull request (§6, `mend-logic.mjs`). The long poll is `wait-requests.mjs`, its decisions
`wait-logic.mjs`; `manager-call.mjs` is one Manager-grant tool call from a shell and
`capacity.ps1` the CPU and memory probe. `fake-session-host.mjs` is a host held in memory that
records each call; `tests/dispatcher.test.mjs` drives the steps against it, and
`tests/dispatch-assets.test.mjs` runs the scripts as processes. `backlog-desktop`'s
`dispatch-sessions` is the Claude Desktop adapter: it runs byte-identical copies of these files
from its own `assets/`, which its `tests/shared-copies.test.mjs` holds equal, so edit them here
and copy them there.

### The devbook sweep routine

Run the two sweeps as **one local routine per repository**, the way `delivery-schedule`
creates its routines (its `resources/schedule-catalog-contract.md`): a session the desktop
app's scheduler starts on this machine, in the repository's main checkout, with the plugins
installed here — never a cloud routine, which starts on a fresh clone without them. Nothing in
this plugin creates it; make it once per repository from Claude Code's Routines page, or by
asking a session in that checkout to call `create_scheduled_task` with:

| Field | Value |
| --- | --- |
| Name | `<owner>/<repo> · Devbook sweep` |
| Working folder | the repository's main checkout, never a worktree — a worktree goes with its session |
| Schedule | weekly, e.g. `0 4 * * 1` UTC, converted to this machine's timezone |
| Worktree option | on, so each run gets a worktree of its own |
| Tools to approve on the first run, fired by hand | `Bash`, `Read`, `Write`, `Edit`, `Glob`, `Grep`, `Skill`, `Workflow`, and the `backlog` MCP server |

The prompt is `delivery-schedule`'s `resources/schedule-preamble.md` with its placeholders
filled — `{{name}}` `devbook-sweep`, `{{title}}` `Devbook sweep`, `{{repo}}`, `{{base}}`, and
`{{checkout}}` the checkout's absolute path with forward slashes — a blank line, and:

```text
Run /backlog-tools:devbook-sweep with direction: pull for the repository {{repo}} on {{base}},
with maxResolve 3. Then run it again with direction: push and maxResolve 1, after the pull
sweep's pull requests are open, so a sync group that also reads code-ahead waits for them.
Every pull request is a draft, on its own branch under schedule/devbook-pull-sweep/<YYYY-MM-DD>/
or schedule/devbook-push-sweep/<YYYY-MM-DD>/. The report is both briefs, pull first, each
closed by its devbook-sync-report block.
```

The Backlog app must be running for the `backlog` MCP server to answer; a run that cannot
reach it stops in Phase 1 and says so, and levels nothing. Retire the catalog's
`devbook-pull-sweep` and `devbook-push-sweep` routines for that repository —
`delivery-schedule:update` with both left out of the selection — so a unit is never levelled
twice. `delivery-schedule`'s `devbook-verify` stays where it is and still reports drift on the
units `units.mjs` resolves to `report`; it does not read Backlog's map yet, so once a
repository's chapters state no `sync` it reports on every unit, the swept ones included. Read
its report beside the sweep's rather than as a second verdict.

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
`/telemetry` endpoint — beside `/mcp`, on the same port — and
the app attributes the tool calls, delegated agents, token usage and context gauge to the
delivery run that session is driving, which the Sessions pane shows under the run's row. Each
post carries a token for the app's **Telemetry** grant (`mcpServer.grants.telemetry` in its
`settings.json`, local ADR 0058), which `hooks/backlog-grant.mjs` asks for through the headers
helper the app installs, `<app data>\hooks\backlog-mcp-headers.mjs --grant <id>`; no token is
kept in a file. `BACKLOG_MCP_TOKEN`, the dev and CI escape hatch, is sent only where no
Telemetry grant is recorded, and `BACKLOG_MCP_PORT` overrides the port. The tool-call events
are matched to shell, edits, sub-agents, skills and MCP tools, the calls a run's figures are
read for, so a session is not paying a process spawn on every `Read` and `Grep`. The script
needs Node, drops the tool's output before posting, gives up after two seconds and exits 0 on
any error, so a closed app costs a session nothing but a refused connection.

`hooks/backlog-grant.mjs` and `tests/fake-backlog-app.mjs` have a byte-identical copy in
`backlog-desktop`, since an installed plugin reaches no sibling's files: edit them here and copy
them there, which `backlog-desktop/tests/shared-copies.test.mjs` holds. Its tests run with
`node --test plugins/backlog-tools/tests/*.test.mjs`.

The plugin ships both manifests: `.claude-plugin/plugin.json` for Claude Code, and
`.github/plugin/plugin.json` for GitHub Copilot, which lists the skills only — the hooks are
Claude Code's.

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

Three plugins, one marketplace (`jsdotnet-backlog`, `.claude-plugin/marketplace.json` at the
repository root). `backlog-tools` is the shared one; each host adds its own beside it:

| Host | Install |
| --- | --- |
| Claude Desktop | `backlog-tools` and `backlog-desktop` |
| herdr | `backlog-tools` and `backlog-herdr` |

**Claude Code**, inside a session started at the repository root:

```
/plugin marketplace add .
/plugin install backlog-tools@jsdotnet-backlog
```

then `/plugin install backlog-desktop@jsdotnet-backlog` in Claude Desktop, or
`/plugin install backlog-herdr@jsdotnet-backlog` under herdr. Both declare `backlog-tools` as a
dependency.
