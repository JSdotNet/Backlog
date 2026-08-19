# Naming: Backlog Management

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Term: Backlog Entry

```meta
status: draft
aliases: [BacklogEntry, backlog_entry_id, backlog_item_id]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

The single work item managed by this context. `backlog_item_id` is the form
other contexts and GitHub use to reference it (see Monitoring and Dev PC
Management); `backlog_entry_id` is the form Second Brain's `BacklogLink` uses.

## Term: Sub-Item

```meta
status: draft
aliases: [SubItem]
related: [.domain/backlog/domain.md#sub-item]
```

An owned checklist step of a Backlog Entry, with identity only within the
aggregate.

## Term: Projection

```meta
status: draft
aliases: [ProjectionRef]
related: [.domain/backlog/domain.md#domain-service-projection]
```

Turning a targeted `repo_id` into an external artifact when work starts. The
recorded projection target is the `ProjectionRef` value object.

## Term: Entry Type

```meta
status: draft
aliases: [EntryType]
related: [.domain/backlog/domain.md#entry-type]
```

Classification of an entry as prompt, task, idea, or follow-up.

## Term: Entry Status

```meta
status: draft
aliases: [EntryStatus]
related: [.domain/backlog/domain.md#entry-status]
```

Lifecycle state of an entry; see `flow.md` for the state transitions.

## Term: Area

```meta
status: draft
aliases: [area]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

A self-chosen grouping the person files an entry under — "repos", "projects",
"inbox", or whatever vocabulary they actually use. Deliberately a free-form
string rather than an enum: the taxonomy belongs to the person, not the
product. An entry with no area is unfiled.

## Term: AI Work Log

```meta
status: draft
aliases: [AIWorkLog, AIWorkLogged]
related: [.domain/backlog/domain.md#domain-event-aiworklogged]
```

Evidence that an AI-assisted action contributed to a Backlog Entry. The log is
owned by Backlog because it is part of the entry audit trail; Productivity
consumes the published event to calculate insight.

## Term: Due Date

```meta
status: proposed
aliases: [due_on, due, Due]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

The calendar day an entry is committed to. A date, carrying no time and no
timezone, so it means the same day wherever the device is. Distinct from a
`Reminder`, which is a moment, and from `My Day`, which is a choice about today.
How the date is worded on screen belongs to the channel showing it.

## Term: Reminder

```meta
status: proposed
aliases: [remind_at, remind, Reminder]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

A local date and time the person asked to be reminded of an entry at, held as
wall-clock intent: 09:00 means 09:00 wherever they are when it arrives. A
request recorded on the entry rather than a promise about delivery — a reminder
whose time has passed reads as overdue until it is cleared.

## Term: Recurrence

```meta
status: proposed
aliases: [Repeat, repeat, recurrence, RepeatLabel, RecurrenceUnit, DayOfWeek]
related: [.domain/backlog/domain.md#recurrence]
```

The shape of a repeat: an interval, a unit, and optionally the weekdays it is
restricted to. Called "repeat" on screen and in the metadata token, `Recurrence`
in the model. It describes the shape only — the date of the next occurrence is
the `Recurrence` policy's calculation, anchored to the due date.

## Term: My Day

```meta
status: proposed
aliases: [in_my_day_on, myday, InMyDay]
related: [.domain/backlog/features.md#feature-my-day]
```

The set of entries a person picked to work on today. Held as the date it was
picked for, not as a flag, so membership is derived by comparing that date
against the reader's current local date and expires without a timer.
Deliberately not a deadline: `My Day` is this morning's choice, a `Due Date` is
a commitment.

## Term: Dependency

```meta
status: proposed
aliases: [depends_on, DependsOn, after, predecessor]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

An entry this entry waits on, named by id. An entry may have several — needing
two things finished before starting is ordinary. Written `after:<id>` in the
metadata line. A weak reference across an aggregate boundary: an id resolving to
nothing is still a dependency, and still blocks.

## Term: Readiness

```meta
status: proposed
aliases: [TaskReadiness, ready, blocked]
related: [.domain/backlog/domain.md#readiness]
```

Where an entry stands once its dependencies are taken into account: done, ready,
or blocked. Derived on every read, never stored, and orthogonal to `Entry
Status` — status is recorded, readiness is concluded, and they share only `done`.

## Term: Occurrence

```meta
status: proposed
aliases: [recurrence_source_id, OccurrenceSpawned]
related: [.domain/backlog/domain.md#domain-service-recurrence]
```

One instance of a recurring entry. Completing an occurrence leaves it completed
and spawns the next as a separate entry, linked back by
`recurrence_source_id`. A series is therefore a chain of entries rather than one
entry that moves, so the record of each completion survives.

## Term: Roadmap

```meta
status: draft
aliases: [Roadmap planning, Roadmap view]
related: [.domain/backlog/features.md#feature-roadmap-planning]
```

A planning view over selected Backlog Entries, grouped by horizon, milestone,
theme, target environment, or repository. The roadmap does not own status or
priority; it reads those from the entries it displays.