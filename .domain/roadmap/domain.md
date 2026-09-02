# Roadmap Planning

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

Roadmap Planning owns the forward plan: what is intended to happen, when, in
which order, and what is waiting on what. It is the context where priorities are
decided across projects rather than inside one of them, and it is the only
context that holds a dependency between two pieces of planned work.

It is deliberately **not** a view over
[Tasks](../tasks/domain.md#task). A plan has to be
able to contain work that has not been refined into a task yet — that is most
of what planning is — so the plan is stored in its own right, and a Roadmap Item
may optionally name the task that executes it. What Roadmap does not own is
execution: a Roadmap Item has no status of its own, and progress is read from the
linked Task when there is one. Tasks remains the authority
for task status and task priority; Roadmap Planning is the authority for
planning priority and sequence. The same division holds for size: the effort an
item reports is **totalled** here but **registered** elsewhere — the story points
live on the Tasks and knowledge chapters the item gathers, and Roadmap
reads and adds them without owning a single one.

Plans are scoped to repositories. Every Roadmap Item names zero or more
repositories from the
[Repository Registry](../repository-management/domain.md#repository-registry)
by alias, which is what makes a portfolio-wide plan readable as a set of
per-repository bands rather than one undifferentiated list.

## Roadmap Plan

```meta
type: aggregate
status: draft
related: [.domain/tasks/domain.md#task, .domain/repository-management/domain.md#repository-registry, .arc42/08-crosscutting-concepts.md#shared-data-types]
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
[Repository Registry](../repository-management/domain.md#repository-registry),
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
  Task, and a task's own priority never overwrites it — the two are
  different judgements made for different reasons, and collapsing them would mean
  reprioritising a plan by touching an issue.
- A Repository Scope alias that no longer resolves in the Repository Registry is
  **kept**, and reads as unresolved. Dropping it would silently delete something
  the person wrote because a supplier's registry changed.
- A Task Link is a foreign id and nothing more. The plan never reads
  through it to make a decision about itself.
- Every Roadmap Item has exactly one Roadmap Tag. It is derived from the title
  when the item is created and is thereafter independent of it: renaming the title
  never reslugs the tag, because a tag is a word other contexts have already
  written down. A title that slugifies to nothing takes the tag `item` rather than
  an empty one.
- Roadmap Tags are not unique across items. The plan may deliberately hold several
  items under one tag, so resolving a tag may return more than one item and
  uniqueness is neither enforced nor assumed.
- A Knowledge Ref is a foreign reference the plan holds and never reads through to
  decide anything about itself — the same rule as the Task Link. Both may
  dangle, and neither is validated or repaired.
- The total registered effort an item reports is plain arithmetic over story
  points that were actually registered elsewhere. The plan invents no estimate for
  anything that registered none, and owns none of the values it adds.
- All mutations to items, milestones, and dependencies go through the root.

A plan with no items is a valid plan — a first run, or everything delivered.

### Roadmap Item

```meta
type: entity
status: draft
```

A single piece of planned work, identified by `roadmap_item_id`. Holds `title`,
its `Roadmap Tag`, its `Planned Window`, its `Planning Priority`, its
`Repository Scope`, its `Planning Lane`, its `Dependency` set, an optional
`Task Link`, a set of `Knowledge Ref`s, and optional `notes`.

It has no status and no percentage. Both are questions about execution, and
execution belongs to Tasks: an item that names a task shows that
task's progress, and an item that names none shows that it is planned and
nothing more. Its identity is meaningful outside the plan only as a dependency
endpoint — and, now, as the tag other contexts file work under — which is why the
id is stable across every reschedule.

An item gathers the work it stands for in **two different ways**, and the
difference is worth stating because it decides what can safely be unpicked later.
It gathers by **name**: the one `Task Link` it may hold, and the
`Knowledge Ref`s it lists, are references it wrote down outright. And it gathers
by **tag**: every Task filed under its `Roadmap Tag`, and every knowledge
chapter whose own `roadmap` list names that tag, is reached without the item
naming it at all. The two threads overlap on purpose — a person may both link an
task and tag it — and something reached **both** ways is counted **once**, but
recorded as held by both threads rather than one. A reader deciding whether a link
is safe to remove needs to know a thing is still held by its tag once the named
reference is gone; collapsing that to a single count would make a two-thread hold
look like a one-thread hold and invite deleting the reference that was actually
load-bearing.

Over everything it gathers — named or tagged, each counted once — the item reports
its **total registered effort**: plain arithmetic over the story points that were
actually registered, with no inference and no estimate invented for anything that
registered none. It reports, alongside the total, **how many gathered things
registered no estimate**, because a total that silently dropped unestimated work
would read as smaller than the work in front of the person actually is. The item
owns none of these values — the effort lives on the Tasks and the
knowledge chapters, registered by Tasks and Second Brain — and the
item only reads and adds. The gathering and the totalling are done by
[Roadmap Item Gathering](#roadmap-item-gathering), because neither
answer is in the item's own state.

### Milestone

```meta
type: entity
status: draft
```

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

### Planned Window

```meta
type: value-object
status: draft
```

When an item is intended to run: `start` and `end`, both inclusive. Equality is
by value. Validation: `end >= start`. Derived: `days = end - start + 1`, never
less than one.

Inclusive on both ends because that is how a plan is spoken — "through the 31st"
means the 31st — and because an exclusive end makes the shortest possible piece
of work indistinguishable from no work at all.

### Repository Scope

```meta
type: value-object
status: draft
```

The repositories an item or milestone belongs to: a set of repository aliases,
normalized, without duplicates. Equality is by value. An empty scope is valid and
means unfiled; it is not an error, and not a default repository.

Aliases are the shared key with
[Repository Management](../repository-management/naming.md#repository) and
are held as opaque strings. Resolution is a separate concern — see
[Repository Scope Resolution](#repository-scope-resolution).

### Dependency

```meta
type: value-object
status: draft
```

An edge from the node that must land first to the node that waits for it:
`depends_on_id`, referencing a `Roadmap Node` — an item or a milestone — in the
same plan. Equality is by value, so the same dependency declared twice is one
dependency.

Held on the waiting node rather than in a separate edge list, because "what am I
waiting for" is the question a reader asks of an item, and the direction in which
a plan is edited.

### Planning Lane

```meta
type: value-object
status: draft
```

A free-form row label within a repository band — "platform", "migration",
whatever the person actually calls it. Equality is by value; blank is normalized
to the default lane.

Deliberately a string rather than an enum, for the same reason `area` is on a
[Task](../tasks/domain.md#task): the taxonomy is
the person's, and an enum here would mean shipping a release every time someone
invents a workstream.

### Task Link

```meta
type: value-object
status: draft
```

An optional foreign id naming the
[Task](../tasks/domain.md#task) that executes this
item. Equality is by value. The link may dangle — a task can be deleted while
the plan still intends the work — and a dangling link reads as unlinked rather
than as an error.

### Roadmap Tag

```meta
type: value-object
status: draft
```

The slug other contexts file work under to say it belongs to this item: a
lowercase kebab-case string. Equality is by value. Every Roadmap Item has exactly
one — it is **not optional** — and it is derived from the item's title when the
item is created, then freely editable in its own right.

Two rules make the tag load-bearing rather than cosmetic. The first is that it
**does not change when the title is later renamed.** A tag is a word other
contexts have already written down — a Task filed under it, a knowledge
chapter naming it in its `roadmap` list — and reslugging it on a rename would make
every one of those references silently stop matching, with nothing to report that
it had. So the tag drifts away from the title on purpose; staying put while the
title moves is the whole point of holding it separately rather than deriving it on
read. The second is that a title that slugifies to nothing — punctuation, an
emoji, a script the slug rule strips — falls back to the constant `item` rather
than to an empty tag, so there is always something to file against.

Tags are **not required to be unique** across items. Two items may deliberately
share a tag because the person means to group them, and the plan can report both
the tags in use and the items sitting under a given tag. Uniqueness is therefore
neither enforced nor assumed: a tag names a grouping, not one item, and resolving
one may return several. This is the vocabulary two other contexts borrow — the
[Tasks](../tasks/domain.md#task) tag picker offers every
roadmap tag, and a knowledge chapter names roadmap tags in its `roadmap` list — so
its stability across a rename is a contract with them, not an internal detail.

### Knowledge Ref

```meta
type: value-object
status: draft
```

A direct reference from the item to a knowledge chapter that informs it:
`<path>#<slug>`, naming a chapter in one of the knowledge folders. Equality is by
value; an item may hold several or none.

It is the knowledge counterpart of the `Task Link`, and it behaves the
same way on purpose. The reference may **dangle** — the chapter can be moved,
renamed, or deleted while the plan still points at where it was — and a dangling
ref reads as unresolved rather than as an error. The plan never reads through it
to decide anything about itself; it holds the ref and resolves it only when a
reader asks. Validation and repair are deliberately not done, exactly as the
`Task Link` is already left to dangle, because a plan that refused to
hold a reference to something temporarily missing would lose the intent the
reference recorded.

### Planning Priority

```meta
type: enum
status: draft
```

How much the plan wants this item relative to the others: `low`, `medium`,
`high`, `critical`.

The same four words as [Priority](../tasks/domain.md#priority) in Tasks
, chosen deliberately rather than by accident: two vocabularies for the
same idea would make every conversation about priority start with "which kind".
They remain different values owned by different contexts, and neither overwrites
the other.

### Milestone Kind

```meta
type: enum
status: draft
```

What kind of fixed point it is: `release`, `freeze`, `review`, `commitment`.

Business kinds, not shapes. How each one is drawn is a presentation decision and
belongs to the view, not here.

## Plan Sequencing

```meta
type: domain-service
status: draft
related: [.domain/roadmap/domain.md#roadmap-plan]
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

## Repository Scope Resolution

```meta
type: domain-service
status: draft
related: [.domain/repository-management/naming.md#repository, .domain/environment/domain.md#environment-shortcut-resolution]
```

Turns the opaque aliases in a Repository Scope into the repositories the reader
recognizes, by asking the
[Repository Registry](../repository-management/domain.md#repository-registry),
and reports which aliases did not resolve.

It exists so the aggregate never holds a foreign repository model and never
depends on the registry being reachable. An unresolved alias is a normal outcome
with a normal presentation — an unfiled band — not a failure that stops a plan
from being read. Invocation semantics: query/composition-oriented, on the read
path.

## Roadmap Item Gathering

```meta
type: domain-service
status: draft
related: [.domain/roadmap/domain.md#roadmap-plan, .domain/tasks/domain.md#task, .domain/second-brain/domain.md#knowledge-note]
```

Assembles, for one Roadmap Item, everything it reaches across Tasks
and Second Brain — the task it links and the tasks carrying its tag, the
chapters it references and the chapters naming its tag — deduplicates what the two
threads both reach, and totals the registered story points over the result.

It is a service because none of this is in the item's own state. The Tasks
Entries filed under its tag live in Tasks; the knowledge chapters
naming its tag live in Second Brain; and the effort values are registered by those
contexts, not by the plan. Like
[Repository Scope Resolution](#repository-scope-resolution), it
reads foreign data on the read path and holds none of it — so an unreachable
supplier degrades a total, it does not corrupt a plan.

Two things about the arithmetic are deliberate. A thing reached **both** by a
named reference and by the tag is counted **once**, and carried in the result as
having been reached both ways, so a reader can tell a two-thread hold from a
one-thread one before removing a link. And the total is **only** over story points
that were actually registered: nothing is inferred for an unestimated thing, and
the count of gathered things that registered no estimate is reported next to the
total rather than folded into it, because a number that quietly dropped the
unestimated work would understate it.

Invocation semantics: query/composition-oriented, on the read path. It never
writes — not to the plan, not to a task, not to a chapter.

## RoadmapItemScheduled

```meta
type: domain-event
status: draft
related: [.domain/monitoring/domain.md#progress-signal]
```

Published when a Roadmap Item's Planned Window is set or changed — on creation,
and on every reschedule that actually moves a date.

### Payload

- `roadmap_item_id` — stable identity of the item, unchanged by the reschedule.
- `title` — the item's title at the time of the event, for readability.
- `start`, `end` — the new Planned Window, both days inclusive.
- `previous_start`, `previous_end` — the window it replaced; absent when the item
  is newly planned, which is how a consumer tells the two apart.
- `planning_priority` — the plan's own priority, not any task's.
- `repository_aliases` — the item's Repository Scope as written, unresolved.
- `task_id` — the linked task, when there is one.

### Consumers

- [Monitoring & Dashboard](../monitoring/domain.md#progress-signal) —
  compares the plan against what actually happened, so drift between intent and
  delivery becomes visible without Monitoring reading the plan's internals.

### Published language rules

- The window is always inclusive at both ends. A consumer that treats `end` as
  exclusive will be one day short on every item.
- `repository_aliases` are opaque and unresolved by design. A consumer that needs
  repository facts asks Repository Management, not this payload.
- Absent `previous_start`/`previous_end` means "newly planned". It does not mean
  "unchanged".
- The event carries no status and no progress. Those are Tasks's
  published language; a consumer that wants both correlates on
  `task_id`.
