# Features: Second Brain

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Knowledge capture

```meta
status: draft
related: [.domain/inbox/features.md#feature-routing]
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags.

## Feature: PARA organization

```meta
status: draft
depends-on: [.domain/second-brain/features.md#feature-knowledge-capture]
```

Organize notes into Projects (active, deadline), Areas (ongoing), Resources
(reference), and Archive (inactive) buckets.

## Feature: Cross-project linking

```meta
status: draft
```

Reference multiple projects and repos from a single note, and discover notes
across projects by tag.

## Feature: Topic and tag grouping

```meta
status: draft
```

Group notes by topic (not just project), support cross-cutting tags, and search
across all knowledge content via the tag index.

## Feature: Bi-directional linking

```meta
status: draft
related: [.domain/backlog/features.md#feature-search-filter-and-organize]
```

Link from backlog entries to notes (reference or embed inline) and from notes
back to related backlog items or projects, supporting queries that cross both
domains and embedding knowledge context directly in backlog item details.

## Feature: Repository knowledge areas

```meta
status: draft
feature-flag: repository-knowledge
related: [.domain/repository-management/features.md#sub-feature-repository-knowledge-folder-settings, .domain/backlog/features.md#feature-search-filter-and-organize]
```

Read the knowledge a repository already carries alongside its code, next to the
backlog rather than in a separate tool. Knowledge is grouped into named areas —
working instructions, domain, architecture, technology, and design — each backed
by the repository's own folder for that subject. Backlog concerns are not one of
them: they are their own workspace section, read and written rather than browsed,
so a repository's backlog folder is not a knowledge area. Areas are browsed from
a side pane that sits beside the entry list so knowledge and work stay in view
together, and the pane's width is adjustable because the two compete for the same
screen.

### Sub-feature: Area selection and scope

```meta
status: draft
feature-flag: knowledge-sections
```

Switch between areas, and between repositories when more than one is registered,
so the knowledge shown always belongs to a known repository. Every area is
opt-in, and each can be switched off on its own or pointed at a non-standard
folder; when none is left on there is nothing to browse and the pane says so
rather than offering an empty tab strip.

### Sub-feature: Rendered knowledge documents

```meta
status: draft
```

Present each area's documents as readable content rather than raw files:
headings and sections, the metadata each chapter declares, cross-references
between knowledge documents, and embedded diagrams rendered as diagrams. A
cross-reference may name a chapter in the repository's backlog folder even though
that folder is not a browsable area, and it is read with that folder's own status
vocabulary rather than as an unknown one.
