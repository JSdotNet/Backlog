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

## Inbox Item

```meta
type: aggregate
status: draft
related: [.devbook/domain/inbox/domain.invariants.md#inbox-item, .devbook/domain/capture/domain.md#capture, .devbook/arc42/08-crosscutting-concepts.md#shared-data-types, .devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md, .devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests]
aliases: [InboxItem, InboxItemDto, inbox_items, dismissed_suggestions]
```

A single unit of captured, unprocessed information moving through triage. The
aggregate guarantees that the original source link and capture timestamp are
always preserved, that status only advances through the defined lifecycle
(`unprocessed` → `triaged` → routed/deferred/archived), and that a routing
decision records exactly one `Routing Target`. Deferred items carry an optional
`deferred_until` review date and resurface as `unprocessed` when it is reached.
Nothing runs in the background to notice that: the desktop pane sweeps the
store each time it opens, which for one reader on one desktop is every moment
the difference could be seen. A deferral with no date comes back only when a
person returns it.

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

The item also remembers which of [Classification](#classification)'s
suggestions the reader turned down, as `dismissed_suggestions`. Each is kept
by its key: `tag:sync`, `repository:owner/name` or `destination:tasks`. The
key names what was proposed, not why, so a suggestion the reader refused is
not offered again, even when something new would propose it. It sits on the
item because the refusal is the reader's decision about this item.

An item is born one of two ways. A capture pulled from the replica becomes an
item that **reuses the capture's id**, which is what makes intake idempotent: a
replayed page or the desktop's own acknowledgement echo finds the row it already
has. A thought typed into the desktop's Add dialog becomes an item with a
fresh id, channel `manual`, the notes as its body, and no replica behind it.

A capture can also bring files — a photo, a screenshot, a PDF the phone
shared. The item owns them as [Attachments](#attachment): the file belongs to the
thought, so it lives inside the item's boundary and not beside it, and it stays
with the item whatever triage later decides.

The Inbox Item aggregate has no owned child entities; `Tag`, `Routing Target`,
`Source` and `Attachment` are value objects owned by the root.

The rules the item and its attachments are held to are `### Invariant:`
chapters in [`domain.invariants.md`](domain.invariants.md#inbox-item).

### Routing Target

```meta
type: value-object
status: draft
aliases: [RoutingTarget, InboxRoutingDto]
```

The destination chosen during triage: a target `domain` (tasks, devbook,
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

A `#keyword` applied during triage. Equality is by canonical `name`, without
regard to case. A person is not a tag: `@name` is recorded on `Source`, not
here.

`auto_generated` is kept for a tag applied without the reader. Nothing applies
one today. A tag [Classification](#classification) suggests is only a
proposal until the reader takes it, and a tag the reader took is theirs, so it
is stored with `auto_generated` false like one they typed.

### Source

```meta
type: value-object
status: draft
related: [.devbook/domain/capture/domain.md#source-metadata]
aliases: [InboxSource, person]
```

Where the item came from: the `Capture Source` channel it arrived through and,
optionally, the `person` who shared it as a stored `@name` tag. Two facts and
not one, because they answer different questions — a link shared by a colleague
and the same link clipped alone arrive through different channels and mean
different things to the reader triaging them. The channel is provenance mirrored
from Capture; the person is provenance the channel cannot carry. Immutable once
set; equality is by value.

### Attachment

```meta
type: value-object
status: draft
related: [.devbook/domain/inbox/domain.invariants.md#inbox-item, .devbook/domain/inbox/features.md#capture-attachments, .devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests]
aliases: [InboxAttachment, InboxAttachmentDto, inbox_item_attachments, file]
```

One file an item arrived with, and whether this machine has it. It has two
halves, because they come from two places and change at different rates.

- **What the capture said**: the id the phone minted for the file, its name,
  its content type, its size, and the sha256 of its bytes. This is a record of
  what was sent, like the capture instant, so it does not change after it is
  recorded.
- **This machine's copy**: where the file is kept (`local_path`), when it was
  written (`downloaded_at`), and why the last attempt failed (`last_error`).
  This half changes each time the desktop tries to fetch the file. Another
  device keeps its own copy.

The bytes do not travel on the replica. The capture names the file, and the
desktop fetches it from the sync service's attachment store afterwards (local
ADR 0014). So an attachment moves through a short lifecycle of its own:

- **Recorded**: the capture named it and the item saved it before any byte was
  fetched. The pane shows it as *Waiting to download*.
- **Downloaded**: the bytes arrived, matched the recorded sha256, and were
  written into the item's [attachment folder](#attachment-folder).
- **Failed**: the fetch or the write did not succeed, and the reason is kept.
  A failed file waits for the person to press Retry. A replayed sync page does
  not try it again.
- **Retried**: Retry fetches the file again. It ends Downloaded, which clears
  the reason, or Failed again with the new reason.

A file that did not arrive does not make the thought any less captured, so the
item is Received whatever happens to its files. A picture is a file whose
content type is `image/*`. This is the one thing the pane and
[Classification](#classification) ask of a file. Equality is by value. A
change is a new attachment that the item swaps in, and only the item does so.

### Content Kind

```meta
type: enum
status: draft
aliases: [ContentKind, CaptureKinds, kind, KindSlug]
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
aliases: [InboxStatus]
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
related: [.devbook/domain/capture/domain.md#capture-source, .devbook/arc42/adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md]
aliases: [CaptureSource, source, channel]
```

Origin of the item, mirrored from Capture as provenance: `mobile`, `youtube`,
`website`, `email`, `web_clipper`, `ide`, `manual`, `import`. `manual` is the channel of a [Capture (manual)](#capture-manual). `import` is the channel of an item read
from an import manifest. It can arrive filed in the Inbox List the manifest
names, but it is `unprocessed` like any other capture, because filing is not
triage. A channel token nobody recognises is kept as written rather than folded
into a default.

## Inbox List

```meta
type: aggregate
status: draft
related: [.devbook/domain/inbox/domain.md#inbox-item, .devbook/domain/inbox/domain.md#inbox-group, .devbook/domain/inbox/domain.invariants.md#inbox-list]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests]
aliases: [InboxList, InboxListDto, list_id, inbox_lists]
```

A named place a reader files waiting items — the leaf of the pane's side menu.
It has a `name`, an `order` among its siblings, and optionally the
[Inbox Group](#inbox-group) it sits in. Its own small root rather than a child
of the item: a list exists before anything is in it and after everything has
left, and deleting one is a write across every item it held (each returns to
the unfiled inbox) rather than a cascade. The fixed **Inbox** entry above the
lists in the side menu is not a list: it is the items whose `list_id` is empty.

Lists are local-only organisation and are hard-deleted — tombstoning exists for
documents that travel (`.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`)
and nothing replicates a list. Names are unique among siblings without regard
to case; that rule is applied by the create and rename commands rather than by
the aggregate, which cannot see its siblings. A workspace with neither lists
nor groups is seeded once with a default organiser (groups `Areas`, `Projects`,
`Archive`; lists `Resources`, `Someday/Maybe`, `Updates`, `Wishlist`).

## Inbox Group

```meta
type: aggregate
status: draft
related: [.devbook/domain/inbox/domain.md#inbox-list, .devbook/domain/inbox/domain.invariants.md#inbox-group]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests]
aliases: [InboxGroup, InboxGroupDto, group_id, inbox_groups]
```

A fold in the side menu that holds lists. It has a `name` and an `order`; the
membership is recorded on the list (`group_id`), not on the group, so a group
is never the owner of what its lists hold. Local-only and hard-deleted, for the
reasons `Inbox List` gives. Ungrouping moves the group's lists to the top level
and then removes the group, so no list is ever deleted by deleting its group.

## Triage

```meta
type: domain-service
status: draft
related: [.devbook/domain/tasks/domain.md#task, .devbook/domain/devbook/domain.md#knowledge-note, .devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToBacklogTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.CreatePlanTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.AttachmentIntakeTests.Routing_hands_the_items_folder_to_the_task_as_its_attachment]
aliases: [RouteToBacklogCommand, CreatePlanCommand, ArchiveItemCommand]
```

Coordinates the triage decision for an Inbox Item and the resulting cross-context
handoff: routing to Tasks (emitting `ItemTriaged` with title, `body_md`,
`source_url`, tags, `repo_ids`, `source_inbox_id`), routing to Devbook
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
  (`.devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md`) and hands it to
  Tasks' import. Every entry carries the item's [Plan tag](#plan-tag), so two items with one title cannot clear each other's plan; the entries are born Draft whatever the
  plan says, because nobody has read them yet; and a plan naming a repository the
  item was not assigned is refused whole before Tasks sees it.

An item with attachments hands its [attachment folder](#attachment-folder) to
every task Route to backlog creates, as that task's attachment. The file goes
where the work goes. Create plan does not hand the folder on yet.

Routing to Devbook is modelled and not built.

## Classification

```meta
type: domain-service
status: draft
related: [.devbook/domain/inbox/domain.invariants.md#classification, .devbook/domain/inbox/features.md#classification-and-enrichment, .devbook/domain/inbox/context.md#inbox-routing-rules]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.ContentKindDetectorTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.AttachmentIntakeTests.A_picture_and_nothing_else_arrives_as_an_image_and_a_file_as_a_document, unit:dotnet:Backlog.Modules.Inbox.UnitTests.SuggestionTests]
aliases: [ContentKindDetector, InboxClassifier, SuggestQuery, DismissSuggestionCommand, InboxSuggestionDto, suggestion]
```

Reads what an Inbox Item is and proposes where it goes. It has two jobs.

**On intake**, it reads the item's `Content Kind` and its first URL, which
becomes the source link. The kind comes from the captured text: a YouTube
host, a Claude host, an image or document extension, a bare URL against a
titled one, or a fenced snippet. An item with [attachments](#attachment) is
read by its files first. It is an `image` when its only content is pictures
(no body and no link), and a `document` when any file is not a picture.

**While an item is open**, it proposes *suggestions*. It never applies one.
It reads three things:

- **The item itself.** A `#word` in the title or body is a tag suggestion.
- **The backlog's own tags,** through the `IBacklogTagSource` port. One the
  text mentions as a whole word is a tag suggestion, in the backlog's
  spelling.
- **The reader's routing rules,** through the `IInboxRoutingRules` port (the
  [Inbox routing rules](context.md#inbox-routing-rules) setting). The
  repository of each rule the item matches is a repository suggestion.

It also proposes one destination from the item's own facts:

- `archive` for a newsletter.
- `tasks` for an item with a repository, or a note of the reader's own.
- `devbook` for collected material. The `devbook` proposal carries the reason
  it cannot be taken yet: routing to Devbook is not built.

Each suggestion carries a key, the value it would apply, and a sentence saying
why it was proposed. It leaves out anything the item already has, and anything
the reader turned down for it (the item's `dismissed_suggestions`).

Taking a suggestion goes through the ordinary act it names: set the tags,
assign the repositories, route, or archive. Classification adds no second way
to change an item.

It is a service because a suggestion draws on the backlog and on the reader's
rules, which are outside any single item's state.

Modelled and not built:

- Enriching an item with links to related tasks or knowledge notes.
- A rule that applies a decision by itself: every proposal waits for the
  reader.

Invocation semantics:

- On intake, it is called by the intake and capture commands.
- Otherwise it is a query (`SuggestQuery`) the pane asks for whenever the item
  on screen changes.
- A refusal is the `DismissSuggestionCommand`.

## ItemTriaged

```meta
type: domain-event
status: draft
related: [.devbook/domain/inbox/domain.md#inbox-item, .devbook/domain/tasks/domain.md#task, .devbook/domain/devbook/domain.md#knowledge-note]
```

Published by `Triage` when an Inbox Item is routed out of the inbox. The route
shape is stable even though the destination-specific fields differ.

### Payload

- `inbox_item_id` - originating Inbox Item identifier.
- `route` - `tasks` or `devbook`.
- `title` - normalized title.
- `body_md` - normalized body.
- `source_url` - the preserved source link, when the capture had one.
- `tags` - final tags after classification; bare names, never a person.
- `repo_ids` - targeted repositories when routing to Tasks; one task per entry,
  one untargeted task when empty.
- `type` - requested task type when routing to Tasks.
- `topic` - requested knowledge topic when routing to Devbook.
- `source_inbox_id` - preserved source id for traceability.
- `triaged_at` - time of the routing decision.

### Consumers

- Tasks, which creates one draft `Task` per targeted repository, each stamped
  with `source_inbox_id`.
- Devbook, which creates a `Knowledge Note` (not built).

### Published language rules

- Inbox owns the event name and field meanings; consumers conform to it instead of
  depending on `Inbox Item` internals.
- Route-specific fields are optional outside their route; consumers ignore fields
  not relevant to their own destination.
- On the desktop the Tasks route is realised in-process, not as an asynchronous
  event: the payload is `InboxRouteRequestDto` in `Backlog.Modules.Inbox.Abstractions`,
  carried over the `IInboxBacklogTarget` port and answered by an infrastructure
  adapter over Tasks' published `ITaskItems`. The adapter is the only place an
  inbox item becomes entry text (`.devbook/arc42/adr/0002-backlog-module-owns-the-entry-text-language.md`);
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

## Ubiquitous Language

```meta
type: ubiquitous-language
```

### Capture (manual)

```meta
type: term
status: draft
aliases: [CaptureItemCommand, manual, ManualChannel]
related: [.devbook/domain/inbox/domain.md#capture-source, .devbook/domain/inbox/features.md#add-by-hand]
```

A thought typed straight into the desktop through the Inbox's Add dialog: a
title and, optionally, notes that become the item's body. It becomes an
`unprocessed` Inbox Item with channel `manual` and no replica behind it —
`CaptureItemCommand` is the slice, `InboxEnumMap.ManualChannel` the token.
Distinct from a `Capture` in the Capture context, which arrives from a device
through sync.

### Plan tag

```meta
type: term
status: draft
aliases: [PlanTag, import_plan_id]
related: [.devbook/domain/inbox/domain.md#triage, .devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
```

The `#tag` every entry of a plan drafted from an item shares, which Tasks reads
as the plan's identity (`import_plan_id`). Shaped `{title-slug}-{last eight hex
digits of the item id}`, at most forty characters, so it is unique per item and
a legal tag on the metadata line.

### Attachment folder

```meta
type: term
status: draft
aliases: [AttachmentPath, _inbox/attachments]
related: [.devbook/domain/inbox/domain.md#attachment, .devbook/domain/inbox/domain.md#triage]
```

The folder on this machine that holds one item's downloaded files:
`<workspace>/_inbox/attachments/<item id>/`. Each file has one name there, so
a retry writes to the same place the first attempt did. Routing hands the
folder to every task the item becomes, as the task's single attachment
(`AttachmentPath`). The task then points at the files and does not copy them.
