# Tasks

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Task

```meta
type: term
status: draft
aliases: [TaskItem, task_id, BacklogEntry, backlog_entry_id, backlog_item_id]
related: [.domain/tasks/domain.md#task]
```

The single work item managed by this context. `task_id` is the form other
contexts and GitHub use to reference it (see Monitoring and Dev PC Management),
and the form Second Brain's `TaskLink` uses.

The implementation type is `TaskItem` rather than the bare `Task`, which would
shadow `System.Threading.Tasks.Task` in the module's own namespace and collide
with the narrower `TaskType.Task`; both names resolve back to this one concept.

This context was called Backlog Management until this term was renamed, and the
aggregate was called a **Backlog Entry**. `BacklogEntry`, `backlog_entry_id`,
and `backlog_item_id` are recorded above as aliases so the older names — which
outlive the rename in code, GitHub issue bodies, and stored data — still resolve
here. The product itself is still called Backlog; only this bounded context and
its work item were renamed.

## Sub-Item

```meta
type: term
status: draft
aliases: [SubItem]
related: [.domain/tasks/domain.md#sub-item]
```

An owned checklist step of a Task, with identity only within the
aggregate.

## Projection

```meta
type: term
status: draft
aliases: [ProjectionRef]
related: [.domain/tasks/domain.md#projection]
```

Turning a targeted `repo_id` into an external artifact when work starts. The
recorded projection target is the `ProjectionRef` value object.

## Task Type

```meta
type: term
status: draft
aliases: [TaskType]
related: [.domain/tasks/domain.md#task-type, .domain/tasks/naming.md#dependency]
```

Classification of a task as prompt, task, or idea. A follow-up is not a type: it
is an ordinary task carrying a `Dependency` on the task it comes after.

## Task Status

```meta
type: term
status: draft
aliases: [TaskStatus]
related: [.domain/tasks/domain.md#task-status]
```

Lifecycle state of a task; see `flow.md` for the state transitions.

## Area

```meta
type: term
status: draft
aliases: [area]
related: [.domain/tasks/domain.md#task]
```

A self-chosen grouping the person files a task under — "repos", "projects",
"inbox", or whatever vocabulary they actually use. Deliberately a free-form
string rather than an enum: the taxonomy belongs to the person, not the
product. A task with no area is unfiled.

## AI Work Log

```meta
type: term
status: draft
aliases: [AIWorkLog, AIWorkLogged]
related: [.domain/tasks/domain.md#aiworklogged]
```

Evidence that an AI-assisted action contributed to a Task. The log is
owned by Tasks because it is part of the task audit trail; Productivity
consumes the published event to calculate insight.

## Due Date

```meta
type: term
status: proposed
aliases: [due_on, due, Due]
related: [.domain/tasks/domain.md#task]
```

The calendar day a task is committed to. A date, carrying no time and no
timezone, so it means the same day wherever the device is. Distinct from a
`Reminder`, which is a moment, and from `My Day`, which is a choice about today.
How the date is worded on screen belongs to the channel showing it.

## Reminder

```meta
type: term
status: proposed
aliases: [remind_at, remind, Reminder]
related: [.domain/tasks/domain.md#task]
```

A local date and time the person asked to be reminded of a task at, held as
wall-clock intent: 09:00 means 09:00 wherever they are when it arrives. A
request recorded on the task rather than a promise about delivery — a reminder
whose time has passed reads as overdue until it is cleared.

## Recurrence

```meta
type: term
status: proposed
aliases: [Repeat, repeat, recurrence, RepeatLabel, RecurrenceUnit, DayOfWeek]
related: [.domain/tasks/domain.md#recurrence]
```

The shape of a repeat: an interval, a unit, and optionally the weekdays it is
restricted to. Called "repeat" on screen and in the metadata token, `Recurrence`
in the model. It describes the shape only — the date of the next occurrence is
the `Occurrence Spawning` policy's calculation, anchored to the due date.

## My Day

