# Domain: Second Brain

```meta
status: draft
```

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

Second Brain is a personal project knowledge base — not a task queue. It
collects, organizes, and retrieves Knowledge Notes structured with the PARA
framework (Projects, Areas, Resources, Archive), and links bidirectionally with
[Backlog Entries](../backlog/domain.md#aggregate-backlog-entry) so related
context is easy to find later.

## Aggregate: Knowledge Note

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

An organized unit of project knowledge stored as markdown and the boundary for
its own organization and links. Invariants: a note carries a primary `topic` and
a `PARA Category`; it may be scoped to zero or more projects/repos; links to
backlog entries and other notes are managed through the root; and status advances
through the note lifecycle (created → organized → linked → archived, with
restore). `updated_at` moves forward on every change.

### Entities

The Knowledge Note aggregate has no independently identified child entities;
`Project Ref`, `Tag`, and `Backlog Link` are value objects owned by the root.

### Value Objects

#### Project Ref

A scope link to a project/repo: `repo_id` and `project_name`. Equality is by
value; a note may hold several.

#### Backlog Link

A typed link to a Backlog Entry: `backlog_entry_id`, `link_type`
(reference or embed), and `linked_at`. Immutable; equality is by value.

#### Tag

A `#keyword` for cross-cutting discovery (e.g. `#architecture`, `#performance`).
Equality is by canonical `name`.

### Enums

#### PARA Category

Organization bucket for a note:

- `projects` — active work with a goal and deadline.
- `areas` — ongoing responsibilities without a deadline.
- `resources` — reference material and collected knowledge.
- `archive` — inactive items preserved for future search.

#### Note Source

Origin of the note: `inbox` (routed from triage), `manual`, or `import`.

## Domain Service: Cross-Linking

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

Maintains bi-directional links between Knowledge Notes and Backlog Entries and
supports queries that span both domains (search backlog + knowledge together,
embed knowledge snippets into backlog details). It is a service because a link
has two owners in different contexts and must be kept consistent from both sides
rather than living inside a single aggregate. Invocation semantics: consistency policy invoked when note/backlog links are created, removed, or reconciled.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Second Brain has a single aggregate; `PARA Category` and `Note Source` are
documented under it. This chapter is reserved for future cross-aggregate enums.
