# Second Brain

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Knowledge capture

```meta
type: feature
status: draft
related: [.domain/inbox/features.md#routing]
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags.

## PARA organization

```meta
type: feature
status: draft
depends-on: [.domain/second-brain/features.md#knowledge-capture]
```

Organize notes into Projects (active, deadline), Areas (ongoing), Resources
(reference), and Archive (inactive) buckets.

## Cross-project linking

```meta
type: feature
status: draft
```

Reference multiple projects and repos from a single note, and discover notes
across projects by tag.

## Topic and tag grouping

```meta
type: feature
status: draft
related: [.domain/second-brain/domain.md#tag, .domain/second-brain/domain.md#roadmap-contribution]
```

Group notes by topic (not just project), support cross-cutting tags, and search
across all knowledge content via the tag index.

These discovery tags are the context's own `#keyword`s and are a different thing
from a chapter's **roadmap contribution** — the roadmap-item tags a chapter names
in its `roadmap` metadata to say which planned work it feeds. A discovery tag finds
notes here; a roadmap contribution is read by Roadmap Planning when it gathers work
by tag, draws no edge, and is never confused with a `#keyword` even though both are
loosely "tags". A chapter may also declare an `effort` in story points, sized the
same way a Backlog Entry is; like the roadmap contribution it is registered here
and only read by Roadmap Planning.

## Bi-directional linking

```meta
type: feature
status: draft
related: [.domain/backlog/features.md#search-filter-and-organize]
```

Link from backlog entries to notes (reference or embed inline) and from notes
back to related backlog items or projects, supporting queries that cross both
domains and embedding knowledge context directly in backlog item details.

## Repository knowledge areas

```meta
type: feature
status: draft
feature-flag: repository-knowledge
related: [.domain/repository-management/features.md#repository-knowledge-folder-settings, .domain/backlog/features.md#search-filter-and-organize]
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

### Area selection and scope

```meta
type: sub-feature
status: draft
feature-flag: knowledge-sections
```

Switch between areas, and between repositories when more than one is registered,
so the knowledge shown always belongs to a known repository. Every area is
opt-in, and each can be switched off on its own or pointed at a non-standard
folder; when none is left on there is nothing to browse and the pane says so
rather than offering an empty tab strip.

### Rendered knowledge documents

```meta
type: sub-feature
status: draft
```

Present each area's documents as readable content rather than raw files:
headings and sections, the metadata each chapter declares, cross-references
between knowledge documents, and embedded diagrams rendered as diagrams. A
cross-reference may name a chapter in the repository's backlog folder even though
that folder is not a browsable area, and it is read with that folder's own status
vocabulary rather than as an unknown one.
