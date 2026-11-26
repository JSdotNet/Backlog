# ADR 0073: The bounded contexts are redrawn

```meta
date: 2026-11-26
related: [".devbook/domain/context-map.md", ".devbook/arc42/adr/0072-the-tasks-pane-groups-by-plan-and-a-plan-is-queued-as-a-whole.md", ".devbook/arc42/adr/0018-roadmap-plan-and-pace-ride-the-task-feed.md", ".devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md", ".devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md", ".devbook/arc42/adr/0040-the-brain-is-its-own-bounded-context.md", ".devbook/arc42/adr/0050-events-is-its-own-bounded-context.md", ".devbook/arc42/adr/0054-clarify-outcomes-follow-gtd-and-each-item-has-one-home.md", ".devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md", ".devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md", ".devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md", ".devbook/arc42/adr/0059-tokenless-mcp-calls-get-the-scopes-the-person-opens-to-them.md", ".devbook/arc42/adr/0067-each-module-ships-its-own-ui-project-and-the-apps-compose-them.md", ".devbook/arc42/adr/0069-the-backlog-mcp-server-keeps-grants-and-serves-its-own-skill.md", ".devbook/arc42/adr/0071-agent-changes-are-restorable-through-a-change-journal-beside-the-store.md"]
c4: [backlog.desktop.tasks, backlog.desktop.roadmap, backlog.desktop.insights, backlog.desktop.repositoryManagement, backlog.desktop.progress, backlog.desktop.devPc]
c4-touches: [backlog.desktop.brain, backlog.desktop.devbook, backlog.desktop.events, backlog.desktop.calendar, backlog.desktop.shell, backlog.desktop.mcp, backlog.desktop.sync, backlog.desktop.notifications, backlog.taskStore, backlog.mobile]
```

Eighteen bounded contexts become thirteen, with two compositions and one channel beside
them. Tasks and Roadmap Planning merge into **Work**; Monitoring & Dashboard merges into
Productivity as **Insights**; Environment folds into Repository Management as
**Repositories**; Progress is renamed **Goals**; Technology Stack is dissolved. Sync,
Notifications and the change journal are generic, the Calendar and the desktop shell are
compositions, and the MCP server is a channel. Every stored value that carries an old name
is renamed with a migration and a compatibility read.

## Status

```meta
```

Proposed, 2026-11-26. Built by the entries of the plan `+boundary-redesign` on
`plan/boundary-redesign`; each later entry implements one section below and cites it.

A **local** decision, numbered in the local sequence. Every bare ADR number below means
the local one.

The person settled every section on the boundary review of 26 November 2026, read from
`main` at `0294356` with PR #2018 (ADR 0072) and PR #2051 merged. Where this record
departs from the review's own proposal, *Rejected* says so.

## Context

```meta
```

Code facts below are reference counts in `src/`, not runtime checks.

**Tasks and Roadmap Planning already act as one context.** They are the map's only
Partnership pair besides Tasks–Brain, with ids crossing both ways. Roadmap defines the plan
tag; Tasks offers it in its picker. Seven adapters in `Backlog.Infrastructure.FileSystem`
answer Roadmap ports over Tasks (`ImportedPlanSource`, `RoadmapPlanIntake`,
`RoadmapCompletedWork`, `RoadmapWorkChanges` and three more), and one answers Tasks'
`IRoadmapTagSource` from Roadmap. In Tasks, 42 of 141 files mention the roadmap; in
Roadmap, 16 of 83 use task ids or task types. The plan is a row in the same SQLite database
as the tasks, and it and the pace ride the `/tasks` feed (ADR 0018). Every consumer reads
both: Progress (task by id, plan by tag), the dashboard's plan progress, and the Calendar,
where each answers one layer port (ADR 0056). ADR 0072 then stores a plan's My Day date on
Roadmap's item while the My Day rule that reads it stays in Tasks: one rule across two
contexts.

**Capacity has three owners.** The working week is set in Productivity, travels inside
Roadmap's `planning-pace` document, and lives in the shared kernel as `WorkingHours`.

**Monitoring & Dashboard has no code of its own.** Productivity's code lives in
`Backlog.Modules.Dashboard`. Monitoring's chapter is 22 lines describing a standalone team
service; `FollowUpCaptured`, `ProductivityRecorded` and `MachineStatusChanged` have no
reference in `src/`. Productivity → Monitoring → Productivity is a loop between one built
context and one unbuilt one, and *Dashboard* names both a module and a different context.

