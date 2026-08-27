# Second Brain

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Knowledge Note

```meta
type: term
status: draft
aliases: [KnowledgeNote, Note]
related: [.domain/second-brain/domain.md#knowledge-note]
```

The durable unit of captured knowledge. "Second Brain" is the context name;
"Knowledge" is the informal shorthand the Inbox uses when routing here.

## PARA Category

```meta
type: term
status: draft
aliases: [PARACategory]
related: [.domain/second-brain/domain.md#para-category]
```

Organizing dimension (projects, areas, resources, archive). `archive` is the
persisted archived state for a note.

## Backlog Link

```meta
type: term
status: draft
aliases: [BacklogLink, backlog_entry_id]
related: [.domain/second-brain/domain.md#backlog-link, .domain/backlog/naming.md#backlog-entry]
```

A reference from a note to a Backlog Entry by id only, keeping the two contexts
decoupled. Uses the `backlog_entry_id` alias of Backlog's entry.

## Project Ref

```meta
type: term
status: draft
aliases: [ProjectRef, repo_id]
related: [.domain/second-brain/domain.md#project-ref]
```

Scopes a note to a repository/project by `repo_id`, aligned with the shared
repository identifier used across contexts.

## Effort

```meta
type: term
status: draft
aliases: [effort, story points, story-point estimate]
related: [.domain/second-brain/domain.md#knowledge-note, .domain/backlog/naming.md#effort]
```

The size of a knowledge chapter in **story points**, carried in its `meta` block:
a non-negative integer, optional, with the same three-valued edges as a Backlog
Entry's effort (absent means "not estimated", `0` is a real estimate, negative is
rejected). It sizes the knowledge work rather than timing it. Registered and owned
here; Roadmap Planning reads and totals it across the chapters an item gathers, but
never sets it.

## Roadmap Contribution

```meta
type: term
status: draft
aliases: [roadmap, roadmap contribution, contributes to]
related: [.domain/second-brain/domain.md#roadmap-contribution, .domain/roadmap/naming.md#roadmap-tag]
```

The [Roadmap Item](../roadmap/naming.md#roadmap-tag) tags a chapter declares
it contributes to, listed in its `meta` block's `roadmap` field. **Distinct from a
`Tag`**: a `Tag` is this context's own `#keyword` for discovery, while a Roadmap
Contribution names a slug owned by Roadmap Planning. It *names* a roadmap item
rather than *addressing* a chapter, so it is not a `<path>#<slug>` reference and
draws no edge in the knowledge graph — it is the thread Roadmap Planning follows
when it gathers knowledge by tag. Nothing is validated; a slug naming no current
item is harmless.

## Instruction Set

```meta
type: term
status: draft
aliases: [instructions, agent instructions, working instructions]
related: [.domain/second-brain/domain.md#instruction-review, .domain/second-brain/features.md#instruction-set-inventory]
```

The documents one tool reads as its instructions for a repository: the file it
loads on every run, the ones it loads when a condition matches, and the skills it
can reach. One repository carries several instruction sets, one per tool. They are
the content of the working-instructions knowledge area and belong to the
repository, not to this product.

## Context Load

```meta
type: term
status: draft
aliases: [context load, always-loaded weight]
related: [.domain/second-brain/features.md#context-load-budget]
```

What an instruction spends on every agent turn because it is loaded whether or not
it applies. It is a property of *when* a document is loaded rather than of how long
it is: a line reached only on the branch that needs it carries almost none.

Distinct from the person's own cost of knowing which document to reach for. That
one buys human judgement and is spent on purpose, so the two are never summed and
never traded against each other silently.

## Instruction Finding

```meta
type: term
status: draft
aliases: [finding, proposal]
related: [.domain/second-brain/domain.md#instruction-review]
```

One reviewed observation about an instruction set: where it is, what kind of
problem it is, and the change proposed. Always a proposal — a finding never means
a file was changed, and an accepted one is applied singly with the previous wording
recoverable.

## Instruction Alignment

```meta
type: term
status: draft
aliases: [alignment, cross-tool alignment]
related: [.domain/second-brain/features.md#cross-tool-alignment-validation]
```

Two tools' instruction sets stating the same rule for the same repository, each in
its own agent, skill, and command names.

**Distinct from duplication**, and the distinction is load-bearing. Duplication is
one meaning stated twice where one place would do, and the fix is to keep one
place. Alignment is one meaning that deliberately has to be stated in each tool's
set, and the fix is never to delete one of them — it is to make them agree, or to
add the rule to the set that is missing it. Documents held in alignment are
maintained as a pair, and a change to one leaves the other unfinished.

## Saving Evidence

```meta
type: term
status: draft
aliases: [evidence basis]
related: [.domain/second-brain/features.md#saving-evidence]
```

How a claimed reduction was obtained: read from local agent activity, or measured
in a controlled before-and-after. It travels with the number so a reader knows what
the number is worth. It is stated in what agents load rather than in money — no
billed amount is claimed, and a figure with no basis is not shown.
