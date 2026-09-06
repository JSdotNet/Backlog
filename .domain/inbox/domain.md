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

Before triage decides where an item goes, a reader has to be able to see what
it is and where it came from. So an item states its `Content Kind` — what the
captured content *is*, which is a different fact from the `Capture Source`
channel it arrived through: a video is a video whether the YouTube monitor found
it, the phone shared it, or somebody pasted the link. Its `Source` keeps that
channel together with the person who shared it, when someone did. And an item
may carry a `PARA Lean`: the drawer a reader would reach for first, which is how
the queue is read one drawer at a time, without being the routing decision
itself.

The Inbox Item aggregate has no owned child entities; `Tag`, `Routing Target`
and `Source` are value objects owned by the root.

### Invariants

| Rule | Enforced at | Evidence |
|---|---|---|
| The original source link and `captured_at` are preserved unchanged for the life of the item. | all mutations | untested |
| Status only advances through the defined lifecycle (`unprocessed` → `triaged` → routed / deferred / archived). | all mutations | untested |
| A routing decision records exactly one `Routing Target`. | Triage | untested |
| A deferred item resurfaces as `unprocessed` when its `deferred_until` date is reached. | scheduled review | untested |
| Every item has a `Content Kind`; `text` is the kind of an item nobody has looked at. | constructor | untested |
| A `Source` person, when present, is a stored `@name` tag — the sigil is the whole of the difference from a general tag. | constructor | untested |
| A `PARA Lean` is a reading aid and never substitutes for a `Routing Target`: an item with a lean and no routing decision is still `unprocessed`. | all mutations | untested |

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
by canonical `name`. A person is not a tag: `@name` is recorded on `Source`,
not here.

### Source

```meta
type: value-object
status: draft
related: [.domain/capture/domain.md#source-metadata]
```

Where the item came from: the `Capture Source` channel it arrived through and,
optionally, the `person` who shared it as a stored `@name` tag. Two facts and
not one, because they answer different questions — a link shared by a colleague
and the same link clipped alone arrive through different channels and mean
different things to the reader triaging them. The channel is provenance mirrored
from Capture; the person is provenance the channel cannot carry. Immutable once
set; equality is by value.

### Content Kind

```meta
type: enum
status: draft
```

What the captured content is, as a reader sorting the queue would name it —
distinct from `Capture Source`, which says how it arrived:

- `text` — a plain note; the kind every source can produce and the kind an
  item is until somebody says otherwise.
- `article` — a web page worth reading, clipped or linked.
- `link` — a bare URL not yet known to be an article.
- `youtube` — a video, from the YouTube monitor or a shared link.
- `image` — a picture or screenshot.
- `document` — a file: a PDF, an office document, an archive.
- `email` — a newsletter or mail ingested from an IMAP inbox.
- `code` — a selection from an IDE-class host, or a fenced snippet.
- `voice` — a dictated memo from the mobile app.
- `claude-artifact` — an artifact a Claude session produced and shared.

The set will grow; a kind nobody has drawn yet is shown as its plain word rather
than breaking the queue. `article`, `link`, `youtube`, `image`, `document`,
`email` and `claude-artifact` are *reference* kinds — collected material rather
than a thought of the reader's own — which is the distinction PARA files under
Resources.

### PARA Lean

```meta
type: enum
status: draft
related: [.domain/second-brain/domain.md#para-category]
```

The PARA drawer an unprocessed item leans towards — `projects`, `areas`,
`resources` or `archive` — restated here from Second Brain's `PARA Category`
because the Inbox sits upstream of Second Brain and may not reference it. A lean
and not a filing: triage is where an item is actually routed, and the lean only
says which drawer a reader would reach for first, so the queue can be read one
drawer at a time. Absent means nobody has said, and the queue shows such items
as unsorted rather than guessing.

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
