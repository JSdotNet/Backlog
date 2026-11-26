# backlog-desktop

The Claude Desktop half of the Backlog tooling: the parts of what was one `backlog-tools` plugin
that only work in Claude Desktop, because they start, message or wake its sessions, read its
`local_…` session ids, or report its scheduled runs. Install it in Claude Desktop beside
[`backlog-tools`](../backlog-tools/README.md), the shared plugin, which it declares as a
dependency; herdr installs [`backlog-herdr`](../backlog-herdr/README.md) instead.

They were found with

```sh
grep -rlE "start_session|ccd_|send_message|wake_when_done|local_" plugins/backlog-tools
```

and moved with what they import: two skills, the scheduled-run report hooks and the plan-progress
pane, with their tests. Every skill keeps its name and behaviour; only its plugin prefix changed,
so it is started as `/backlog-desktop:<name>`, and the Backlog app's own briefs and loop commands
say so.

A skill here names a file of the shared plugin as `backlog-tools/<path>` —
`resources/standing-session.md`, `resources/chat-reports.md`,
`resources/repository-conventions.md`, `skills/import-plan/assets/backlog-import-grammar.md`:
`../../../backlog-tools/` in this repository's checkout, the installed `backlog-tools` version
folder otherwise. `hooks/backlog-grant.mjs` and `tests/fake-backlog-app.mjs` are byte-identical
copies of `backlog-tools`' own, held so by `tests/shared-copies.test.mjs`.

- **`execute-plan`** — runs a whole plan, after the pattern of Matt Pocock's
  `implement-spec`. It reads the plan from the `backlog` MCP server by its `+tag`
  (`get_plan_items`) or from a file `import-plan` wrote, builds the graph its `after:`
  tokens draw, and changes nothing itself: each `prompt` entry whose prerequisites are done
  goes to its own nested session, started with `start_session` in a worktree the host makes and
  briefed by `skills/execute-plan/assets/item-brief.md`, which switches it to the entry's
  branch and runs the entry through `run-plan-item` — so every
  entry passes the repository's gate and its own Personal Validation. Claude Desktop starts a
  session only on a turn the person typed or one chained to it by sessions finishing, so a
  start it refuses — on a wake-up, or in a session another started — runs the same brief as a
  background sub-agent in its own worktree that parks at its gate. Every check watch runs in the
  foreground to keep the chain. Entries that wait on
  nothing run in parallel. It always runs unattended and asks the person nothing. Before it
  starts anything it opens every entry's `Read first:` references and linked Devbook chapters,
  and holds each entry that names one it cannot reach — blocked, with a `source not reachable`
  comment — until a later invocation finds the source again.
  An entry is done when its pull request merges — into `<integration>` in
  branch mode, into the base branch otherwise; the orchestrator
  then moves it to Done and starts what waited on it. It stops at `task` and `test` entries,
  listing what they hold up, starts the application in its own worktree for the first one
  that validates landed work, and resumes from the plan's state when invoked again. Two
  landing modes, defined in `skills/execute-plan/assets/landing-modes.md`: *branch*, the
  default, each into one integration branch, by default `plan/<tag without its +>`, that the
  orchestrator merges into, ending in a single pull request to the base branch; and
  *stacked*, each cut from and targeting its predecessor's branch, so a chain runs before
  anything merges and the person merges bottom-up. Each entry parks at its gate and lands as a
  draft pull request whose body is the review handoff, so the whole plan runs without anyone
  present and Personal Validation happens on the drafts — in branch mode on the closing draft
  to the base branch. A failed entry blocks only what depends on
  it. It titles its own session `<tag>:execute-plan` and each entry session
  `<n> - <Title>` — the plan tag is already in the parent's name — and a ready entry in
  another repository is handed to a new session in that repository's checkout running the same plan, per
  `skills/execute-plan/assets/handoff.md`. Branch names and pull request titles follow the
  repository's `.devbook/backlog-tools.json`, with an optional ticket the plan supplies and no
  tracker dependency (**Repository conventions** in `landing-modes.md`). The app itself moves an
  entry to Done on a merge only into the default branch or `plan/*` (`PullRequestLanding`); under
  another integration pattern `execute-plan` and `close-merged` do it. User-invoked only. `backlog-tools`' `UserPromptSubmit` hook
  stays quiet on a prompt that names this skill, so a pasted plan is not mistaken for one item.
