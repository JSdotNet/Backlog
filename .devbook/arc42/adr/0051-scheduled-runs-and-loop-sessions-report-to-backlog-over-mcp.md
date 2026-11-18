# ADR 0051: Scheduled runs and loop sessions report to Backlog over MCP

```meta
date: 2026-11-18
related: [".devbook/domain/sessions/domain.md#weekly-usage", ".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md", ".devbook/arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md", ".devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md", ".devbook/arc42/adr/0041-agent-questions-reach-backlog-by-presence-and-come-back-through-the-hook-or-the-manager.md", ".devbook/domain/sessions/context.md#sessions-area", ".devbook/domain/sessions/domain.md#schedule-register", ".devbook/domain/sessions/domain.md#loop-register", ".devbook/domain/sessions/domain.md#session-title-reading", ".devbook/domain/sessions/features.md#schedules-and-their-runs", ".devbook/domain/sessions/features.md#loop-sessions", ".devbook/domain/sessions/requirements.md#schedules-and-their-runs", ".devbook/domain/sessions/requirements.md#loop-sessions", ".devbook/domain/notifications/features.md#asking-from-the-contexts", ".devbook/domain/notifications/requirements.md#asking-from-the-contexts"]
```

Each scheduled run tells Backlog that it started and how it finished, through MCP, from a
hook pair the `backlog-tools` plugin ships. Each loop session (the Manager of ADR 0038 and the
Brain tender) checks in with Backlog through a new MCP tool, `check_in`, on every tick. Backlog
decides a state for every schedule and every loop session from those reports, alerts the person
through the notification centre when one needs them, and shows both in the Sessions takeover.
All of it sits behind the existing `sessions` flag.

## Status

```meta
```

Accepted, 2026-11-11, when its pull request merged into the plan's integration branch. Drafted
by an unattended `execute-plan` run of the `+schedules-and-loops` plan, its `record-decision`
item, from the plan's settled decisions and its design canvas *Schedules Monitor*.

Built on that branch:

- the `report_schedule_run` tool and the `backlog-tools` hook pair, which also reports a failed
  run and the run a skip waits on;
