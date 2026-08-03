# Features: Inbox

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Incoming queue

```meta
status: draft
depends-on: []
related: [.domain/capture/features.md#feature-normalized-delivery]
issue: null
```

Receive normalized Inbox Items from all Capture sources into a single shared
queue. New items default to `unprocessed` and are ordered by capture timestamp
(configurable).

## Feature: Triage workflow

```meta
status: draft
depends-on: [.domain/inbox/features.md#feature-incoming-queue]
related: []
issue: null
```

Review unprocessed items one by one or in batch and take an action per item.

### Sub-feature: Per-item triage actions

```meta
status: draft
depends-on: []
related: []
issue: null
```

Route to backlog, store as knowledge, defer, archive, or delete — while tagging
and annotating, and preserving the original source link and capture timestamp.

### Sub-feature: Quick-triage shortcuts

```meta
status: draft
depends-on: []
related: []
issue: null
```

Keyboard/shortcut actions for common routing patterns to speed up triage.

## Feature: Classification and enrichment

```meta
status: draft
depends-on: []
related: []
issue: null
```

Auto-suggest tags from content analysis, auto-suggest a routing destination from
keywords/patterns, apply routing rules (source patterns → repo mapping), and
enrich items with links to related backlog items or knowledge notes.

## Feature: Routing

```meta
status: draft
depends-on: [.domain/inbox/features.md#feature-triage-workflow]
related: [.domain/backlog/features.md#feature-backlog-entry-creation, .domain/second-brain/features.md#feature-knowledge-capture]
issue: null
```

Move a triaged item to its destination.

### Sub-feature: Route to Backlog

```meta
status: draft
depends-on: []
related: [.domain/backlog/features.md#feature-backlog-entry-creation]
issue: null
```

Create a Backlog Entry draft from the item.

### Sub-feature: Route to Second Brain

```meta
status: draft
depends-on: []
related: [.domain/second-brain/features.md#feature-knowledge-capture]
issue: null
```

Create a Knowledge Note from the item.

### Sub-feature: Defer

```meta
status: draft
depends-on: []
related: []
issue: null
```

Postpone the item with an optional remind-at date; it resurfaces as unprocessed
when the review date is reached.

### Sub-feature: Archive

```meta
status: draft
depends-on: []
related: []
issue: null
```

Dismiss items that are not actionable while keeping them accessible.

## Feature: Queue health

```meta
status: draft
depends-on: []
related: [.domain/monitoring/features.md#sub-feature-inbox-and-queue-health]
issue: null
```

Track unprocessed count and oldest item age, surface items unprocessed for too
long, and raise configurable alerts when the queue exceeds a threshold.
