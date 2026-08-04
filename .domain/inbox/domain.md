# Domain: Inbox

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

The Inbox is the processing queue for all captured input. Items arrive from
[Capture](../capture/domain.md#aggregate-capture) as Inbox Items. The Inbox owns
**what happens to items after they arrive** — triage, classification, and
routing — deciding whether each item becomes a
[Backlog Entry](../backlog/domain.md#aggregate-backlog-entry), a
[Knowledge Note](../second-brain/domain.md#aggregate-knowledge-note), is
deferred, or is archived. It owns no capture sources.

## Aggregate: Inbox Item

```meta
status: draft
related: [.domain/capture/domain.md#aggregate-capture, .arc42/08-crosscutting-concepts.md#shared-data-types]
```

A single unit of captured, unprocessed information moving through triage. The
aggregate guarantees that the original source link and capture timestamp are
always preserved, that status only advances through the defined lifecycle
(`unprocessed` → `triaged` → routed/deferred/archived), and that a routing
decision records exactly one `Routing Target`. Deferred items carry an optional
`deferred_until` review date and resurface as `unprocessed` when it is reached.

### Entities

The Inbox Item aggregate has no owned child entities; `Tag` and `Routing Target`
are value objects owned by the root.

### Value Objects

#### Routing Target

The destination chosen during triage: a target `domain` (backlog, second brain,
archive, deferred), an optional `repo_id`, and the `routed_at` timestamp.
Immutable once set; equality is by value.

#### Tag

A `#keyword` extracted during classification or applied during triage.
`auto_generated` distinguishes suggested tags from user-applied ones. Equality is
by canonical `name`.

### Enums

#### Inbox Status

Lifecycle state of an Inbox Item:

- `unprocessed` — newly received, awaiting triage.
- `triaged` — a triage action has been taken.
- `deferred` — postponed with an optional review date.
- `archived` — dismissed / not actionable.

#### Capture Source

Origin of the item, mirrored from Capture as provenance: `mobile`, `youtube`,
`website`, `email`, `web_clipper`, `ide`, `manual`.

## Domain Service: Triage

```meta
status: draft
related: [.domain/backlog/domain.md#aggregate-backlog-entry, .domain/second-brain/domain.md#aggregate-knowledge-note]
```

Coordinates the triage decision for an Inbox Item and the resulting cross-context
handoff: routing to Backlog (emitting `ItemTriaged` with title, type, tags,
`repo_ids`, `source_inbox_id`), routing to Second Brain (emitting `ItemTriaged`
with title, `body_md`, topic, tags), or setting the item to deferred or archived.
It lives as a service because routing crosses bounded-context boundaries rather
than mutating a single aggregate. Invocation semantics: command-invoked application service triggered by a human or automated triage decision.

## Domain Service: Classification

```meta
status: draft
```

Enriches an unprocessed Inbox Item before or during triage: auto-suggests tags
from content analysis, auto-suggests a routing destination from keywords/patterns,
and applies configured routing rules (source/tag patterns → repo mapping). It is
a service because suggestions draw on rules and analysis external to any single
item's state. Invocation semantics: invoked during intake/triage or by configured queue-processing rules.

## Domain Event: ItemTriaged

```meta
status: draft
related: [.domain/inbox/domain.md#aggregate-inbox-item, .domain/backlog/domain.md#aggregate-backlog-entry, .domain/second-brain/domain.md#aggregate-knowledge-note]
```

Published by `Triage` when an Inbox Item is routed out of the inbox. The route
shape is stable even though the destination-specific fields differ.

### Payload

- `inbox_item_id` - originating Inbox Item identifier.
- `route` - `backlog` or `second-brain`.
- `title` - normalized title.
- `body_md` - normalized body when routing to knowledge.
- `tags` - final tags after classification.
- `repo_ids` - targeted repositories when routing to backlog.
- `type` - requested backlog entry type when routing to backlog.
- `topic` - requested knowledge topic when routing to Second Brain.
- `source_inbox_id` - preserved source id for traceability.
- `triaged_at` - time of the routing decision.

### Consumers

- Backlog Management, which creates a draft `Backlog Entry`.
- Second Brain, which creates a `Knowledge Note`.

### Published language rules

- Inbox owns the event name and field meanings; consumers conform to it instead of
  depending on `Inbox Item` internals.
- Route-specific fields are optional outside their route; consumers ignore fields
  not relevant to their own destination.

## Shared Enums

```meta
status: draft
```

> Enums used by more than one aggregate in this bounded context.

The Inbox has a single aggregate; `Inbox Status` and `Capture Source` are
documented under it. This chapter is reserved for future cross-aggregate enums.