- **`dispatch-sessions`** — this machine's Manager session, the first dispatcher of local
  ADR 0038 (`.devbook/arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md`).
  A person starts it once as `/loop /backlog-desktop:dispatch-sessions` in a pinned Claude
  Desktop session. It works under the app's **Manager** grant (`mcpServer.grants.manager`,
  local ADR 0058), the only grant holding `sessions:dispatch`: the six request and delivery
  tools go through `assets/manager-call.mjs`, one JSON-RPC call to the app's `/mcp` each, not
  through the `backlog` server, whose delivery grant the app refuses them. A Monitor runs
  `skills/dispatch-sessions/assets/wait-requests.mjs`, which takes a Manager token per call
  the way the telemetry forwarder takes its own (`hooks/backlog-grant.mjs`), loops on
  `GET /session-requests/wait` — naming the Manager's own session as `sessionId`, so each wait
  counts as its loop check-in (local ADR 0051 §3) — and prints each item once — an answer delivery (`delivery`,
  local ADR 0041) before any session request (`request`) — so the Manager spends no turn
  while nothing is queued; it never polls the request tools in a loop. It delivers first: for
  each pending delivery whose session the Claude desktop app holds open it claims it
  (`claim_delivery`), sends "You asked: <question>. The answer from Backlog: <answer>" to the
  session with `send_message`, which wakes an idle session, and marks it `delivered`, or
  `released` when the session no longer exists. Its four limits — fewer than 3 of its own linked
  sessions working, every plan limit under 80%, average CPU under 80% and at least 4 GB free —
  are arguments with these defaults, and each check-in sends them to the app as `maxSessions`,
  `maxUsagePercent`, `maxCpuPercent` and `minFreeMemoryGb`. Before each start it reads room from
  that check-in's answer: `capacity.hasRoom` against those limits, with a start made since the
  answer counted as one more working session. Only when the answer carries no `capacity`, or its
  `capacity` lacks a figure — absent, since the server omits a null property, or `null` — does it
  measure that figure itself before each start, cheapest first: its own linked sessions
  through `list_sessions` — a session the `backlog` server's `list_sessions` reads `Waiting` on a
  question does not count — plan usage through `get_usage`, and CPU over 30 seconds and free
  memory through the PowerShell probe `assets/capacity.ps1`. A request that does not fit stays
  Requested. In the order the person set in the session queue, it claims the
  request (`claim_session_request`), starts a local session in the request's clone folder in
  its own worktree with the app's fixed brief as its whole prompt and `wake_when_done` on, and
  reports it (`mark_session_request` `started`), or gives the claim back (`released`) when
  the start fails. Claude Desktop starts a session only on a turn the person typed, and
  refuses one on a `/loop` line, a Monitor event or a wake-up. On those turns the Manager
  claims nothing, since a claim's lease is two minutes. It proposes each fitting request as a
  New task card instead, at most three a turn and never twice, and records the card in a
  scratch file. When a card's session appears among its linked sessions, it claims the
  request and reports that session `started`. A card whose request left the queue is
  dismissed. A turn the person typed claims and starts a carded request by its proposal.
  Fixers are carded the same way. A new request wakes it through the Monitor and a finished session through
  `wake_when_done`; the `/loop` tick is the fallback only, 300 seconds while usage, CPU or
  memory holds the queue and 1800 otherwise. On restart it releases the request and delivery
  claims its dispatcher still holds. The dispatcher is named for the machine, and the skill runs one Manager per
  machine. A request released after a failed start is skipped for that turn and comes up again
  on the next tick or re-armed Monitor. With the queue empty and room left it **mends** (local
  ADR 0038 §6): for each `mend` clone folder — this session's checkout by default, `off` for none —
  `assets/red-bot-prs.mjs` lists the open pull requests a bot opened on a branch of the repository
  itself — never a fork's — whose checks all settled with one or more failed, skipping one an open session is bound to, one a fixer titled
  `CI fix <owner/name>#<number>` still holds, and one that had two fixers; the Manager starts a
  fixer for each with the fixed brief the script prints, built from the repository, number and
  head commit alone, so no pull request text reaches it. The fixer fixes on the pull request's own
  branch through the gate, binds it with Auto-fix on, pushes and stops at Personal Validation, and
  never merges. The app counts no fixer, so the Manager adds its running ones to the session count; `mend-logic.mjs` holds the
  decisions the assets test drives. It writes no brief — the app builds each from the request's kind and id — so
  the session it starts goes through `run-plan-item` or `execute-plan` and the repository's
  gate like any pasted entry: it changes no file, is no flow, and adds no execution path beside
  the gate. It needs the `session-requests` flag on in the app. Started by the person alone,
  yet model-invocable: `/loop` runs its inner command through the Skill tool, which refuses a
  `disable-model-invocation` skill, so its description confines invocation to that start and
  `tests/loop-skills.test.mjs` holds every `/loop`-started skill to it. It is the **Claude
  Desktop adapter** of the session-host port (local ADR 0038 §4): `assets/claude-desktop-host.mjs`
  writes each operation down as the call the skill makes — Start is `start_session`, Report id
  the id it answers, List Claude Desktop's `list_sessions`, Read state `backlog`'s `list_sessions`
  `Waiting` else `isRunning`, Deliver `send_message` — and `tests/claude-desktop-host.test.mjs`
  holds those calls to the arguments the skill sends, run through the shared `dispatcher-logic.mjs`.
  Every other file in `assets/` is a byte-identical copy of `backlog-tools`'
  `resources/dispatch/assets/`, the host-neutral dispatcher (its README says what each holds),
  which `tests/shared-copies.test.mjs` holds equal: edit them there and copy them here.
  `tests/dispatch-sessions.test.ts` drives the wait script's decisions under `claude plugin test`;
  the processes themselves — the wait script, `manager-call.mjs` and the capacity probe — run under
  `node --test plugins/backlog-tools/tests/dispatch-assets.test.mjs`, since that sandbox spawns nothing.

