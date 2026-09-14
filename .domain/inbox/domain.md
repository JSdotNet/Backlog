# Inbox

```meta
type: domain
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests]
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

On the desktop the Inbox is its own module (`Backlog.Modules.Inbox`) with its
own store — the `inbox_items`, `inbox_lists` and `inbox_groups` tables in
`backlog.db` — and no longer a projection over draft tasks. A capture that
arrives through sync is a `capture`-kind document on the replica and is handed
to the Inbox's intake before the task merge sees it
(`.arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`).

## Inbox Item

```meta
type: aggregate
status: draft
related: [.domain/capture/domain.md#capture, .arc42/08-crosscutting-concepts.md#shared-data-types, .arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests]
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
channel together with the person who shared it, when someone did.

While an item waits, a reader may prepare it and put it somewhere: assign it
`Tags`, assign the repositories (`repo_ids`) the work will belong to, and file
it in an [Inbox List](#inbox-list) through `list_id`. Filing is organisation,
not triage — an item in a list with no routing decision is still `unprocessed`,
and an archived item may still be moved between lists.

An item is born one of two ways. A capture pulled from the replica becomes an
item that **reuses the capture's id**, which is what makes intake idempotent: a
replayed page or the desktop's own acknowledgement echo finds the row it already
has. A thought typed into the desktop's capture field becomes an item with a
fresh id, channel `manual`, and no replica behind it.

The Inbox Item aggregate has no owned child entities; `Tag`, `Routing Target`
and `Source` are value objects owned by the root.

### Invariants

| Rule | Enforced at | Evidence |
|---|---|---|
| The original source link and `captured_at` are preserved unchanged for the life of the item. | constructor (neither has a setter) | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.The_capture_instant_and_the_source_url_have_no_setter` |
| Status only advances through the defined lifecycle (`unprocessed` → `triaged` → routed / deferred / archived). | `Defer()`, `Resurface()`, `Archive()`, `RouteToBacklog()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.An_item_that_is_still_open_can_be_archived` |
| A routing decision records exactly one `Routing Target`; a second routing is refused. | `RouteToBacklog()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Routing_happens_exactly_once` |
| A routed item is never archived: routing is the terminal outcome of triage. | `Archive()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_routed_item_cannot_be_archived` |
| An archived item is never routed. | `RouteToBacklog()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.An_archived_item_cannot_be_routed` |
| A deferred item resurfaces as `unprocessed` when its `deferred_until` date is reached. | `Resurface()` | untested — the transition exists; nothing schedules it yet |
| Every item has a `Content Kind`; `text` is the kind of an item nobody has looked at, and a kind this build does not know keeps its own word. | constructor, `SetKind()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.An_unknown_kind_slug_survives_as_its_own_word` |
| A `Source` person, when present, is a stored `@name` tag — the sigil is the whole of the difference from a general tag. | constructor | untested |
| A person is never a tag: a `@name` among the tags is refused. | `SetTags()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_person_is_refused_as_a_tag` |
| Tags are stored bare (no `#`) and de-duplicated by canonical name. | `SetTags()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Tags_are_stored_bare_and_deduplicated_by_name` |
| Assigned repositories are distinct without regard to case. | `SetRepoIds()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Repositories_are_distinct_without_regard_to_case` |
| Filing is not triage: `list_id` may change in any state, archived included. | `MoveToList()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Filing_is_allowed_in_every_state_including_archived` |
| An item that arrived through sync carries the capture's own id. | constructor (`FromCapture`) | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_replica_capture_keeps_the_captures_id` |

### Routing Target

```meta
type: value-object
status: draft
```

The destination chosen during triage: a target `domain` (tasks, second brain,
archive), the `repo_ids` the item was assigned when it was routed, the
`task_ids` of the entries Tasks created for it — one per repository, or one
when no repository was assigned — and the `routed_at` timestamp. Immutable once
set; equality is by value. The task ids name aggregates in another context that
may since have been deleted there, which is why they are ids and not references.

### Tag

```meta
type: value-object
status: draft
```

A `#keyword` extracted during classification or applied during triage.
`auto_generated` distinguishes suggested tags from user-applied ones. Equality is
by canonical `name`, without regard to case. A person is not a tag: `@name` is
recorded on `Source`, not here.

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
than a thought of the reader's own.

### Inbox Status

```meta
type: enum
status: draft
```

Lifecycle state of an Inbox Item:

- `unprocessed` — newly received, awaiting triage.
- `triaged` — a triage action has been taken. In this product deciding *is*
  routing, so an item reaches `triaged` as part of being routed and "routed" is
  `triaged` with a `Routing Target` (see `flow.md`).
- `deferred` — postponed with an optional review date.
- `archived` — dismissed / not actionable.

### Capture Source

```meta
type: enum
status: draft
```

Origin of the item, mirrored from Capture as provenance: `mobile`, `youtube`,
`website`, `email`, `web_clipper`, `ide`, `manual`. `manual` is the channel of
an item typed straight into the desktop's capture field — the one channel with
no replica behind it. A channel token nobody recognises is kept as written
rather than folded into a default.

## Inbox List

```meta
type: aggregate
status: draft
related: [.domain/inbox/domain.md#inbox-item, .domain/inbox/domain.md#inbox-group]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests]
```

A named place a reader files waiting items — the leaf of the pane's side menu.
It has a `name`, an `order` among its siblings, and optionally the
[Inbox Group](#inbox-group) it sits in. Its own small root rather than a child
of the item: a list exists before anything is in it and after everything has
left, and deleting one is a write across every item it held (each returns to
the unfiled inbox) rather than a cascade.

Lists are local-only organisation and are hard-deleted — tombstoning exists for
documents that travel (`.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`)
and nothing replicates a list. Names are unique among siblings without regard
to case; that rule is applied by the create and rename commands rather than by
the aggregate, which cannot see its siblings. A workspace with neither lists
nor groups is seeded once with a default organiser (groups `Areas`, `Projects`,
`Archive`; lists `Resources`, `Someday/Maybe`, `Updates`, `Wishlist`).

### Invariants

| Rule | Enforced at | Evidence |
|---|---|---|
| A list has a non-empty name, stored trimmed. | constructor, `Rename()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Renaming_trims_and_refuses_a_blank` |
| Deleting a list returns every item it held to the unfiled inbox. | `DeleteList` command | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Deleting_a_list_returns_its_items_to_the_inbox` |
| An item is only filed in a list that exists. | `MoveToList` command | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Filing_an_item_in_a_list_that_does_not_exist_is_refused` |

## Inbox Group

```meta
type: aggregate
status: draft
related: [.domain/inbox/domain.md#inbox-list]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests]
```

A fold in the side menu that holds lists. It has a `name` and an `order`; the
membership is recorded on the list (`group_id`), not on the group, so a group
is never the owner of what its lists hold. Local-only and hard-deleted, for the
reasons `Inbox List` gives. Ungrouping moves the group's lists to the top level
and then removes the group, so no list is ever deleted by deleting its group.

### Invariants

| Rule | Enforced at | Evidence |
|---|---|---|
| A group has a non-empty name, stored trimmed. | constructor, `Rename()` | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.A_group_is_named_uniquely_and_renamed_under_the_same_rule` |
| Removing a group never removes a list: its lists are moved to the top level first. | `UngroupLists` command | `unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Ungrouping_moves_the_lists_to_the_top_level_and_removes_the_group` |

## Triage

```meta
type: domain-service
status: draft
related: [.domain/tasks/domain.md#task, .domain/second-brain/domain.md#knowledge-note, .arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToBacklogTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.CreatePlanTests]
```

Coordinates the triage decision for an Inbox Item and the resulting cross-context
handoff: routing to Tasks (emitting `ItemTriaged` with title, `body_md`,
`source_url`, tags, `repo_ids`, `source_inbox_id`), routing to Second Brain
(emitting `ItemTriaged` with title, `body_md`, topic, tags), or setting the item
to deferred or archived. It lives as a service because routing crosses
bounded-context boundaries rather than mutating a single aggregate. Invocation
semantics: command-invoked application service triggered by a human or automated
triage decision.

Two doors lead to Tasks and both end in the same `Routing Target`:

- **Route to backlog** creates one draft task per assigned repository — or one
  untargeted task when none is assigned — each stamped with the item's id as
  its `source_inbox_id`. A failure from Tasks leaves the item unrouted.
- **Create plan** asks an AI drafter for an import plan about the item
  (`.arc42/adr/0007-import-reuses-the-entry-text-grammar.md`) and hands it to
  Tasks' import. Every entry carries a plan tag unique to the item —
  `{title-slug}-{last eight hex digits of the item id}` — so two items with one
  title cannot clear each other's plan; the entries are born Draft whatever the
  plan says, because nobody has read them yet; and a plan naming a repository the
  item was not assigned is refused whole before Tasks sees it.

Routing to Second Brain is modelled and not built.

## Classification

```meta
type: domain-service
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.ContentKindDetectorTests]
```

Enriches an unprocessed Inbox Item before or during triage. Built today: on
intake it reads the captured text for its `Content Kind` (a YouTube host, a
Claude host, an image or document extension, a bare URL against a titled one, a
fenced snippet) and its first URL as the source link. Modelled and not built:
auto-suggested tags from content analysis, an auto-suggested routing destination
from keywords/patterns, and configured routing rules (source/tag patterns → repo
mapping). It is a service because suggestions draw on rules and analysis
external to any single item's state. Invocation semantics: invoked during
intake/triage or by configured queue-processing rules.

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
- `body_md` - normalized body.
- `source_url` - the preserved source link, when the capture had one.
- `tags` - final tags after classification; bare names, never a person.
- `repo_ids` - targeted repositories when routing to Tasks; one task per entry,
  one untargeted task when empty.
- `type` - requested task type when routing to Tasks.
- `topic` - requested knowledge topic when routing to Second Brain.
- `source_inbox_id` - preserved source id for traceability.
- `triaged_at` - time of the routing decision.

### Consumers

- Tasks, which creates one draft `Task` per targeted repository, each stamped
  with `source_inbox_id`.
- Second Brain, which creates a `Knowledge Note` (not built).

### Published language rules

- Inbox owns the event name and field meanings; consumers conform to it instead of
  depending on `Inbox Item` internals.
- Route-specific fields are optional outside their route; consumers ignore fields
  not relevant to their own destination.
- On the desktop the Tasks route is realised in-process, not as an asynchronous
  event: the payload is `InboxRouteRequestDto` in `Backlog.Modules.Inbox.Abstractions`,
  carried over the `IInboxBacklogTarget` port and answered by an infrastructure
  adapter over Tasks' published `ITaskItems`. The adapter is the only place an
  inbox item becomes entry text (`.arc42/adr/0002-backlog-module-owns-the-entry-text-language.md`);
  the module hands over facts and never composes a line of markdown. The same
  port carries a drafted plan to Tasks' import, together with the item's
  repositories as the only ones the plan may name.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

`Inbox Status`, `Content Kind` and `Capture Source` belong to the Inbox Item
alone; `Inbox List` and `Inbox Group` carry no enums. This chapter is reserved
for future cross-aggregate enums.
