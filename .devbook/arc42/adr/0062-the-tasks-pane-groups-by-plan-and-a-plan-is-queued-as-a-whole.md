# ADR 0062: The Tasks pane groups by plan, and a plan is queued only as a whole

```meta
date: 2026-11-25
related: [".devbook/domain/tasks/features.md#search-filter-and-organize", ".devbook/domain/tasks/features.md#plans-view", ".devbook/domain/tasks/features.md#my-day", ".devbook/domain/tasks/domain.md#my-day", ".devbook/domain/tasks/requirements.md#plans-view", ".devbook/domain/sessions/features.md#start-a-session-from-an-entry", ".devbook/domain/sessions/domain.md#session-request-queue", ".devbook/domain/roadmap/domain.md#roadmap-item", ".devbook/arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md", ".devbook/arc42/adr/0032-my-day-preferences-ride-the-task-feed.md", ".devbook/arc42/adr/0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md", ".devbook/arc42/adr/0043-a-surface-can-open-in-its-own-window.md"]
c4: [backlog.desktop.tasks]
c4-touches: [backlog.desktop.sessions, backlog.desktop.roadmap, backlog.mobile]
```

The Tasks pane opens on **Plans**: a rail of plans beside a column that shows one plan
by Now, Next and Later, with the session queue docked beside the list. **Flat** keeps
the unplanned entries. A plan is queued only as a whole, and each of its entries shows
the state the plan's run gives it. A plan can be put on My Day by a My Day date on its
Roadmap Item.

## Status

```meta
```

Proposed, 2026-11-25. Built on `plan/plan-first-tasks` by the entries of the plan
`+plan-first-tasks`; it moves on when that branch merges, as the plan's sign-off entry
decides.

A **local** decision, numbered in the local sequence. 0061 is taken by the agent change
journal on its plan branch. Every bare ADR number below means the local one.

The person settled the design on the boards of the design canvas "Plan-based task
list": *Tasks — by plan, with the queue docked*, *Row states — entry ↔ queue*, *Tasks —
flat list, queue docked*, *One plan — dependency waves* and *My Day — plans and tasks
for today*. Its three *Alt* boards are the rejected alternatives below. The My Day
board's *Suggested* group is not part of the decision: My Day shows no suggestions.

## Context

```meta
```

**The list is flat, and most of it is plans.** Of 258 open entries on the design's
sample, 86 belong to a plan. A plan shows only as a chip on the filter bar's tag strip,
so the person reads a plan's progress by pressing its chip and counting rows.

**A plan runs unattended, but its entries can each be queued.** `execute-plan` runs a
plan's prompt entries in nested sessions that ask nothing and land on the plan branch
(ADR 0038, `plugins/backlog-tools/skills/execute-plan/SKILL.md`). An entry of the plan
queued on its own starts a second session on the same work, outside the plan's order.

**The queue hides in a popover.** The session queue is a count on the filter bar that
opens a list (ADR 0038). What needs the person, a question or a validation, is one
click away and easy to miss.