## Scheduled run reports

`hooks/schedule-run-report.mjs` reports each run a Claude Desktop schedule starts to the app's
Schedules page (local ADR 0051 §1). Such a run's first prompt opens with
`<scheduled-task name="<taskId>" …>`, and that task id is the schedule's id in Backlog.

- **SessionStart** (`startup`) reads that first prompt from the transcript — Claude Desktop
  writes it there before the hook fires — and adds context telling the session, before any other
  work, to look its entry up once with the scheduler tool (`list_scheduled_tasks`) and call
  `report_schedule_run` with event `started`, its title, cron and repository, and the session id
  the hook names. A `UserPromptSubmit` hook does the same for a transcript not yet on disk; the
  shell lets only a prompt carrying `<scheduled-task` through to Node, and a marker per session
  in the temp folder keeps the context to once.
- **Stop** posts the finish to `POST /schedule-runs` on the same port and token as `/telemetry`:
  `skipped`, with the line, when the final message opens with `Skipped:`, and the session id
  the line names as `heldBy`; `failed`, with the error after it, when it opens with `Failed:`;
  else `finished` with the report's bold verdict line (its first line of prose when it has
  none) and the rows under every *Needs you* heading, each as its cells joined by ` · `. The
  final message is the Stop payload's `last_assistant_message`, else the transcript's last
  assistant text.
- **StopFailure** fires instead of Stop when the turn ends on an API error — a rate limit, an
  overloaded or failing server, a billing or authentication error — and posts `failed` with the
  error line the session shows as the verdict.

`delivery-schedule`'s skills write no closing line of their own on failure, and its pickup gate's
`Skipped:` line names the earlier run only by its start time. So the start context also asks the
run to end a skip with `(session <session_id>)`, the holding run's id from the scheduler's run
list, and to end on a `Failed: <the error>` line when an error stops it. A run that does neither
still reports: a skip without `heldBy`, an error as its finish. The app resolves a `local_` id to
the session that run reported under.

