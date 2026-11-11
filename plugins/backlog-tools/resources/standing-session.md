---
name: standing-session
description: How a backlog-tools skill a person keeps running under /loop behaves as a long-running agent — it marks its Claude Desktop session on its first turn (an icon and an ALL CAPS title, pinned, filed under the Agents section) so it stands out from task sessions, and checks in with Backlog on every tick and obeys the pause, resume or stop the answer carries.
---

# Standing session

A skill a person starts as `/loop /backlog-tools:<name>` is a long-running agent, not a task.

| Skill | Title | Kind |
| --- | --- | --- |
| `dispatch-sessions` | `📡 SESSION DISPATCHER` | `dispatcher` |
| `tend-brain` | `🧠 BRAIN TENDER` | `brain-tender` |

## Mark

On its first unpaused turn in a session — never on a later tick — it marks that session so the sidebar
sets it apart:

1. **Title** it `<icon> <NAME>`, its row's title, with the app's session-title tool on `self`.
   The title already reads that → skip.
2. **File** it under the sidebar section `Agents`: take it from the app's section list, else
   create it, then move `self` there.
3. **Pin** it with the app's pin tool on `self` — after filing, since a move unpins.

- A tool the host lacks, or one the person declines → skip that step, say so once, go on.
  Marking never holds up the tick's own work.
- Never mark a session the skill did not run in, and never mark it again once the person
  retitled, unpinned or moved it: that is their answer.

## Check in

Every tick opens with the `backlog` server's `check_in` (local ADR 0051 §3), before any other
work: `kind` its row's kind, `sessionId` the id its skill passes, `state` `listening` unless its
skill says otherwise, `heartbeatMinutes` the `/loop` interval the person gave, else its skill's
fallback pace. The answer's `command` decides the tick:

- **`stop`** → do no work. End the loop: stop every Monitor it armed, cancel the `/loop`'s
  recurring schedule when it has one, schedule no further tick, and say so in one line —
  Backlog asked it to stop. A stop holds, in this session, until the person resumes the loop.
- **`pause`** → do no work: no claim, release, start, delivery, marking or filing. Say so once,
  on the first paused tick, then end the turn and come back at the heartbeat, so the check-ins
  go on. A first-turn step a paused turn skipped waits for the first unpaused one.
- **`resume`**, or none → carry on with the tick. After a pause, say it resumed, once.
- No `check_in` tool, or the call fails → carry on as before, say so once. A loop never stops
  or pauses on a check-in it could not make.

Backlog never stops the session itself; the loop obeys its answer.
