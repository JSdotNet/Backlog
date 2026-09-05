# Inbox

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Incoming queue

```meta
type: feature
status: draft
related: [.domain/capture/features.md#normalized-delivery]
```

Receive normalized Inbox Items from all Capture sources into a single shared
queue. New items default to `unprocessed` and are ordered by capture timestamp
(configurable).

### Read the queue as PARA drawers

```meta
type: sub-feature
status: draft
related: [.domain/inbox/domain.md#para-lean, .domain/inbox/domain.md#content-kind, .domain/second-brain/domain.md#para-category]
feature-flag: inbox-pane
```

PARA is the queue's structure, not one way of grouping it. The queue is always
read as drawers — Projects, Areas, Resources, Archive, and the Unsorted that
PARA does not name — each folding with its count, so a queue of forty reads as
five drawers. A drawer is sectioned by what it is made of: Projects per project,
Areas per area; the others are one list. Tag and repository are lenses that
section the rows *inside* every drawer and never replace the drawers, because a
tag is something an item has and a drawer is where it goes. Every row leads with
its `Content Kind` and its `Source`, so the reader sees what a thing is and who
sent it before deciding on it.

## Triage workflow

```meta
type: feature
status: draft
depends-on: [.domain/inbox/features.md#incoming-queue]
```

Review unprocessed items one by one or in batch and take an action per item.

### Per-item triage actions

```meta
type: sub-feature
status: draft
```

Route to Tasks, store as knowledge, defer, archive, or delete — while tagging
and annotating, and preserving the original source link and capture timestamp.

### Quick-triage shortcuts

```meta
type: sub-feature
status: draft
```

Keyboard/shortcut actions for common routing patterns to speed up triage.

## Classification and enrichment

```meta
type: feature
status: draft
```

Auto-suggest tags from content analysis, auto-suggest a routing destination from
keywords/patterns, apply routing rules (source patterns → repo mapping), and
enrich items with links to related tasks or knowledge notes.

## Routing

```meta
type: feature
status: draft
depends-on: [.domain/inbox/features.md#triage-workflow]
related: [.domain/tasks/features.md#task-creation, .domain/second-brain/features.md#knowledge-capture]
```

Move a triaged item to its destination.

### Route to Tasks

```meta
type: sub-feature
status: draft
related: [.domain/tasks/features.md#task-creation]
```

Create a Task draft from the item.

### Route to Second Brain

```meta
type: sub-feature
status: draft
related: [.domain/second-brain/features.md#knowledge-capture]
```

Create a Knowledge Note from the item.

### Defer

```meta
type: sub-feature
status: draft
```

Postpone the item with an optional remind-at date; it resurfaces as unprocessed
when the review date is reached.

### Archive

```meta
type: sub-feature
status: draft
```

Dismiss items that are not actionable while keeping them accessible.

## Queue health

```meta
type: feature
status: draft
related: [.domain/monitoring/features.md#inbox-and-queue-health]
```

Track unprocessed count and oldest item age, surface items unprocessed for too
long, and raise configurable alerts when the queue exceeds a threshold.
