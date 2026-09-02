# Tasks

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Tasks maintains a personal backlog of prompts, tasks, ideas, and
follow-ups across multiple projects and repos. It converts triaged
[Inbox Items](../inbox/domain.md#inbox-item) into actionable,
prioritized Tasks and projects them to external systems such as GitHub
and the Copilot CLI.

## Task

```meta
type: aggregate
status: draft
related: [.domain/inbox/domain.md#inbox-item, .arc42/08-crosscutting-concepts.md#shared-data-types, .arc42/08-crosscutting-concepts.md#task-sync, .arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

A refined, actionable item in the personal backlog and the consistency boundary
for all of its sub-items, projections, and usage history. It is the single source
of truth: one logical item with one priority and one status even when it targets
multiple repositories. Invariants: status only moves through the defined
lifecycle; all mutations to sub-items, projection references, AI work log events, and usage events go through the root; parent progress reflects sub-item completion; projections are created on `Ready → In Progress` (`TaskProjected`, one per `repo_id`) and closed on completion (`TaskCompleted`). A manually created task starts at `draft` with no `source_inbox_id`.

The task also carries two attributes that place it in the person's own working
set rather than in any external system: `area`, a free-form string ("repos",
"projects", "inbox", or whatever vocabulary the person actually uses) that files
the task into a self-chosen grouping — blank is normalized to unfiled, and the
taxonomy is deliberately theirs, not a fixed enum; and `order`, a manual rank used
to hand-sequence tasks within the backlog (tasks that have never been ranked
share the default and fall back to recency). Both are freely re-settable and
carry no lifecycle invariant of their own.

Beyond that working-set placement the task carries four scheduling attributes,
all optional and none of them load-bearing for the lifecycle. `due_on` is the
calendar date the task is committed to - a date rather than an instant, because
"due Friday" is a commitment to a day and an instant would move the deadline
whenever the device changed timezone. `remind_at` is a local date and time the
person asked to be reminded at, held as wall-clock intent with no zone: a
reminder set for 09:00 is a reminder for 09:00 wherever they are when it
arrives. A reminder whose time has passed is overdue, which is derived by
comparison rather than stored, and delivery sits deliberately outside the
aggregate - the task records that a reminder was wanted, not that one was sent.
`recurrence` says the task repeats and is described by the `Recurrence` value
object. `in_my_day_on` is the date the person picked this task for their day:
the task is in My Day exactly when that date is the reader's current local
date, so the decision expires by arithmetic rather than by a timer and needs no
clock, timezone or background sweep to retire it. My Day is not a due date - one
is a commitment, the other is this morning's choice about what to look at.

`depends_on` lists the tasks this one waits on. A list rather than a single
predecessor, because a step that needs two things finished before it can start
is the ordinary case and asking which of the two is the real predecessor is a
question with no answer. The tasks are named by id and held as plain
identifiers, the same rule `repo_ids` follows: every `Task` is its own
aggregate root, so a dependency is a weak reference across a boundary rather
than an object graph. An id naming no task the reader can see still blocks -
dropping it would let a chain claim to be ready when the step it waits on is
merely missing from view, which is the one failure that looks exactly like
success. `Readiness` is derived from this list on every read and never stored.

Dependency cycles are surfaced rather than prevented, and there is deliberately
no invariant against one. A cycle spans aggregate boundaries, so no single task
can enforce its absence transactionally; and which edge in a loop is the wrong
one is answerable only by the person who wrote them.

`attachment` names one place on disk the task's material lives: a folder or an
archive, held as a path. One place and not a list, which is the decision worth
recording here rather than in the surface that draws it - a collection would make
the task's presentation grow with however many files somebody happened to drop
on it, and what a person means by "the files for this" is a folder they already
keep them in. A path and not a copy, for the reason the whole context rests on:
the task is Markdown that gets committed and shared
(`.arc42/02-constraints.md#technical-constraints`), and copying material into a
store would make the text stop being the whole of the task. So the task points,
and pointing is all it does. Whether the path still resolves is the file system's
answer and a different answer on a different machine, so it carries no invariant
and is never validated: a task written on the desktop and read on the phone is
not invalid for naming a drive the phone has never seen. Its spelling on the
metadata line is `.design/content-editing.md#scheduling-and-dependency-tokens`.

The task also carries one attribute that is not a fact about the work at all.
`view` records which reading of the body the person last asked for - the steps,
or the Markdown block those steps are written in - and it is a display preference
rather than domain state: nothing in the lifecycle reads it, no invariant depends
on it, and a task that has never been looked at simply does not have one. It is
named here because it is stored on the task, and it is stored on the task
because the Markdown is canonical (`.arc42/02-constraints.md#technical-constraints`):
a preference kept in a sidecar or in a per-device setting would not survive the
file being shared, and whoever opened the task from a clone would get somebody
else's default instead of the way this task is meant to be read. The
alternative - deriving it every time from whether the body happens to contain
sub-items - is what happens when the attribute is absent, and it cannot record a
person disagreeing with that guess. Its spelling on the metadata line is
`.design/content-editing.md#scheduling-and-dependency-tokens`.

A task spawned as the next occurrence of a repeating one carries
`recurrence_source_id`, naming the occurrence it followed. It is provenance in
the same spirit as `source_inbox_id` and carries no invariant: a spawned task
is a separate aggregate with its own lifecycle, and the task it came from may
since have been archived or deleted.

A task created by importing a plan carries `import_plan_id` and
`import_item_id`, naming the plan and the item inside it that produced the
task. Both are provenance in the same spirit as `source_inbox_id` and
`recurrence_source_id` and carry no invariant of their own, but together they
are also the key a later import of the same plan uses to find the task a
given item already produced, so re-importing an updated plan can adjust an
task still in flight instead of duplicating it — see
[Re-importing an updated plan](features.md#re-importing-an-updated-plan).
`import_plan_id` is also added to the task's `tags`, so filing and filtering
by plan reuses the same mechanism as filing against a
[roadmap tag](features.md#filing-a-task-against-a-roadmap-tag) rather than a
parallel lookup.

The task also carries an optional `effort`: a size estimate in **story points**,
held as a non-negative integer. It is deliberately three-valued at the edges.
Absent or `null` means "not estimated"; `0` is a real estimate that happens to
contribute nothing; and a negative number is not an estimate at all and is
rejected by the model. Story points size the work, they do not measure the time
spent on it — a task that took an afternoon and one that took a week can carry
the same estimate if they were the same size of problem, and the number does not
change because the clock did.

It is explicitly expected that an AI agent will often derive the estimate from the
task's own content rather than a person typing one in, but that changes nothing
about what it is: derived or hand-set, it stays an estimate and is revised as the
understanding of the work changes. The deriving itself is not built here — this is
the point at which the value becomes registrable and visible; calculating it comes
later, and the model is deliberately indifferent to which of the two put the number
there. The estimate is Tasks's to hold: Roadmap Planning reads and
totals it (see [Roadmap Item Gathering](../roadmap/domain.md#roadmap-item-gathering))
but never registers or owns it.

Two attributes exist so the same task can live on more than one of the person's
machines. `updated_at` is the moment the task last changed, in UTC, restamped by
the device on every mutation; `deleted_at` marks a task as deleted without
removing it. Neither carries a lifecycle invariant of its own, and neither is
ever set by hand.

They are here rather than in the storage adapter because reconciliation is a
domain rule, not a persistence detail. When the same task is edited on two
machines the later edit wins whole — see
[Multi-device sync](features.md#multi-device-sync) — and "later" is a question
only the task itself can answer. A deletion has the same problem in sharper form:
a task that is simply gone from one machine is indistinguishable from one that
machine has never seen, so deletion has to leave something behind to travel.
`deleted_at` is that something, and a task carrying it is gone as far as every
read is concerned.

The task is the single source of truth **on the device that holds it**. Where a
second device holds its own copy, the two reconcile with each other; the cloud
holds a replica used to carry changes between them and is never itself the
authority. See
`.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`.

### Sub-Item

```meta
type: entity
status: draft
```

An ordered breakdown step owned by the task, with its own `title`,
`Sub-Item Status`, optional `notes`, and `order`. It has identity within the
aggregate but no meaning outside it. Sub-items can be reordered, added, or removed
independently and may project to GitHub issue task-list checkboxes - checkboxes
inside the task's own issue. A sub-item is never projected as an issue of its
own: `Projection Ref` is owned by the task and there is nowhere on a step to
record one, so a step filed separately could be filed again with nothing able to
tell that it already had been.

A step, in the product's language, is a sub-item - there is no second concept
for one. A sub-item carries those four attributes and nothing else: no task
type, no priority, no task status vocabulary, no tags, and none of the task's
scheduling or dependency attributes. A step inherits its parent's deadline by
belonging to it, and a breakdown step that needed its own priority would be an
task rather than a step.

### Projection Ref

```meta
type: value-object
status: draft
```

An immutable link to a downstream external artifact created from the task:
`repo_id`, `external_id`, and `target_type` (e.g. github-issue, cli-task).
Equality is by value.

### Recurrence

```meta
type: value-object
status: draft
```

How often a task repeats: an `interval`, a `Recurrence Unit`, and an optional
set of `Weekday`s the repeat is restricted to. "Every weekday" is interval 1,
unit week, weekdays Monday through Friday; "every other week" is interval 2,
unit week, with no weekday restriction. Equality is by value.

The value object describes the shape of the repeat only. It does not say when
the next occurrence falls - that is the `Occurrence Spawning` policy's calculation - and
the repeat is anchored to `due_on` rather than to the completion date, so a
weekly task finished three days late still falls due on its original weekday.

### Attachment

```meta
type: value-object
status: draft
```

Where a task's material is kept: a `path` naming a folder or an archive.
Equality is by value.

Two things are read off the path rather than stored beside it. Its `name` is the
last segment - what a person would call the thing - and whether it is an archive
is decided by the path's spelling rather than by asking the disk, so that the
value object stays comparable and a file renamed underneath a task cannot
change what the task says. A folder that happens to be called `backup.zip` is an
archive by this rule, and that is the right answer for the only thing the answer
decides: which word to call it.

How much is in the place is deliberately not part of the value. A count is a
question for whoever can see the file system at the moment it is asked, and an
task that stored one would be a task asserting something that stopped being
true the moment somebody added a file.

### Usage Event

```meta
type: value-object
status: draft
```

An immutable audit record of a prompt copy/use: `timestamp` and `action`.
Equality is by value.

### AI Work Log

```meta
type: value-object
status: draft
```

An immutable record that an AI-assisted action contributed to the task:
`timestamp`, `ai_tool`, `activity_kind`, optional `session_id`, and optional
`outcome_ref`. Equality is by value.

### Task Type

```meta
type: enum
status: draft
related: [.domain/tasks/naming.md#dependency]
```

Classification of the task: `prompt`, `task`, `idea`. A follow-up is a
`Dependency` on the task it comes after rather than a type of its own.

### Task Status

```meta
type: enum
status: draft
```

Lifecycle state: `draft`, `ready`, `in_progress`, `done`, `archived`.

### Priority

```meta
type: enum
status: draft
```

Ranking of the task: `low`, `medium`, `high`, `critical`.

### Sub-Item Status

```meta
type: enum
status: draft
```

Completion state of a sub-item: `pending`, `done`.

### Recurrence Unit

```meta
type: enum
status: draft
```

The period a repeat is counted in: `day`, `week`, `month`, `year`.

### Weekday

```meta
type: enum
status: draft
```

A day of the week, `monday` through `sunday`. Used only to restrict a weekly
`Recurrence`.

### Readiness

```meta
type: enum
status: draft
```

Where a task stands once the tasks it waits on are taken into account:
`done`, `ready`, `blocked`. Derived on every read from `depends_on` and never
stored - a stored readiness would go stale the moment a predecessor was
completed.

Three values rather than two, because "not done" covers both the task that can
be picked up now and the task that cannot, and telling those two apart is the
whole reason anyone writes a dependency down. `done` wins over the derivation:
a task somebody marked done is done even if something it named is still
outstanding, because done is a recorded fact where blocked is only a
conclusion.

## Projection

```meta
type: domain-service
status: draft
related: [.domain/monitoring/domain.md#progress-signal, .arc42/06-runtime-view.md#task-to-github-issue]
```

Creates and closes downstream artifacts for a multi-repo task: on `TaskProjected`
it creates one GitHub issue and/or CLI task per `repo_id`, recording each as a
`Projection Ref`; on `TaskCompleted` it closes all projections. It is a service
because it coordinates the task with external systems (GitHub, Copilot CLI) and
spans multiple downstream artifacts rather than a single aggregate mutation. Invocation semantics: event-triggered policy / process manager. It reacts to `TaskProjected` and `TaskCompleted`; it is not invoked as part of a synchronous aggregate command.

## Occurrence Spawning

```meta
type: domain-service
status: proposed
related: [.domain/tasks/domain.md#task, .domain/tasks/domain.md#taskcompleted, .domain/tasks/domain.md#occurrencespawned]
```

Creates the next occurrence of a repeating task. When a save completes a task
that carries a `Recurrence`, it creates a new `Task` with `due_on`
advanced to the next date that recurrence produces.

It is a service because producing the next occurrence creates a second
aggregate instance, which an aggregate cannot do to itself: the completed task
stays completed and keeps its record of what was done, and what follows is a new
task with its own lifecycle.

Invocation semantics: called synchronously by the use case that completes the
task. Deliberately not an event-triggered policy, unlike `Projection` — this
context publishes no domain events yet, and ADR 0006 rejected putting a mediator
behind its handlers on the grounds that a caller which already knows the use case
it means gains nothing from the indirection. `OccurrenceSpawned` below is
therefore documented rather than emitted, alongside the other events of this
context, until there is machinery to carry it. Nothing about the spawn waits on
that machinery: the successor is created either way, and what an event would add
is a consumer being told.

What carries over is what the repeat is of - `title`, `content_md`, `type`,
`priority`, `area`, `tags`, `repo_ids`, and the `Recurrence` itself - plus
`recurrence_source_id` pointing back at the task it came from, so a series can
be traced the way `source_inbox_id` traces a task back to its inbox item. What
does not carry over is everything that was about the occurrence rather than the
repeat: the new task starts at `ready` with its sub-items reset to `pending`,
and with no projections, no usage history, no reminder that has already fired,
and no `in_my_day_on`.

A repeating task therefore accumulates one completed task per occurrence.
That is the cost of keeping the record rather than rolling a single task
forward, and it is bounded by `Archive and lifecycle` rather than by this policy
- completed occurrences archive out of default views like any other finished
task.

## StatusChanged

```meta
type: domain-event
status: draft
related: [.domain/tasks/domain.md#task, .domain/monitoring/domain.md#progress-signal]
```

Published when a `Task` changes `Task Status`.

### Payload

- `task_id` - task identifier.
- `previous_status` - prior `Task Status`.
- `new_status` - new `Task Status`.
- `changed_at` - time of the transition.
- `repo_ids` - targeted repositories for correlation.

### Consumers

- Monitoring & Dashboard, which turns work-state changes into progress signals.

### Published language rules

- The event reflects the durable work-state transition; consumers do not infer
  additional semantics from projection side effects.

## TaskProjected

```meta
type: domain-event
status: draft
related: [.domain/tasks/domain.md#task, .domain/tasks/domain.md#projection, .domain/monitoring/domain.md#progress-signal]
```

Published when a `Task` begins external execution and the Projection
policy creates one downstream artifact per targeted repository.

### Payload

- `task_id` - task identifier.
- `repo_id` - repository receiving a projection.
- `projection_target` - `github-issue` or `copilot-task`.
- `external_id` - created downstream artifact identifier.
- `projected_at` - time the projection was created.

### Consumers

- Monitoring & Dashboard, which correlates planned work with external execution.

### Published language rules

- One event instance is emitted per projection target so consumers can reason
  about multi-repo fan-out without inspecting `Projection Ref` internals.

## TaskCompleted

```meta
type: domain-event
status: draft
related: [.domain/tasks/domain.md#task, .domain/tasks/domain.md#projection, .domain/monitoring/domain.md#progress-signal]
```

Published when a completed `Task` closes its downstream projections.

### Payload

- `task_id` - task identifier.
- `repo_ids` - repositories whose projections are being closed.
- `closed_projection_ids` - downstream artifact identifiers that were closed.
- `completed_at` - time the projections were closed.

### Consumers

- Monitoring & Dashboard, which reconciles work completion against external systems.

### Published language rules

- Completion is owned by Tasks; external systems consume the closing
  signal but do not redefine what completion means.

## OccurrenceSpawned

```meta
type: domain-event
status: proposed
related: [.domain/tasks/domain.md#task, .domain/tasks/domain.md#occurrence-spawning, .domain/monitoring/domain.md#progress-signal]
```

Published when a completed repeating `Task` produces its successor.

### Payload

- `source_task_id` - the completed task the occurrence came from.
- `task_id` - the newly created task.
- `due_on` - calendar date the new occurrence is due.
- `spawned_at` - time the occurrence was created.

### Consumers

- Monitoring & Dashboard, which needs the two tasks linked to read a repeating
  series as a series rather than as unrelated items.

### Published language rules

- The successor is a separate `Task` with its own lifecycle. The link is
  provenance, not ownership, and consumers must not treat a series as one work
  item.
- One event instance is emitted per spawned occurrence.

## AIWorkLogged

```meta
type: domain-event
status: draft
related: [.domain/tasks/domain.md#task, .domain/productivity/domain.md#productivity-ledger]
```

Published when a Task records that an AI-assisted action contributed to
the work item.

### Payload

- `task_id` - task identifier.
- `ai_tool` - tool or channel used, such as Copilot CLI, GitHub Copilot App, IDE
  chat, or another AI assistant.
- `activity_kind` - planning, coding, review, summarization, research, or other
  user-facing category.
- `session_id` - optional source session identifier when one exists.
- `outcome_ref` - optional reference to a pull request, commit, issue, note, or
  generated artifact.
- `logged_at` - time the activity was recorded.

### Consumers

- Productivity, which turns AI-assisted activity into personal productivity
  metrics and trends.

### Published language rules

- The event records contribution evidence only; it does not claim productivity
  value by itself.
- Consumers conform to the published fields and do not inspect Task
  internals to infer additional activity.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Tasks has a single aggregate; all enums are documented under the
Task. This chapter is reserved for future cross-aggregate enums.