**My Day holds tasks only.** A person who wants to work a plan today picks its entries
one by one (ADR 0032's rule is per task).

## Decision

```meta
```

### 1. A Plans / Flat switch, opening on Plans

```meta
```

The Tasks pane gains a **Plans / Flat** switch. It opens on Plans the first time and is
remembered per window, as the window's other view choices are (ADR 0043). The Board and
the Calendar are unchanged.

### 2. The Plans view

```meta
```

- **The plan rail** lists, top to bottom: My Day; Now across plans, the entries in a
  session in any plan; each plan with a thin progress line, `done/total` and its run
  state as text; and Unplanned with its count.
- **The main column** shows the selected plan expanded in three groups:
  - **Now**: its entries in a session, each with the session and its stage.
  - **Next**: its ready entries, queued with the plan when the plan is queued.
  - **Later**: one line for the waiting entries, naming what unlocks next, and one line
    for the done ones.
- The other plans follow, folded to one line each, then the unplanned entries.
- A plan here is a [Roadmap Tag](../../domain/roadmap/domain.md#roadmap-tag) the
  entries in view carry. Its run state comes from its session request and its plan
  session: Running, Queued with its place, Blocked, Draft, or how many entries are
  ready.

### 3. The Flat view lists unplanned work

```meta
```

Flat lists the entries in no plan, plus a plan entry the person put on My Day. The other
plan entries live under Plans only.

### 4. A plan is queued only as a whole

```meta
```

Each entry of a plan shows a **plan entry state**, derived from the entry, its plan's
request and run, and its pull request; the first that holds wins, top to bottom:

| State | When |
| --- | --- |
| Done | The entry is Done or Archived. |
| Landed | Its pull request merged into the plan branch. |
| Left for you | A `test` or `task` entry: the run never does it. |
| Held for you | The entry is blocked. `execute-plan` blocks an entry whose Read-first source it cannot read, so the run holds it. |
| In plan session | A plan session runs the entry. |
| Waiting | The entry is not ready, or waits on another open entry. |
| Queued with plan | The plan is queued and the entry is ready. |
| Ready | The plan is not queued and the entry is ready. |

An entry's own queue button is disabled on a plan entry, with a tooltip saying to queue
the plan. An unplanned prompt entry keeps its own queue button. Plan sessions run
unattended and ask nothing (ADR 0038).

### 5. Rows

```meta
```

In the Tasks pane only, a row shows its status as coloured text instead of a pill, and
ends in two small icon buttons: copy, shown on hover, and queue. Every other badge stays
as it is. The Board's cards keep their pill.

### 6. The session queue is a docked column

```meta
```

The session queue becomes a column docked beside the list, in this order:

- **Needs you**: a session's question, with Answer; a draft awaiting validation, with
  Dismiss and Validate.
- **Running**, with Cancel on hover.
- **Up next**, reordered with Move up and Move down and with Alt+arrows, as today.
- **Landed today**.
- **Pause**, and **Long-running agents**, as today.

At 48rem wide or narrower the column folds back into today's popover on the filter bar.
Selecting a queue item filters the list to it: an entry request to that entry, a plan
request to that plan.

### 7. One plan's waves view

```meta
```

Opening one plan shows its entries in columns by dependency depth, the first wave the
entries that wait on nothing in the plan. A **Waves / List** toggle switches to its rows.
A run line names the plan branch the run lands on.

### 8. A plan on My Day

```meta
```

A plan is put on My Day by a My Day date on its
[Roadmap Item](../../domain/roadmap/domain.md#roadmap-item). The date rides the roadmap
plan document that already rides the task feed (ADR 0018), so no new document kind is
needed. Whether the plan is in today's My Day follows exactly the task rule and the
existing Clear My Day overnight setting (ADR 0032). Putting a plan tag with no Roadmap
Item on My Day creates its item.

On the desktop, a plan in My Day shows its progress, its run state, and only the entries
left for you, held, or awaiting validation. The phone's Today tab shows the plan as one
line, without its entries. My Day shows no suggestions.

## Consequences

```meta
```

- **A plan's entries can no longer run out of order** from the Tasks pane: the only way
  to start one is the plan's run.
- **An entry that must run alone has to leave its plan** first.
- **The roadmap plan document grows one field per item,** the My Day date. A device on
  an older build keeps the field it does not read, since the document travels whole.
- **The phone reads the roadmap plan document** for the first time, for the plan's one
  line on Today.
- **The queue takes width from the list** above 48rem, which the open entry also needs.
- **The plan entry state is derived,** from the plan's request, its run and each entry's
  pull request; nothing new is stored on the entry.

## Rejected

```meta
```

- **Quiet groups, with the queue in the status bar.** The plans read as groups of the
  flat list, and the queue shrinks to a count again: what needs the person stays hidden.
- **A plans index that opens one plan at a time.** Seeing the other plans means leaving
  the one open, and the unplanned work has no home on the same screen.
- **An attention feed whose order is the queue.** It mixes plans and entries into one
  stream, and loses the plan's own progress.
- **Queuing a plan entry on its own.** It starts a second session on the plan's work,
  outside its order and off its branch.
- **Suggestions on My Day.** My Day is the person's choice for today.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | The design system sanctions no teal or amber plan-state ink, so the rail's Queued keeps secondary text and the lit sun keeps `color-primary`. Teal `#7FD1C1` is the Brain's and a repository's band 2 ([the Brain's accent](../../design/color-scheme.md#the-brains-accent)), and a Tasks row already wears both, the **◆ n note** badge and a band-2 repository edge, beside its plan state; a teal state would read as either, and a near-teal of its own is the borrowing that rule forbids. The palette's only amber ink is `color-primary`, since `color-warning` is a dark fill; the one other amber, `color-domain-core-line`, is the Domain view's and may share its value only where the two never show at once, and the Devbook pane shows beside Tasks. A third gold would not be told apart from those two. The sun is a toggle, a control that acts, so `color-primary` is its ink, as it is for the queued queue button. Left for the person: the plan sections' Queued, the waves view's Left for you and ready marks, and the quiet row's ready ink already spend `color-band-2`, which the Brain rule forbids and the task-rows guideline's *ready teal* allows. | Closes the review's ink gap: the colour rules decide it, not the boards. |
| 2026-11-25 | §4 lists the eight plan entry states `PlanEntryState` derives, Waiting, Ready and Done added, in the order the first that holds wins, and says Held for you reads the entry's blocked status. Status says the record is built on the plan branch and moves on when it merges. The docked queue's selection and Show all chip are written into the sessions chapters, not here. | The plan's review found these code-ahead of the record. |
| 2026-11-25 | Reviewed against the five boards and §1–§8: every building entry of `+plan-first-tasks` landed on `plan/plan-first-tasks` (#1997, #1998, #2000, #2002, #2004, #2006, #2012–#2016), and the mobile head built in CI. Six gaps the entries deferred are filed under the plan: the quiet row of §5 is not in the design guideline's task rows; the plan entry states Waiting, Ready and Done and the docked queue's Show all chip are code-ahead of the chapters; Now shows no stage, since nothing yet reports one for a plan session; a blocked plan's folded line names no impediment; the teal and amber ink the boards use has no sanctioned token, so Queued reads secondary and the lit sun reads primary; the plan-run states need a live pass with a dispatcher. This record stays Proposed until the plan branch merges. | Closes the plan's review before its sign-off. |
| 2026-11-25 | Checked end to end in the desktop and mobile web harnesses against the five boards, on an imported fixture of a queued plan, an unqueued plan and unplanned entries. Plans is remembered across reloads; the rail reads `Queued 5th`, then `Queued 3rd` after a move, and `n ready` or Draft otherwise; queuing the plan turned its ready prompts to Queued with plan, and Later named `#4 when #2 and #3 land`; a plan entry's own queue button is disabled with the plan tooltip; the docked queue paused and resumed, reordered by button and Alt+↑, cancelled, filtered the list to the entry or plan selected, and folded into the popover between 860 and 720 pixels of list; the waves view and its List toggle matched *One plan — dependency waves*; two plans put on My Day showed in Plans today with only the entries left for you, and on the phone's Today as one line each. Six mismatches are filed under `+plan-first-tasks`: a Done-status entry with no completion date counts open on the rail and blocks its dependents; the rail and the phone count waiting entries as ready; a waiting `test` sits in Next; Flat keeps the rail's plan or Unplanned selection; the rail recounts done/total under My Day and Now across plans; a plan entry's row outside Plans shows its status, not its plan state. Running, Blocked, In plan session, Held for you, Landed, Needs you and Landed today were not reached: no dispatcher ran a plan session. | §1–§8 were each built on their own; this checks them together against the design. |
| 2026-11-25 | Proposed: Plans / Flat switch, plan rail with Now / Next / Later, plans queued whole with a state per entry, status as text on the rows, the queue docked, a plan's waves view, and a plan on My Day through its Roadmap Item. | The plan-first design the person settled for `+plan-first-tasks`. |