**Environment has no code.** Its context chapter is 21 lines; `EnvironmentShortcutUsed`
and the shortcut types have no reference in `src/`. It already depends on the repository
registry, and its destinations (the local harness, staging, production, a repository's
dashboards) are facts about a registered repository.

**Progress is named for something else.** *Progress* collides with Monitoring's *Progress
Signal*. Its settings keys already say goals (`goals.iteration-length`,
`goals.iteration-anchor`), and the weekly review lives in it (ADR 0054 §8).

**Technology Stack duplicates the devbook on paper.** It has no module;
`BaselineProvided` and `TechnologyBaseline` have no reference in `src/`, and none of its
four edges is built. A repository's technology graph and its ratings are the devbook
`tech/` folder, drawn by 15 `Technology*` files in `Backlog.Modules.Devbook.UI`. Devbook's
reader already reads every registered repository's devbook for the Brain's Lobes atlas.

**Some rows on the map are not contexts.** Sync's own chapter says it is not what sets the
product apart, and Notifications reads none of its publishers' data (ADR 0039). The change
journal records changes across tasks, plans, goals, inbox triage, events and devbook state
(ADR 0071). The Calendar owns its grid and reads layers (ADR 0055, 0056), like the desktop
shell. The MCP server owns no data, passes each tool group through to one context's
Abstractions (ADR 0012), and keeps the agent grants and scopes in `SharedKernel.Mcp`
(ADR 0058, 0059, 0069); the map never mentions it.

**A Machine is modelled three times:** `DashboardMachine` in Dashboard, machine-name strings
in Dev PC Management, and the device identity in Sync. Dev PC Management owns no machine
type.

**A Talk note keeps an Inbox item alive.** An event holds each Talk note by its Inbox note
id (ADR 0050), while ADR 0054 says every Inbox item ends in one home. The link is also
missing from the map.

**Brain's screens reach into Devbook's.** `Backlog.Modules.Brain.UI` references
`Backlog.Modules.Devbook.UI`, the one module-to-module reference that skips Abstractions,
which ADR 0067's rule that each module ships its own UI breaks.

**The map misleads.** About a dozen edges touching Technology Stack, Environment and
Monitoring are drawn solid with no code behind them, while Sessions' unbuilt edges are
dashed. Edges run from supplier to consumer, but no legend says so, and `Notify → Devbook`
reads as Notifications depending on Devbook.

## Decision

```meta
```

### 1. Work

```meta
```

Tasks and Roadmap Planning merge into one core context, **Work**, with **Task** and
**Plan** as separate aggregates. The split of *priority* stays as an invariant inside it: a
task owns its execution priority, a plan its planning priority and sequence, and neither
writes or derives the other's value. Capacity — pace, the working week and day overrides —
moves into Work from Productivity. ADR 0072's split of My Day between the plan item and
the task rule ends inside Work. The modules merge into `Backlog.Modules.Work`, with
`Backlog.Modules.Work.Abstractions` and `Backlog.Modules.Work.UI`.

### 2. Insights

```meta
```

Monitoring & Dashboard merges into Productivity, and the result is renamed **Insights**. It
reads Work, Sessions, Repositories and Dev PCs and owns no work. Team monitoring and
multi-repo scanning are dropped as ideas. `Backlog.Modules.Dashboard` becomes
`Backlog.Modules.Insights`.

### 3. Repositories

```meta
```

Environment folds into Repository Management, renamed **Repositories**. A repository holds
named environments, each with a launch link; a destination with no repository sits in a
no-repository group. `Backlog.Modules.RepositoryManagement` becomes
`Backlog.Modules.Repositories`. Sessions keeps its own term *Environment*, which now means
only where a session ran.

### 4. Goals

```meta
```

Progress is renamed **Goals** and stays its own context: Work knows no goals (ADR 0028,
0035). `Backlog.Modules.Progress` becomes `Backlog.Modules.Goals`.

### 5. Technology Stack is dissolved

```meta
```

A repository's technologies, ratings and deprecations live in the devbook `tech/` folder.
The tools a machine must have are Dev PCs' tool catalog. Portfolio adoption across
repositories is a Repositories read through Devbook's reader.

