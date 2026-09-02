# Second Brain

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

Second Brain is a personal project knowledge base — not a task queue. It
collects, organizes, and retrieves Knowledge Notes structured with the PARA
framework (Projects, Areas, Resources, Archive), and links bidirectionally with
[Tasks](../tasks/domain.md#task) so related
context is easy to find later.

## Knowledge Note

```meta
type: aggregate
status: draft
related: [.domain/tasks/domain.md#task, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

An organized unit of project knowledge stored as markdown and the boundary for
its own organization and links. Invariants: a note carries a primary `topic` and
a `PARA Category`; it may be scoped to zero or more projects/repos; links to
tasks and other notes are managed through the root; and status advances
through the note lifecycle (created → organized → linked → archived, with
restore). `updated_at` moves forward on every change.

A knowledge chapter — this aggregate, and equally any chapter Second Brain renders
from a repository's knowledge folders — may also carry two pieces of metadata in
its `meta` block that describe how it relates to planned work. The first is an
optional `effort`: a size in **story points**, a non-negative integer with exactly
the meaning it has on a
[Task](../tasks/domain.md#task) — absent means "not
estimated", `0` is a real zero-point estimate, a negative is rejected, and it
sizes the knowledge work rather than timing it. The second is a `roadmap` list:
the [Roadmap Item](../roadmap/domain.md#roadmap-item) tags this chapter declares it
contributes to, held as a `Roadmap Contribution`. Both are read by Roadmap
Planning when a Roadmap Item gathers and totals the work behind it; Second Brain
registers them and owns them, and Roadmap only reads.

The Knowledge Note aggregate has no independently identified child entities;
`Project Ref`, `Tag`, `Roadmap Contribution`, and `Task Link` are value objects
owned by the root.

### Project Ref

```meta
type: value-object
status: draft
```

A scope link to a project/repo: `repo_id` and `project_name`. Equality is by
value; a note may hold several.

### Task Link

```meta
type: value-object
status: draft
```

A typed link to a Task: `task_id`, `link_type`
(reference or embed), and `linked_at`. Immutable; equality is by value.

### Tag

```meta
type: value-object
status: draft
```

A `#keyword` for cross-cutting discovery (e.g. `#architecture`, `#performance`).
Equality is by canonical `name`.

It is **this context's own** vocabulary: a person invents a `#keyword` to find
notes across projects later, and it means whatever they use it to mean. It is not
a `Roadmap Contribution`, and the two must not be collapsed even though both are
loosely "tags". A `Tag` is a discovery keyword owned here; a `Roadmap Contribution`
names a slug owned by [Roadmap Planning](../roadmap/domain.md#roadmap-tag). One is
for finding knowledge, the other for declaring which planned work a chapter feeds.

### Roadmap Contribution

```meta
type: value-object
status: draft
```

A [Roadmap Item](../roadmap/domain.md#roadmap-item) tag this chapter declares it
contributes to: a lowercase kebab-case slug, held in the chapter's `roadmap` list.
A note may declare several, or none. Equality is by value.

It **names** a roadmap item rather than **addressing** a chapter, and that
distinction is the one most easily misread. A `Task Link` and a knowledge
cross-reference are `<path>#<slug>` addresses that resolve to a specific node and
draw an edge in the knowledge graph; a `Roadmap Contribution` is a bare tag slug
that resolves to nothing here at all — it is the same vocabulary a
[Roadmap Item](../roadmap/domain.md#roadmap-tag) holds and a Task files
under, and it is Roadmap Planning that reads a chapter's contributions when it
gathers work by tag. Because it names rather than addresses, it produces **no
graph edge**, exactly like an alias: a chapter contributing to `sync-service` is
not linked to any document called that. Nothing is validated and a slug matching
no current roadmap item is a normal, harmless state.

### PARA Category

```meta
type: enum
status: draft
```

Organization bucket for a note:

- `projects` — active work with a goal and deadline.
- `areas` — ongoing responsibilities without a deadline.
- `resources` — reference material and collected knowledge.
- `archive` — inactive items preserved for future search.

### Note Source

```meta
type: enum
status: draft
```

Origin of the note: `inbox` (routed from triage), `manual`, or `import`.

## Cross-Linking

```meta
type: domain-service
status: draft
related: [.domain/tasks/domain.md#task]
```

Maintains bi-directional links between Knowledge Notes and Tasks and
supports queries that span both domains (search backlog + knowledge together,
embed knowledge snippets into backlog details). It is a service because a link
has two owners in different contexts and must be kept consistent from both sides
rather than living inside a single aggregate. Invocation semantics: consistency policy invoked when note/backlog links are created, removed, or reconciled.

## Instruction Review

```meta
type: domain-service
status: draft
related: [.domain/second-brain/features.md#instruction-optimization, .domain/repository-management/features.md#repository-knowledge-folder-settings]
```

Reviews the agent-facing instruction documents a repository carries — the
working-instructions knowledge area, per tool — and produces findings about what
they cost, where they disagree, and which wording is not doing its job. It is a
service rather than an aggregate because the documents belong to the repository and
are read through the knowledge-folder settings
[Repository Management](../repository-management/features.md#repository-knowledge-folder-settings)
owns: several tools' instruction sets are examined as one subject, and no aggregate
here may hold their content without becoming a second, staler copy of it.

A finding names a location, the kind of problem, and the change proposed for it.
The kinds are: context load carried by an always-loaded document; one meaning
stated in more than one place; a disagreement or an omission between the sets two
tools read; wording that will not reliably make an agent reach the document it
names; wording that steers by prohibition where stating the wanted behaviour would
steer better; guidance the agent already follows unprompted; guidance that has gone
stale; and guidance that copies what the repository's own configuration already
states.

Every finding is a **proposal**. Accepting one edits the repository's own file and
the repository stays its owner: the previous wording remains recoverable exactly,
and code blocks, commands, paths, and error strings are carried through a rewrite
unchanged. A change that shrinks an instruction set at the cost of how well the
agents work is a failed change rather than a saving, which is why an applied
proposal is measured afterwards and can be withdrawn. A claimed reduction carries
the basis it was obtained on — observed from local agent activity, or measured in a
controlled comparison — and is expressed in what agents load rather than in money.

Findings live for the review that produced them. Nothing here stores instruction
content, a second copy of a repository's rules, or a verdict that outlives the
documents it was drawn from.

Invocation semantics: command-invoked read-only analysis over a repository's
instruction documents; it writes only through a proposal a person has accepted, one
at a time.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Second Brain has a single aggregate; `PARA Category` and `Note Source` are
documented under it. This chapter is reserved for future cross-aggregate enums.