The posts carry the MCP server's own token from `settings.json`. The question hook's token of its
own (plan `zero-trust-auth`) has not landed, so the route has no narrower token to take yet.

A session no schedule started reports nothing. Like the forwarder, the script gives up after
two seconds and exits 0 with nothing printed on any error, so a closed app, the `sessions` flag
off (`404`) or a refused report never fails or holds up the run. Its decisions live in
`hooks/schedule-run-logic.mjs`; `node --test plugins/backlog-desktop/tests/schedule-run-report.test.mjs`
drives them and the hook process against a stand-in route.

## The plan-progress pane

`hooks/hooks.json` names a hooks module, `hooks/register.tsx` — Claude Code's function
hooks, beside the schedule report's command hooks. It draws an `execute-plan` run in two places:

- **The band above the prompt**, while any entry is in flow: the plan's tag and done over
  total, then one row per entry running, at a gate or blocked — its delivery flow's stages
  (✓ done, ● in progress, ✗ blocked, ‖ parked, – skipped, ○ pending) and the stage it stands at, tagged
  `gate` or `blocked`. Entries done or manual fold into one `also:` line. A band another plugin
  draws there, such as `delivery-run-view`'s flow band, stays under it.
- **The Plan progress pane**, the band's **details** button or `/backlog-desktop:plan-progress`:
  the whole plan as a matrix — every entry a row, every stage a column under its short name
  (`scp`, `imp`, `b&t`, `pv`, …) — with the gate's run and worktree and each block's reason under it.
  A surface has no `parked` status, so an unattended entry reports the Personal Validation it
  moved onto its draft as `blocked`; the pane draws that as parked, and keeps ✗ for a block the
  run stopped at.

The status line carries the counts, and a toast says when an entry reaches its gate or blocks.

It writes nothing and calls nothing: it reads the calls the run already makes, so an entry
session resumed by message, with no `start_session` in this session, has no row until
`execute-plan` starts a new one for it.

| What it shows | Read from |
| --- | --- |
| The entries and their statuses | `get_plan_items` and `transition`, on any MCP server |
| Which session runs which entry | The first line of `skills/execute-plan/assets/item-brief.md`, in the `start_session` call's `prompt`, and the `local_…` session id its answer carries — or, for a fallback sub-agent, the spawn's `prompt` and the agent id it answered — change that line and `parseBrief` in `hooks/parse.ts` with it |
| Each entry session's worktree | The session's `cwd` in a `list_sessions` or `get_session` answer; a fallback's, the `worktree` its own calls name |
| Each entry's stages | `list_runs` and `get_run` answers on any delivery surface, matched to the entry session by its worktree |
| How the entry ended | The first line of the session's final message, in a `list_events` answer, or of a fallback's completed turn: `gate`, `pr <url>`, `blocked <stage>: …`, `done-before`. A `SendMessage` to it puts it back to running |

The module's state is declared in `types/index.d.ts`. `claude plugin validate plugins/backlog-desktop`
checks the module against it, and `claude plugin test plugins/backlog-desktop` runs `tests/`,
which drive the hooks against the engine and a scripted run (`tests/demo.ts`), and the
`dispatch-sessions` wait script's decisions. Once Claude
Code has loaded the plugin from this checkout it lays its declarations in the ignored
`.claude-plugin/types/`, and `tsc -p plugins/backlog-desktop` type-checks the module. Function hooks
are early access in Claude Code: a release can change the API, and the pane is what breaks.

Its `node --test` suites run with `node --test plugins/backlog-desktop/tests/*.test.mjs`.

The plugin ships both manifests: `.claude-plugin/plugin.json` for Claude Code, carrying the
module's `types` and the dependency on `backlog-tools`, and `.github/plugin/plugin.json` for
GitHub Copilot, which lists the skills only — the hooks and the module are Claude Code's.

## Install

In Claude Desktop, inside a session started at the repository root, after `backlog-tools`:

```
/plugin marketplace add .
/plugin install backlog-desktop@jsdotnet-backlog
```

Not under herdr: there, install `backlog-herdr`.