- `check_in`, and the dispatch wait counted as a check-in;
- the run states and the loop states;
- the app's own capacity reading;
- the check-in answer's room against the loop's own limits, and the Manager reading it, as
  [Amends](#amends) says;
- the alerts, with their Open run, Open Schedules and Open Sessions actions;
- closing a Failed alert when a later run recovers, as §5 says;
- the Schedules page, the Agents section and the waiting row's question.

A **local** decision, numbered in the local sequence. Every bare ADR number below means the
local one. It adds tools to the listener ADR 0012 built and changes none of its decisions. It
amends ADR 0038's Manager capacity rule, as [Amends](#amends) says. The rest of ADRs 0038 and
0041 stands.

## Context

```meta
```

**Unattended work stops without anyone noticing.** A schedule fires a `schedule-*` skill on a
cadence, as a local routine on this machine. A run that needs the person, fails, or holds the
next runs back says so only in its own Claude Desktop session. A schedule that never fired says
nothing at all. The person finds out when they next open the sidebar and read each run.

**A loop session can stop without anyone noticing.** The Manager session of ADR 0038 and the
Brain tender run under `/loop` in a pinned session. When one stops or hangs, session requests
and answer deliveries wait in Backlog, and nothing tells the person why.

**Claude Desktop's schedule and run files are its own.** They are private, undocumented and
change between releases. A reader built on them breaks when they do.

**Schedule prompts belong to `delivery-schedule`.** Its `update` skill rewrites every prompt it
manages, so a reporting step written into a prompt would be lost on the next update.

**The Manager measures its own capacity today.** It reads plan usage, CPU and memory itself
before each start, so the app cannot show why the queue is not moving.

## Decision

```meta
```

```mermaid
sequenceDiagram
  participant Run as Scheduled run
  participant Hook as backlog-tools hooks
  participant B as Backlog
  participant Loop as Loop session
  participant P as Person
  Hook->>Run: SessionStart: report your start
  Run->>B: run started (schedule id, title, repository, cron)
  Hook->>B: Stop: run finished (verdict, Needs-you lines)
  Loop->>B: check_in(state, heartbeat, limits)
  B-->>Loop: pause, resume or stop, the work waiting, and the room
  B->>P: an Alert, in the centre and on Windows
```

### 1. Scheduled runs report to Backlog over MCP, from a `backlog-tools` hook pair

```meta
```

The app never reads Claude Desktop's private schedule or run files. Each run reports itself:

- **SessionStart** spots a session that a scheduled task started. Its hook has the session
  report its start, with the schedule's id, title, repository and cron, taken from the session's
  own scheduler tool.
- **Stop** reports the run's finish: its verdict and any Needs-you lines from its summary.

A schedule appears in Backlog after its first report. Until then Backlog does not know it
exists, so a schedule that has never run is not shown.

The hooks live in `backlog-tools` only. No schedule prompt carries a reporting step, so
`delivery-schedule:update` rewriting the prompts changes nothing. This differs from ADR 0041 §4
on purpose. The question relay must reach every session, so its hook ships with the app. A
schedule report concerns only scheduled `schedule-*` runs, which run where the person keeps
the stack's plugins.

### 2. Seven run states, read from the reports

```meta
```

| State | When | Alerts |
| --- | --- | --- |
| Needs you | The run reported Needs-you lines and its session is not archived. | Yes |
| Held | The run reported that it skipped itself because an earlier run is unarchived. | Yes |
| Failed | The run reported a failure. | Yes |
| Missed | Enabled, 30 minutes past its cron's due time, and no start report. | Yes |
| Review ready | The run reported work ready for review, and its session is not archived. | Row only |
| Handled | Its session is archived, or it failed and a later run finished Review ready. | Closes the alert |
| No summary yet | A start report and no finish report yet. | No |

**Archiving is read from the desktop record the Sessions reader already reads.** Archiving a
run's session in Claude Desktop is how the person marks it done. Backlog never archives a session.

**A failed run also reads Handled once its schedule recovers.** A later run of the same schedule
must finish Review ready: it began after the failed run and finished no earlier. The Failed alert
closes, but the failed run's session stays unarchived, so a run that skips itself for it still
reads Held.

A schedule's state is its latest run's. Held stays on the schedule while its later runs keep
skipping, so the person sees one Held schedule, not one alert per skipped run.

### 3. Loop sessions check in over MCP, and the answer carries what the app wants

```meta
```

A **loop session** is a session titled by `standing-session.md`: `📡 SESSION DISPATCHER` or
`🧠 BRAIN TENDER`. Its Session Purpose is the new **Agent** purpose.

A loop checks in two ways:

- **`check_in`**, a new MCP tool. The loop (`dispatch-sessions`, `tend-brain`) sends its state,
  its own heartbeat and its limits on every tick.
- **The dispatch wait.** A Manager's call to `GET /session-requests/wait` counts as a check-in
  too, because it reaches Backlog far more often than a tick.

The answer to `check_in` carries what the app wants from the loop: `pause`, `resume` or `stop`,
set by the person on the loop's detail, and the work waiting for it. The loop obeys the answer.
Backlog still never stops a session itself.

```mermaid
stateDiagram-v2
  direction LR
  [*] --> Listening: checks in
  Listening --> Holding: work waiting, a capacity limit reached
  Holding --> Listening: room again, or its check-in's heartbeat passed
  Listening --> Stalled: process alive, no check-in past heartbeat + 10 min
  Holding --> Stalled: process alive, no check-in past heartbeat + 10 min
  Stalled --> Listening: checks in again
  Listening --> NotRunning: no process, or session archived
  Holding --> NotRunning: no process, or session archived
  Stalled --> NotRunning: no process, or session archived
  NotRunning --> Listening: checks in again
```

- **Listening.** It is checking in, and nothing holds it.
- **Holding.** It is checking in, session requests are waiting, and a capacity limit is
  reached. A paused queue has no session request waiting. What a `check_in` says about holding stands only until that check-in's own heartbeat has
  passed: a dispatch wait moves the time of the check-in but measures nothing. Backlog's own
  reading at the check-in decides when it can: a limit is reached, or all four figures are
  measured. When the reading cannot decide, or a limit is reached and the work waiting cannot be
  read, the loop's own `holding` decides. The reading cannot decide when there is none, or when a figure is unavailable
  and no limit is reached. The Brain tender has no queue, so it never holds.
- **Stalled.** Its process is alive, but nothing checked in past its own heartbeat plus 10
  minutes.
- **Not running.** It has no process, or its session is archived.

A check-in or a dispatch wait keeps a loop off Stalled. Whether its process is alive is read by
the [Liveness Assessment](../../domain/sessions/domain.md#liveness-assessment) the Sessions
context already runs.

The goal tracker, `track-goals`, also runs under `/loop` but does not check in. It has no queue
and holds nothing back, and its proposals already reach the person as Backlog questions. A
stopped goal tracker therefore raises no alert.

### 4. The app measures capacity itself

```meta
```

Backlog measures four figures, and the loop's limits decide whether there is room:

- dispatched sessions working, with Waiting sessions not counted, as ADR 0041 has it;
- plan usage, read from the app's own
  [Weekly Usage](../../domain/sessions/domain.md#weekly-usage) estimate;
- CPU, its busy share over the last 30 seconds;
- free memory.

Plan usage is the estimate's share of the calibrated weekly limit. It reads unavailable until a
weekly refusal sizes that limit, and an unavailable figure holds nothing back. No Claude usage
cache holds plan percents the app could read instead.

A dispatcher's check-in answer carries the reading as `capacity`:

- `hasRoom`;
- `heldBy`, each limit reached, in the order `sessions`, `usage`, `cpu`, `memory`;
- `workingSessions`, `planUsagePercent`, `cpuPercent` and `freeMemoryGb`, each `null` where
  Backlog could not measure it;
- the limits it was held against.

The Brain tender's answer carries no `capacity`, and neither does the answer on a host that
measures nothing. Backlog keeps the reading with the check-in, so the loop's row reads Holding
from what the app measured. The reading counts every dispatched session, because one Manager runs
per machine. The loop's detail shows the four figures beside its limits, and a Holding row names
the limit that holds it. The person sees why the queue is not moving without opening the
Manager's session.

### 5. Alerts go through the existing notification centre

```meta
```

Backlog raises an **Alert** in the notification centre of ADR 0039, with its Windows
notification, for six causes: Needs you, Held, Failed, Missed, Stalled, and Not running while
work waits. Each cause alerts once, and Backlog closes the alert when the cause clears: the run's
session is archived, a missed schedule's run starts, or the loop checks in again.

A schedule's Failed alert also closes once a later run of the same schedule finishes Review
ready. The schedule has recovered, so the failure no longer needs the person. Archiving the
failed run's session still closes it too.

Review ready shows on the row only. No alert source of its own is built.

Work waits for a stopped Manager while session requests wait in a running queue, or answers wait
for delivery. A paused queue's requests do not count.

Neither register prunes its rows. The alert sweep reads sessions back only as far as an alert can
still open or close, so its read does not grow with them. The sweep remembers each run it has
seen archived until the app restarts, so unarchiving that run's session does not reopen its alert
before then.

### 6. The screens sit in the Sessions takeover, behind the `sessions` flag

```meta
```

- **Schedules** is a page inside the Sessions takeover, like Delivery economics. A header button
  reaches it and carries a badge counting the schedules that need the person.
- **An Agents section always leads the Sessions list**, above every grouping's sections.
- **A waiting row shows its question**, as "Asks", with an Answer link to the notification.

All of it sits behind the existing `sessions` flag, with no flag of its own. With `sessions` off,
the page, the section, the report tools and `check_in` are gone, and no alert is raised.

## Amends

```meta
```

This record amends ADR 0038 §4 without rewriting it. Its Manager reads the room from the
`check_in` answer, against the limits it sends:

| Argument | Default | Refused when |
| --- | --- | --- |
| `maxSessions` | 3 | below 1 |
| `maxUsagePercent` | 80% | 0 or below, or above 100 |
| `maxCpuPercent` | 80% | 0 or below, or above 100 |
| `minFreeMemoryGb` | 4 GB | negative |

A limit the Manager leaves out is Backlog's default, and a value out of range is refused with the
code `loop.limits`. The Manager counts each start made since the answer as one more working
session. It measures a figure itself only when the answer has no `capacity`, or the figure is
missing from it. How many sessions it runs at once stays the skill's own setting.

## Consequences

```meta
```

Positive:

- **Unattended work that needs the person reaches them.** A run that needs them, fails, holds
  the next runs back or never fires raises one alert.
- **A stopped Manager is seen.** Waiting requests no longer wait silently.
- **Nothing breaks when Claude Desktop changes its files.** The app reads only what runs report
  and the session record it already reads.
- **The schedule prompts stay `delivery-schedule`'s.** Its `update` can rewrite them freely.

Negative, and accepted:

- **A schedule that never ran is invisible.** It appears only after its first report.
- **A run with no `backlog-tools` reports nothing.** A machine without the plugin shows no
  schedules.
- **A schedule disabled after its last report reads Missed once.** No report says it was
  disabled, so its next due time passes without a start. Archiving the last run does not
  clear it.
- **The Manager depends on the app for capacity.** With Backlog closed it has no answer, so it
  starts nothing, which it could not do anyway without the wait route.
- **A stopped goal tracker is not seen.** It never checks in, so nothing reads it Stalled or Not
  running.

Neutral:

- **The Sessions context gains two registers.** Run reports and check-ins are evidence the
  sessions send about themselves, beside the records the agents leave.

## Rejected

```meta
```

- **Reading Claude Desktop's schedule and run files.** They are private and change between
  releases, so the reader would break silently.
- **A reporting step in each schedule prompt.** `delivery-schedule:update` rewrites the prompts,
  and the step would go with the next update.
- **The hook shipped by the app, as ADR 0041 does.** The report only concerns `schedule-*` runs,
  and the plugin that sits beside them updates with the stack they belong to.
- **Watching the loop's process alone.** A hung loop keeps its process, so only a check-in tells
  Listening from Stalled.
- **The Manager measuring capacity itself.** The app could not show why the queue is not moving.
- **An alert per skipped run.** A held weekly sweep would alert every week for the same cause.
- **A Failed alert that only archiving closes.** A schedule whose next run succeeded would still
  alert about a failure that no longer needs the person.
- **Plan usage from a Claude usage cache.** No such cache holds plan percents the app can read.
- **A third loop kind for the goal tracker.** It would hold nothing back and raise no alert the
  person needs, while every screen and alert would learn a third loop.
- **A flag of its own.** The screens belong to the Sessions area, and a second switch would only
  add a combination nobody wants.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-18 | Review fixes (plan entry 23): Holding lapses once the last `check_in`'s heartbeat passes, a paused queue has no request waiting, CPU is the last 30 seconds, and the alert sweep's read is bounded. The 2-minute Listening rule goes. §5 says what work waits for a stopped Manager, that neither register prunes, and that the sweep remembers archived runs until restart. §3 records why the goal tracker does not check in. §3, §4, §5, Consequences and Rejected say so. | A review of the closing pull request found a stale Holding, a paused queue read as work, a CPU window up to 30 minutes long, and a rule the code never applied. |
| 2026-11-18 | Built: `check_in` takes the loop's limits, and a dispatcher's answer carries the app's capacity reading. Holding reads from that reading, and from the loop's own word where the reading cannot decide (plan entry 15). A Failed alert closes once a later run finishes Review ready (plan entry 21). Status, §2, §3, §4 and Amends say so. | Both changes landed on the plan branch, and the record still listed them as not built. |
| 2026-11-17 | Status names what the plan built. §4 reads plan usage from the in-app Weekly Usage estimate. §5 closes a schedule's Failed alert once a later run finishes Review ready; plan entry 21 builds it. | The owner settled the plan's open questions, and the status still said nothing was built. |
| 2026-11-11 | Proposed: scheduled runs report through a `backlog-tools` hook pair, loop sessions check in through `check_in`, Backlog decides their states, measures capacity, alerts through the notification centre, and shows them in the Sessions takeover. Amends ADR 0038 §4's capacity. | Unattended runs and loop sessions stop without the person noticing, and Claude Desktop's own files are no contract. |
