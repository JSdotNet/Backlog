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

## Term: Roadmap

```meta
status: deprecated
aliases: [Roadmap planning, Roadmap view]
related: [.domain/roadmap/naming.md#term-roadmap-plan, .domain/backlog/features.md#feature-roadmap-planning]
```

**Not a Backlog Management term any more.** The canonical concept is the
[Roadmap Plan](../roadmap/naming.md#term-roadmap-plan) in Roadmap Planning, which
owns a stored plan rather than presenting a view over entries.

What holds inside this context is narrower, and is the half worth keeping: a
Backlog Entry's status and execution priority are **not** owned by the roadmap.
A plan may name an entry by id and read its progress; it never writes to it.