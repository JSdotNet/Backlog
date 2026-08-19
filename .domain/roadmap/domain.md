# Domain: Roadmap Planning

```meta
status: draft
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate, Domain Service, or Domain Event in this bounded
> context. Aggregate chapters include sub-chapters for their owned Entities,
> Value Objects, and Enums. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Roadmap Planning owns the forward plan: what is intended to happen, when, in
which order, and what is waiting on what. It is the context where priorities are
decided across projects rather than inside one of them, and it is the only
context that holds a dependency between two pieces of planned work.

It is deliberately **not** a view over
[Backlog Entries](../backlog/domain.md#aggregate-backlog-entry). A plan has to be
able to contain work that has not been refined into an entry yet — that is most
of what planning is — so the plan is stored in its own right, and a Roadmap Item
may optionally name the entry that executes it. What Roadmap does not own is
execution: a Roadmap Item has no status of its own, and progress is read from the
linked Backlog Entry when there is one. Backlog Management remains the authority
for entry status and entry priority; Roadmap Planning is the authority for
planning priority and sequence.

Plans are scoped to repositories. Every Roadmap Item names zero or more
repositories from the
[Repository Registry](../repository-management/domain.md#aggregate-repository-registry)
by alias, which is what makes a portfolio-wide plan readable as a set of
per-repository bands rather than one undifferentiated list.

## Aggregate: Roadmap Plan

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/repository-management/domain.md#aggregate-repository-registry, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

The single plan for the workspace, and the consistency boundary for every item,
milestone, and dependency in it.

The plan is the aggregate root rather than the Roadmap Item, for one reason that
outweighs the convenience of a smaller boundary: **acyclicity is an invariant
across items, not within one**. An item cannot tell whether the dependency it is
about to accept closes a loop three items away, so a boundary drawn around a
single item would push its most important rule outside the model, into whatever
happened to call it. The plan is also the unit that is read and written as a
whole, which makes the boundary and the transaction the same shape. This mirrors
[Repository Registry](../repository-management/domain.md#aggregate-repository-registry),
the other singleton-registry aggregate in this product.

Invariants:

- Dependencies form a directed acyclic graph. A dependency that would close a
  cycle — directly or through any number of intermediate nodes — is **rejected**;
  the plan is left exactly as it was.
- A dependency names a node that exists in this plan. An edge to an unknown id is
  rejected rather than stored, because an edge to nothing is not a weaker plan,
  it is a wrong one.
- No node depends on itself.
- A Planned Window ends on or after the day it starts; both days are inclusive,
  so a single-day item has a window of one day rather than none.
- A Milestone falls on exactly one day and has no duration, which is why it is
  not a one-day item.
- Planning Priority is the plan's own. Setting it never writes to a linked
  Backlog Entry, and an entry's own priority never overwrites it — the two are
  different judgements made for different reasons, and collapsing them would mean
  reprioritising a plan by touching an issue.
- A Repository Scope alias that no longer resolves in the Repository Registry is
  **kept**, and reads as unresolved. Dropping it would silently delete something
  the person wrote because a supplier's registry changed.
- A Backlog Entry Link is a foreign id and nothing more. The plan never reads
  through it to make a decision about itself.
- All mutations to items, milestones, and dependencies go through the root.

A plan with no items is a valid plan — a first run, or everything delivered.

### Entities

#### Roadmap Item

A single piece of planned work, identified by `roadmap_item_id`. Holds `title`,
its `Planned Window`, its `Planning Priority`, its `Repository Scope`, its
`Planning Lane`, its `Dependency` set, an optional `Backlog Entry Link`, and
optional `notes`.

It has no status and no percentage. Both are questions about execution, and
execution belongs to Backlog Management: an item that names an entry shows that
entry's progress, and an item that names none shows that it is planned and
nothing more. Its identity is meaningful outside the plan only as a dependency
endpoint, which is why the id is stable across every reschedule.

#### Milestone

A point in time the plan is read against, identified by `roadmap_milestone_id`:
`title`, the single day it falls `on`, its `Milestone Kind`, and its optional
`Repository Scope` and `Planning Lane`.

A Milestone is an entity of this aggregate rather than a degenerate Roadmap Item
because it is a different thing, not a smaller one: it has no duration to
lengthen or shorten, rescheduling it moves one date rather than two, and "is this
late" is answered against a day rather than a span. It is a first-class
dependency endpoint — work waits on a release, and a release waits on work —
which is also why both entities live in one aggregate: a dependency crossing
between them must be validated on one side of a boundary, not two.

### Value Objects

#### Planned Window

When an item is intended to run: `start` and `end`, both inclusive. Equality is
by value. Validation: `end >= start`. Derived: `days = end - start + 1`, never
less than one.

Inclusive on both ends because that is how a plan is spoken — "through the 31st"
means the 31st — and because an exclusive end makes the shortest possible piece
of work indistinguishable from no work at all.

#### Repository Scope

The repositories an item or milestone belongs to: a set of repository aliases,
normalized, without duplicates. Equality is by value. An empty scope is valid and
means unfiled; it is not an error, and not a default repository.

Aliases are the shared key with
[Repository Management](../repository-management/naming.md#term-repository) and
are held as opaque strings. Resolution is a separate concern — see
[Repository Scope Resolution](#domain-service-repository-scope-resolution).

#### Dependency

An edge from the node that must land first to the node that waits for it:
`depends_on_id`, referencing a `Roadmap Node` — an item or a milestone — in the
same plan. Equality is by value, so the same dependency declared twice is one
dependency.

Held on the waiting node rather than in a separate edge list, because "what am I
waiting for" is the question a reader asks of an item, and the direction in which
a plan is edited.

#### Planning Lane

A free-form row label within a repository band — "platform", "migration",
whatever the person actually calls it. Equality is by value; blank is normalized
to the default lane.

Deliberately a string rather than an enum, for the same reason `area` is on a
[Backlog Entry](../backlog/domain.md#aggregate-backlog-entry): the taxonomy is
the person's, and an enum here would mean shipping a release every time someone
invents a workstream.

#### Backlog Entry Link

An optional foreign id naming the
[Backlog Entry](../backlog/domain.md#aggregate-backlog-entry) that executes this
item. Equality is by value. The link may dangle — an entry can be deleted while
the plan still intends the work — and a dangling link reads as unlinked rather
than as an error.

### Enums

#### Planning Priority

How much the plan wants this item relative to the others: `low`, `medium`,
`high`, `critical`.

The same four words as [Priority](../backlog/domain.md#priority) in Backlog
Management, chosen deliberately rather than by accident: two vocabularies for the
same idea would make every conversation about priority start with "which kind".
They remain different values owned by different contexts, and neither overwrites
the other.

#### Milestone Kind

What kind of fixed point it is: `release`, `freeze`, `review`, `commitment`.

Business kinds, not shapes. How each one is drawn is a presentation decision and
belongs to the view, not here.

## Domain Service: Plan Sequencing

```meta
status: draft
related: [.domain/roadmap/domain.md#aggregate-roadmap-plan]
```

Answers the questions that are about the graph rather than about any one node:
the order the dependencies imply, which nodes are reachable from which, and where
the plan **contradicts itself** — a successor whose window opens before its
predecessor's closes, or an item scheduled to finish after a milestone it is
supposed to land before.

A contradiction is reported, never corrected. A plan is allowed to be temporarily
wrong: that is how a person discovers a date does not fit, and silently shifting
the dependent work would hide exactly the fact worth seeing. Cycle rejection is
different, and stays an invariant on the root — a cycle is not a plan that is
wrong about dates, it is not a plan at all.

It is a service because every one of these answers needs the whole graph rather
than one node's own state. Invocation semantics: query/composition-oriented, and
command-invoked as the validation step behind adding a dependency.

## Domain Service: Repository Scope Resolution

```meta
status: draft
related: [.domain/repository-management/naming.md#term-repository, .domain/environment/domain.md#domain-service-environment-shortcut-resolution]
```

Turns the opaque aliases in a Repository Scope into the repositories the reader
recognizes, by asking the
[Repository Registry](../repository-management/domain.md#aggregate-repository-registry),
and reports which aliases did not resolve.

It exists so the aggregate never holds a foreign repository model and never
depends on the registry being reachable. An unresolved alias is a normal outcome
with a normal presentation — an unfiled band — not a failure that stops a plan
from being read. Invocation semantics: query/composition-oriented, on the read
path.

## Domain Event: RoadmapItemScheduled

```meta
status: draft
related: [.domain/monitoring/domain.md#aggregate-progress-signal]
```

Published when a Roadmap Item's Planned Window is set or changed — on creation,
and on every reschedule that actually moves a date.

### Payload

- `roadmap_item_id` — stable identity of the item, unchanged by the reschedule.
- `title` — the item's title at the time of the event, for readability.
- `start`, `end` — the new Planned Window, both days inclusive.
- `previous_start`, `previous_end` — the window it replaced; absent when the item
  is newly planned, which is how a consumer tells the two apart.
- `planning_priority` — the plan's own priority, not any entry's.
- `repository_aliases` — the item's Repository Scope as written, unresolved.
- `backlog_entry_id` — the linked entry, when there is one.

### Consumers

- [Monitoring & Dashboard](../monitoring/domain.md#aggregate-progress-signal) —
  compares the plan against what actually happened, so drift between intent and
  delivery becomes visible without Monitoring reading the plan's internals.

### Published language rules

- The window is always inclusive at both ends. A consumer that treats `end` as
  exclusive will be one day short on every item.
- `repository_aliases` are opaque and unresolved by design. A consumer that needs
  repository facts asks Repository Management, not this payload.
- Absent `previous_start`/`previous_end` means "newly planned". It does not mean
  "unchanged".
- The event carries no status and no progress. Those are Backlog Management's
  published language; a consumer that wants both correlates on
  `backlog_entry_id`.
