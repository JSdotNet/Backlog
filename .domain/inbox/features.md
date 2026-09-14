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
feature-flag: inbox-pane
```

Receive normalized Inbox Items from all Capture sources into a single shared
queue. New items default to `unprocessed` and are read newest first by capture
timestamp. Every row leads with its `Content Kind` and its `Source`, so the
reader sees what a thing is and who sent it before deciding on it, and the
selected item opens in a detail view shaped by its kind.

### Add by hand

```meta
type: sub-feature
status: draft
related: [.domain/inbox/domain.md#capture-source, .domain/capture/domain.md#capture-source, .domain/capture/features.md#run-capture-now]
feature-flag: inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.CaptureItemTests]
```

The queue offers the two ways something gets into it from its own header:
**Add**, for a thing the reader has in their head right now, and **Capture**,
which runs the watched sources. Add asks for a title and, optionally, notes —
nothing else, because the Inbox is where deciding happens and a dialog that
asked where the item goes would be asking for triage before the item exists.
The result is an `unprocessed` Inbox Item with channel `manual`, the notes as
its body, captured and received at the same instant; it lands unfiled in the
queue the reader is filling and opens in the detail beside it rather than
anywhere else. It needs no paired device, which makes it the offline path and
the way an inbox is seeded without a phone.

### Organise into lists and groups

```meta
type: sub-feature
status: draft
related: [.domain/inbox/domain.md#inbox-list, .domain/inbox/domain.md#inbox-group]
feature-flag: inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests]
```

A To Do-style side menu beside the queue: a fixed **Inbox** entry for what is
unfiled, then the reader's own groups and lists, each with a count of the open
items it holds. Lists and groups are created, renamed, moved between groups,
ungrouped and deleted in place, from the menu itself; deleting a list returns
its items to the inbox rather than losing them. The organiser is the reader's
own — local to the machine, never synced — and a fresh workspace is seeded once
with a default set to start from.

### Filter by content kind

```meta
type: sub-feature
status: draft
related: [.domain/inbox/domain.md#content-kind]
feature-flag: inbox-pane
```

One chip per `Content Kind` present in the selected slice, each with its count;
toggling chips narrows the rows to those kinds. The chips are a lens over a
list, never a place an item goes.

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

Route to Tasks, store as knowledge, defer, archive, or delete — while tagging,
assigning repositories, and annotating, and preserving the original source link
and capture timestamp.

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
enrich items with links to related tasks or knowledge notes. Built today: the
`Content Kind` and source link are read from the captured text on intake.

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
related: [.domain/tasks/features.md#task-creation, .domain/inbox/domain.md#itemtriaged]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToBacklogTests]
```

Create one draft task per repository assigned to the item — or a single
untargeted task when none is — each carrying the item's id as its provenance.
The item records the tasks it became and is routed exactly once; a failure on
the Tasks side leaves it unrouted.

### Create plan from an item

```meta
type: sub-feature
status: draft
depends-on: [.domain/inbox/features.md#route-to-tasks]
related: [.domain/tasks/features.md#import, .arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.CreatePlanTests]
```

Ask an AI drafter for an import plan about the item and bring it into the
backlog through the ordinary plan import
(`.arc42/adr/0007-import-reuses-the-entry-text-grammar.md`). The plan's entries
land as Draft, whatever the drafter wrote, because nobody has read them yet;
they share a plan tag unique to the item, so two items with one title never
clear each other's plan; and the plan may name only the repositories the item
was assigned — a plan that names another is refused whole. The item is routed
to the entries the import created, the same outcome as Route to Tasks reached
through a different door. When no drafter is configured the action stays
visible, disabled, with its reason.

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
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_routed_item_cannot_be_archived]
```

Dismiss items that are not actionable while keeping them accessible. An item
that has been routed cannot be archived — routing is the terminal outcome — and
archiving an item that arrived through sync tells the phone to stop offering it.

## Queue health

```meta
type: feature
status: draft
related: [.domain/monitoring/features.md#inbox-and-queue-health]
```

Track unprocessed count and oldest item age, surface items unprocessed for too
long, and raise configurable alerts when the queue exceeds a threshold.
