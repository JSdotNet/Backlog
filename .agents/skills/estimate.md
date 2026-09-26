---
name: estimate
description: "Estimate units of work in story points off the 1/2/3/5/8/13/21 scale, sized against this repository's own reference examples. Use when: estimating, story points, sizing work, 'how big is this', giving a plan entry or an issue its effort, filling a chapter's effort:, or a caller needs points it can divide by a measured pace."
goal: "Return a story-point estimate off the 1/2/3/5/8/13/21 scale for each unit of work, sized against this repository's reference examples rather than in isolation, and name the reference each estimate was compared with."
---

# Estimate Work

Size each unit against finished work, never on its own. The reference table below is
Backlog's own landed work; the scale and what comes back are fixed by the wrapper's goal.

Points size work — the amount, the uncertainty, the number of places touched — never time. A
caller divides them by a pace it measures, which only works while a 3 means the same thing in
every plan.

A **place** here is one of: a module layer (`Abstractions`, domain, `Infrastructure.*`, `UI`),
an app head (desktop, mobile, VS Code extension, sync service), a devbook chapter or ADR, a
plugin skill. A unit whose acceptance needs the harness running through Aspire, or a second
head, carries more than its diff suggests.

## Reference

| Points | Reference | Why it is this size |
| --- | --- | --- |
| 1 | [#631](https://github.com/JSdotNet/Backlog/pull/631) Fix flaky No plan tag filter test | One line in one test, the cause already known |
| 2 | [#599](https://github.com/JSdotNet/Backlog/pull/599) Bind Backlog as this repository's delivery tracker and MCP server | Configuration in three files, no code, one known pattern — estimated 2 and was |
| 3 | [#582](https://github.com/JSdotNet/Backlog/pull/582) Parse the plan-level entry kind in the entry text grammar | One place (the Tasks grammar) and its chapter, one decision, its unit tests — estimated 3 and was |
| 5 | [#635](https://github.com/JSdotNet/Backlog/pull/635) Widen the capture contract with body, tags, person and a client id | One contract carried through three places — the sync API, its client, the Inbox module — with tests at each and two chapters — estimated 5 and was |
| 8 | [#589](https://github.com/JSdotNet/Backlog/pull/589) Give Roadmap its own import command for plan-level entries | A new end-to-end path through the Roadmap module and SQLite persistence, following the Tasks import, with its chapters — estimated 8 and was |
| 13 | [#665](https://github.com/JSdotNet/Backlog/pull/665) Show My Day on the phone's Tasks tab, and let it add a task for today | A new reading and writing view on the phone head, where layout and input carry real unknowns, with a test for each part — estimated 13 and was |
| 21 | [#578](https://github.com/JSdotNet/Backlog/pull/578) Serve Backlog's read-only tools over MCP from inside the running app | A new host inside the app, a new protocol dependency, its own ADR — the largest unit finished in one piece; it was estimated 8 |

Known drift, from estimates made before this table existed: these were sized too small, and
are not references. [#603](https://github.com/JSdotNet/Backlog/pull/603) (Register Backlog's own
MCP server from the Tools pane) and [#600](https://github.com/JSdotNet/Backlog/pull/600)
(Forward hook telemetry to the delivery run) were estimated 3 and landed at the size of the 8
row; [#578](https://github.com/JSdotNet/Backlog/pull/578) was estimated 8. The common cause: a
new dependency or a new host counted as one place.

## Compare

1. Read the unit as it is written — its instructions, its scope, its criteria. Do not size
   what it might grow into.
2. Find the one reference closest in kind and reach. Size up when the unit touches more places
   or carries an unknown the reference did not; down when it touches fewer.
3. Stay on the scale. Never 4, never 40. Past 21, say the unit should be split and name the
   seams rather than returning a number.
4. When two units in one call look alike, give them the same value; when one clearly exceeds
   the other, they differ. The table decides, not the order they were asked in.

## Calibrate

When a finished unit's actual size clearly differs from its estimate — reviewers call a 3 an
8 — replace the row it was compared with by finished work that really is that size. Keep one
row per value, all of it landed work. Drift in the table is drift in every pace computed
from it.

## Return

Per unit: the points, the reference row it was compared with, and a one-line reason naming
what made it larger or smaller. A unit too vague to size is returned unsized with what is
missing, never guessed.
