# Inbox

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

The Inbox is the processing queue for all captured input. Items arrive from
[Capture](../capture/domain.md#capture) as Inbox Items. The Inbox owns
**what happens to items after they arrive** — triage, classification, and
routing — deciding whether each item becomes a
[Task](../tasks/domain.md#task), a
[Knowledge Note](../second-brain/domain.md#knowledge-note), is
deferred, or is archived. It owns no capture sources.

## Inbox Item

```meta
type: aggregate
status: draft
related: [.domain/capture/domain.md#capture, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

A single unit of captured, unprocessed information moving through triage. The
aggregate guarantees that the original source link and capture timestamp are
always preserved, that status only advances through the defined lifecycle
(`unprocessed` → `triaged` → routed/deferred/archived), and that a routing
decision records exactly one `Routing Target`. Deferred items carry an optional
`deferred_until` review date and resurface as `unprocessed` when it is reached.

The Inbox Item aggregate has no owned child entities; `Tag` and `Routing Target`
are value objects owned by the root.

### Routing Target

```meta
type: value-object
status: draft
```

The destination chosen during triage: a target `domain` (tasks, second brain,
archive, deferred), an optional `repo_id`, and the `routed_at` timestamp.
Immutable once set; equality is by value.

### Tag

```meta
type: value-object
status: draft
```

A `#keyword` extracted during classification or applied during triage.
`auto_generated` distinguishes suggested tags from user-applied ones. Equality is
by canonical `name`.

### Inbox Status

```meta
type: enum
status: draft
```

Lifecycle state of an Inbox Item:

- `unprocessed` — newly received, awaiting triage.
- `triaged` — a triage action has been taken.
- `deferred` — postponed with an optional review date.
- `archived` — dismissed / not actionable.

### Capture Source

```meta
type: enum
status: draft
```

Origin of the item, mirrored from Capture as provenance: `mobile`, `youtube`,
`website`, `email`, `web_clipper`, `ide`, `manual`.

## Triage

```meta
type: domain-service
status: draft
related: [.domain/tasks/domain.md#task, .domain/second-brain/domain.md#knowledge-note]
```

Coordinates the triage decision for an Inbox Item and the resulting cross-context
handoff: routing to Tasks (emitting `ItemTriaged` with title, type, tags,
`repo_ids`, `source_inbox_id`), routing to Second Brain (emitting `ItemTriaged`
with title, `body_md`, topic, tags), or setting the item to deferred or archived.
It lives as a service because routing crosses bounded-context boundaries rather
than mutating a single aggregate. Invocation semantics: command-invoked application service triggered by a human or automated triage decision.

## Classification

```meta
type: domain-service
status: draft
```

Enriches an unprocessed Inbox Item before or during triage: auto-suggests tags
from content analysis, auto-suggests a routing destination from keywords/patterns,
and applies configured routing rules (source/tag patterns → repo mapping). It is
a service because suggestions draw on rules and analysis external to any single
item's state. Invocation semantics: invoked during intake/triage or by configured queue-processing rules.

## ItemTriaged

```meta
type: domain-event
status: draft
related: [.domain/inbox/domain.md#inbox-item, .domain/tasks/domain.md#task, .domain/second-brain/domain.md#knowledge-note]
```

Published by `Triage` when an Inbox Item is routed out of the inbox. The route
shape is stable even though the destination-specific fields differ.

### Payload

- `inbox_item_id` - originating Inbox Item identifier.
- `route` - `tasks` or `second-brain`.
- `title` - normalized title.
- `body_md` - normalized body when routing to knowledge.
- `tags` - final tags after classification.
- `repo_ids` - targeted repositories when routing to Tasks.
- `type` - requested task type when routing to Tasks.
- `topic` - requested knowledge topic when routing to Second Brain.
- `source_inbox_id` - preserved source id for traceability.
- `triaged_at` - time of the routing decision.

### Consumers

- Tasks, which creates a draft `Task`.
- Second Brain, which creates a `Knowledge Note`.

### Published language rules

- Inbox owns the event name and field meanings; consumers conform to it instead of
  depending on `Inbox Item` internals.
- Route-specific fields are optional outside their route; consumers ignore fields
  not relevant to their own destination.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

The Inbox has a single aggregate; `Inbox Status` and `Capture Source` are
documented under it. This chapter is reserved for future cross-aggregate enums.
