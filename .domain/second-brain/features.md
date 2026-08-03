# Features: Second Brain

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Knowledge capture

```meta
status: draft
depends-on: []
related: [.domain/inbox/features.md#feature-routing]
issue: null
```

Store notes, references, ideas, and learnings as markdown from inbox triage,
manual creation, or import, attaching them to one or more projects, topics, or
tags.

## Feature: PARA organization

```meta
status: draft
depends-on: [.domain/second-brain/features.md#feature-knowledge-capture]
related: []
issue: null
```

Organize notes into Projects (active, deadline), Areas (ongoing), Resources
(reference), and Archive (inactive) buckets.

## Feature: Cross-project linking

```meta
status: draft
depends-on: []
related: []
issue: null
```

Reference multiple projects and repos from a single note, and discover notes
across projects by tag.

## Feature: Topic and tag grouping

```meta
status: draft
depends-on: []
related: []
issue: null
```

Group notes by topic (not just project), support cross-cutting tags, and search
across all knowledge content via the tag index.

## Feature: Bi-directional linking

```meta
status: draft
depends-on: []
related: [.domain/backlog/features.md#feature-search-filter-and-organize]
issue: null
```

Link from backlog entries to notes (reference or embed inline) and from notes
back to related backlog items or projects, supporting queries that cross both
domains and embedding knowledge context directly in backlog item details.