### 6. Generic and composition

```meta
```

Sync, Notifications and the change journal (ADR 0071) are **generic**. The Calendar and the
desktop shell are **compositions**, not contexts: they read contracts and own no state. The
MCP server is a **channel**; the agent grants and scopes (ADR 0058, 0059, 0069) belong to
it, not to a context.

### 7. Machine

```meta
```

Dev PCs owns the **Machine**, over Sync's device identity. Insights and the tools pane read
it.

### 8. Talk notes

```meta
```

When an event is marked Harvested, each linked Talk note is written as a new note in the
Brain's inbox, the event links it by that Brain path, and the Inbox note is archived. A path
that `tend-brain` later files away shows as missing and is relinked by the person, as any
moved Brain note is today. Events then links only into Work and the Brain, and depends on
the Inbox only for triage.

### 9. Brain.UI

```meta
```

`Backlog.Modules.Brain.UI` reaches Devbook's viewer through a shared component, not through
`Backlog.Modules.Devbook.UI` (ADR 0067).

### 10. Stored values

```meta
```

Every stored value that carries an old context name — feature-flag keys, settings file
names and keys, SQLite tables and columns, sync document kind tokens, MCP tool names — is
renamed. Each rename ships with a migration and a compatibility read that still accepts the
old value from older installs and paired devices. A renamed MCP tool keeps its old name
registered as an alias, and the `backlog-tools` skills that call it switch to the new name
in the same change.

### 11. Map conventions

```meta
```

On the context map, arrows point from supplier to consumer, and an edge with nothing built
behind it is dashed.

## Consequences

```meta
```

- **The landscape reads five core contexts** — Capture, Inbox, Work, Brain, Devbook — six
  supporting — Goals, Sessions, Insights, Repositories, Dev PCs, Events — and two generic,
  Sync and Notifications.
- **The Work merge is the largest step.** Two module sets, their Abstractions and UI, the
  adapters between them, and every consumer's reference move to one; the seven
  Roadmap-over-Tasks adapters fold into the module.
- **Consumers read one supplier for tasks and plans,** Goals and the Calendar included.
- **Every rename is a stored-value migration** (§10). A device on an older build keeps
  sending the old token, so each compatibility read stays until no paired device can carry
  it.
- **Agents keep working across the rename:** an old MCP tool name still answers, through its
  alias.
- **Five unbuilt edges leave the map** with Environment, and Technology Stack's four with
  it.
- **A Talk note's link can break** when `tend-brain` files it, and the person relinks it.
- **The domain folders follow** the new contexts: `tasks/` and `roadmap/` into `work/`,
  `productivity/` and `monitoring/` into `insights/`, `environment/` into
  `repositories/`, `progress/` into `goals/`; `technology-stack/` goes.

## Rejected

```meta
```

- **Keep the modules apart under one Work context.** The review allowed one context across
  two modules; the adapters between them would stay, and the seam they cost is the evidence
  for the merge.
- **Keep the persisted names unchanged.** The review proposed it to avoid migrations. A
  stored `roadmap` or `progress` token keeps the old boundary alive in every database and
  feed, so each is renamed with a compatibility read instead.
- **Rename the Sessions term to *Host*.** With Environment folded into Repositories, the
  word means one thing in each remaining context, and Sessions' record and screens keep it.
- **Keep the `Dashboard` module name.** It is the name that collided with the Monitoring
  context.
- **Park team monitoring and multi-repo scanning as ideas.** Nothing in the product needs
  them; they are dropped, not parked.
- **Fold Goals into Work.** A goal has its own lifecycle, and Work knows no goals (ADR 0028,
  0035).
- **Expose Devbook's viewer through `Devbook.Abstractions`.** An Abstractions project holds
  contracts, not screens; a shared component keeps both UI projects free of each other.
- **Keep a Talk note in the Inbox.** It breaks ADR 0054's one-home rule.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-26 | Proposed: Work, Insights, Repositories and Goals; Technology Stack dissolved; Sync, Notifications and the change journal generic, the Calendar and the shell compositions, MCP a channel; Dev PCs owns the Machine; Talk notes move to the Brain on harvest; Brain.UI off Devbook.UI; every old-name stored value renamed with a migration; supplier-to-consumer arrows, unbuilt edges dashed. | The boundary review the person settled for `+boundary-redesign`. |
