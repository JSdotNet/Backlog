# Naming: Second Brain

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
