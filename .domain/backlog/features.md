# Features: Backlog Management

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Backlog entry creation

```meta
status: draft
related: [.domain/inbox/features.md#feature-routing]
```

Create entries manually or from triaged inbox items, capturing title, body, type
(prompt, task, idea, follow-up), tags, and project/repo link. New entries default
to `draft`.

## Feature: Refinement and prioritization

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-backlog-entry-creation]
```

Edit and enrich entries over time: set priority and status, add context links,
and flag oversized items with suggested splits. The desktop experience keeps
Markdown canonical while letting users adjust type, priority, repository,
status, and metadata tags directly from the reading layout. Expanded entries
open into an inline Markdown editor for the full entry body; compact entries can
stay on one line when they only need metadata-level refinement.

![Desktop backlog entry with inline Markdown editing](assets/backlog-entry-inline-markdown-editing.png)

### Sub-feature: Sub-items and steps

```meta
status: draft
```

Break an entry into ordered sub-items with title, status (pending → done), and
notes. Sub-items can be toggled between open and done from the rendered entry,
reorder/add/remove independently, parent progress reflects completion (e.g. 3/5
done), and they can project to GitHub issue task lists.

## Feature: Multi-repo targeting

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-backlog-entry-creation]
related: [.domain/backlog/features.md#feature-projection]
```

Let one logical entry target multiple repositories (`repo_ids[]`) while remaining
a single source of truth — one item, one priority, one status — with a unified
view across all contexts.

## Feature: Projection

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-multi-repo-targeting]
related: [.domain/monitoring/features.md#sub-feature-backlog-and-github-progress]
```

Spawn and close downstream artifacts from an entry: one GitHub issue and/or
Copilot CLI task per target repo, created when work starts and closed on
completion, without duplicating the backlog item.

### Sub-feature: Issue projection and state read-back

```meta
status: draft
related: [.domain/repository-management/features.md#sub-feature-github-access-resolution, .domain/monitoring/features.md#sub-feature-backlog-and-github-progress]
```

Push an entry to its target repository as an issue carrying the entry's title,
body, and tags, and keep the resulting issue reference on the entry so the link
is part of the item rather than a note about it. The entry can then be asked to
re-read that issue's current state, and the pull request that references it, so
downstream progress is visible from the backlog. Reading GitHub state is a
deliberate act rather than a background poll, because the backlog has to open
instantly and offline.

## Feature: Search, filter and organize

```meta
status: draft
related: [.domain/second-brain/features.md#feature-bi-directional-linking]
```

Search across title, body, tags, and linked knowledge notes; filter by area
(a self-chosen grouping such as "repos", "projects", or "inbox"), repo, type,
status, priority, and recency; grouped views; and inline embedding of Second
Brain content.

### Sub-feature: Manual ordering

```meta
status: draft
```

Hand-sequence entries within the backlog by dragging them into a preferred
order, independent of recency or priority. An entry that has never been
manually ranked falls back to recency.

## Feature: Prompt features

```meta
status: draft
```

One-click copy of prompt text to clipboard, usage-history logging on copy/use,
and reopening historical prompts from the usage log.

### Sub-feature: Hand-off to Copilot CLI

```meta
status: draft
related: [.domain/productivity/features.md#sub-feature-ai-activity-capture]
```

Hand an entry to GitHub Copilot CLI as a task brief without retyping it: the
entry's own markdown — title, metadata, body, and sub-items — is the brief, and
the hand-off is recorded in the entry's usage history so the entry itself shows
that AI was put to work on it.

## Feature: AI assistance over the visible backlog

```meta
status: draft
related: [.domain/second-brain/features.md#feature-repository-knowledge-areas, .domain/productivity/features.md#feature-ai-productivity-tracking]
```

Ask questions about the work currently in view and get an answer grounded in it.
The question is answered from the entries the active filters leave visible plus
the loaded backlog knowledge, not from the entire backlog, so the answer matches
what the person is actually looking at. Entries that were opened but never
edited are left out, and the assembled context is capped so a large backlog
degrades into a partial answer rather than a failure. AI assistance is an opt-in
capability and the product remains fully usable with it switched off.

## Feature: Archive and lifecycle

```meta
status: draft
```

Move entries between active and archived states; archived entries are excluded
from default views but always accessible and restorable.

## Feature: Roadmap planning

```meta
status: draft
depends-on: [.domain/backlog/features.md#feature-refinement-and-prioritization]
related: [.domain/backlog/domain.md#aggregate-backlog-entry]
```

Organize selected backlog entries into a forward-looking roadmap without turning
the roadmap into a separate execution system. The roadmap groups planned work by
theme, horizon, target environment, or repository while Backlog Entry remains the
source of truth for status and priority.

### Sub-feature: Roadmap views

```meta
status: draft
```

Show roadmap-ready entries by Now/Next/Later, milestone, or custom planning lane,
with progress derived from the underlying entries instead of maintained manually.