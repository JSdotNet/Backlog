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