```meta
type: term
status: proposed
aliases: [in_my_day_on, myday, InMyDay]
related: [.domain/tasks/features.md#my-day]
```

The set of tasks a person picked to work on today. Held as the date it was
picked for, not as a flag, so membership is derived by comparing that date
against the reader's current local date and expires without a timer.
Deliberately not a deadline: `My Day` is this morning's choice, a `Due Date` is
a commitment.

## Dependency

```meta
type: term
status: proposed
aliases: [depends_on, DependsOn, after, predecessor]
related: [.domain/tasks/domain.md#task]
```

A task this task waits on, named by id. A task may have several — needing
two things finished before starting is ordinary. Written `after:<id>` in the
metadata line. A weak reference across an aggregate boundary: an id resolving to
nothing is still a dependency, and still blocks.

## Attachment

```meta
type: term
status: proposed
aliases: [attachment, files, attached folder, attached material]
related: [.domain/tasks/domain.md#attachment, .domain/tasks/features.md#attached-material]
```

The one place a task's material is kept: a folder or an archive, named by path
and written `files:<path>` in the metadata line. Singular by design — a task has
an attachment or it has none, never several — and a pointer rather than a copy, so
"attached" means "this is where it lives" and not "this is stored here". Called
"Folder" or "Archive" on screen depending on the path, never "Attachments" in the
plural about one task.

## Readiness

```meta
type: term
status: proposed
aliases: [TaskReadiness, ready, blocked]
related: [.domain/tasks/domain.md#readiness]
```

Where a task stands once its dependencies are taken into account: done, ready,
or blocked. Derived on every read, never stored, and orthogonal to `Task
Status` — status is recorded, readiness is concluded, and they share only `done`.

## Occurrence

```meta
type: term
status: proposed
aliases: [recurrence_source_id, OccurrenceSpawned]
related: [.domain/tasks/domain.md#occurrence-spawning]
```

One instance of a recurring task. Completing an occurrence leaves it completed
and spawns the next as a separate task, linked back by
`recurrence_source_id`. A series is therefore a chain of tasks rather than one
task that moves, so the record of each completion survives.

## Effort

```meta
type: term
status: draft
aliases: [effort, story points, story-point estimate]
related: [.domain/tasks/domain.md#task, .domain/roadmap/naming.md#effort]
```

The size of a Task in **story points**: a non-negative integer, optional,
and three-valued at the edges — absent means "not estimated", `0` is a real
zero-point estimate, and a negative is rejected. It sizes the work, not the time
spent on it, and is an estimate however it was arrived at: often derived by an AI
agent from the task's content, always revisable, and never a measurement.
Registered here and owned here; Roadmap Planning reads and totals it but never
sets it.

## Roadmap Tag

```meta
type: term
status: draft
aliases: [roadmap tag, tag]
related: [.domain/roadmap/naming.md#roadmap-tag, .domain/tasks/features.md#filing-a-task-against-a-roadmap-tag]
```

A [Roadmap Item](../roadmap/domain.md#roadmap-item)'s tag, borrowed here as
vocabulary. This context does not define the tag — it offers every roadmap tag in
the task's tag picker so a task can be filed against planned work using the
plan's own slug, and matching exactly is what lets the roadmap gather the task
back. A task's `tags` stay free-form strings; a roadmap tag is simply one a
person may pick from the shared vocabulary rather than invent. The canonical
concept lives in [Roadmap Planning](../roadmap/naming.md#roadmap-tag).

## Roadmap

```meta
type: term
status: deprecated
aliases: [Roadmap planning, Roadmap view]
related: [.domain/roadmap/naming.md#roadmap-plan, .domain/tasks/features.md#roadmap-planning]
```

**Not a Tasks term any more.** The canonical concept is the
[Roadmap Plan](../roadmap/naming.md#roadmap-plan) in Roadmap Planning, which
owns a stored plan rather than presenting a view over tasks.

What holds inside this context is narrower, and is the half worth keeping: a
Task's status and execution priority are **not** owned by the roadmap.
A plan may name a task by id and read its progress; it never writes to it.