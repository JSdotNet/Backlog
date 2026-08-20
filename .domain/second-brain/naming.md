# Naming: Second Brain

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Term: Knowledge Note

```meta
status: draft
aliases: [KnowledgeNote, Note]
related: [.domain/second-brain/domain.md#aggregate-knowledge-note]
```

The durable unit of captured knowledge. "Second Brain" is the context name;
"Knowledge" is the informal shorthand the Inbox uses when routing here.

## Term: PARA Category

```meta
status: draft
aliases: [PARACategory]
related: [.domain/second-brain/domain.md#para-category]
```

Organizing dimension (projects, areas, resources, archive). `archive` is the
persisted archived state for a note.

## Term: Backlog Link

```meta
status: draft
aliases: [BacklogLink, backlog_entry_id]
related: [.domain/second-brain/domain.md#backlog-link, .domain/backlog/naming.md#term-backlog-entry]
```

A reference from a note to a Backlog Entry by id only, keeping the two contexts
decoupled. Uses the `backlog_entry_id` alias of Backlog's entry.

## Term: Project Ref

```meta
status: draft
aliases: [ProjectRef, repo_id]
related: [.domain/second-brain/domain.md#project-ref]
```

Scopes a note to a repository/project by `repo_id`, aligned with the shared
repository identifier used across contexts.

## Term: Effort

```meta
status: draft
aliases: [effort, story points, story-point estimate]
related: [.domain/second-brain/domain.md#aggregate-knowledge-note, .domain/backlog/naming.md#term-effort]
```

The size of a knowledge chapter in **story points**, carried in its `meta` block:
a non-negative integer, optional, with the same three-valued edges as a Backlog
Entry's effort (absent means "not estimated", `0` is a real estimate, negative is
rejected). It sizes the knowledge work rather than timing it. Registered and owned
here; Roadmap Planning reads and totals it across the chapters an item gathers, but
never sets it.

## Term: Roadmap Contribution

```meta
status: draft
aliases: [roadmap, roadmap contribution, contributes to]
related: [.domain/second-brain/domain.md#roadmap-contribution, .domain/roadmap/naming.md#term-roadmap-tag]
```

The [Roadmap Item](../roadmap/naming.md#term-roadmap-tag) tags a chapter declares
it contributes to, listed in its `meta` block's `roadmap` field. **Distinct from a
`Tag`**: a `Tag` is this context's own `#keyword` for discovery, while a Roadmap
Contribution names a slug owned by Roadmap Planning. It *names* a roadmap item
rather than *addressing* a chapter, so it is not a `<path>#<slug>` reference and
draws no edge in the knowledge graph — it is the thread Roadmap Planning follows
when it gathers knowledge by tag. Nothing is validated; a slug naming no current
item is harmless.
