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
related: [.devbook/domain/capture/features.md#normalized-delivery]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
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
related: [.devbook/domain/inbox/domain.md#capture-source, .devbook/domain/capture/domain.md#capture-source, .devbook/domain/capture/features.md#run-capture-now]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.CaptureItemTests]
```

The queue offers the two ways something gets into it from its own header:
**Add**, for a thing the reader has in their head right now, and **Capture**,
which runs the watched sources. Add asks for a title and, optionally, notes —
nothing else, because the Inbox is where deciding happens and a dialog that
asked where the item goes would be asking for triage before the item exists.
The result is a [Capture (manual)](domain.md#capture-manual), captured and received at the same instant; it lands unfiled in the
queue the reader is filling and opens in the detail beside it rather than
anywhere else. It needs no paired device, which makes it the offline path and
the way an inbox is seeded without a phone.

### Capture attachments

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/requirements.md#capture-attachments, .devbook/domain/inbox/domain.md#attachment, .devbook/domain/inbox/domain.md#attachment-folder, .devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.AttachmentIntakeTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_captured_picture_is_a_thumbnail_and_a_captured_file_is_a_row_with_open, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_file_that_failed_to_download_shows_why_and_retry_brings_it_down, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_file_not_fetched_yet_says_it_is_waiting_and_a_picture_not_on_disk_is_a_row]
```

A thought captured on the phone often comes with a file: a photo, a
screenshot, a PDF. This sub-feature brings those files to the desktop Inbox,
so the reader can see the file while deciding what to do with the item.

The files follow the item. The item arrives first with the list of files the
capture named, and the desktop then downloads each file into the item's
[attachment folder](domain.md#attachment-folder). A file that cannot be
downloaded does not hold up the capture or the rest of the sync. The item still
lands, and the file shows why it did not arrive. Its **Retry** tries it again.

In the detail view, pictures show as thumbnails above the body, and each one
opens the file. Every other file shows as a row with its size and an **Open**
action. A file still on its way reads *Waiting to download*. An item whose
only content is pictures reads as an `image` in the queue, and an item with any
other file reads as a `document`.

When the item is routed to Tasks, its attachment folder goes with it: every
task it becomes carries the folder as its attachment. Create plan does not hand
the folder on yet.

### Organise into lists and groups

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/domain.md#inbox-list, .devbook/domain/inbox/domain.md#inbox-group]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests]
```

A To Do-style side menu beside the queue: a fixed **Inbox** entry for what is
unfiled and a fixed **Deferred** entry for what was put aside (see
[Defer](#defer)), then the reader's own groups and lists, each with a count of
the items still waiting in it. Lists and groups are created, renamed, moved between groups,
ungrouped and deleted in place, from the menu itself; deleting a list returns
its items to the inbox rather than losing them. The organiser is the reader's
own — local to the machine, never synced — and a fresh workspace is seeded once
with a default set to start from.

### Filter by content kind

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/domain.md#content-kind]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
```

One chip per `Content Kind` present in the selected slice, each with its count;
toggling chips narrows the rows to those kinds. The chips are a lens over a
list, never a place an item goes.

## Triage workflow

```meta
type: feature
status: draft
depends-on: [.devbook/domain/inbox/features.md#incoming-queue]
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

### Act on several at once

```meta
type: sub-feature
status: draft
related: [.devbook/domain/tasks/features.md#bulk-editing, .devbook/domain/inbox/requirements.md#act-on-several-at-once, .devbook/domain/inbox/features.md#filter-by-content-kind]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.BatchTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests]
```

Pick several items in the open slice and decide them together, so a batch of
captures that belong in the same place is filed, tagged, targeted or dismissed
once instead of item by item. It mirrors bulk editing in the Tasks pane, so the
two panes pick things the same way: **Select** puts a box beside every row,
Shift extends a run from the last box pressed, and a bar over the list keeps a
running count of what is picked, a box that takes every row shown, and a way
back out.

Four acts are offered across the selection: **Archive**, **Move to list**,
**Tags** — add tags, or take one tag off — and **Repositories**. Each is the
same decision the item's own detail makes, applied to each picked item in turn,
so an item the single-item act would refuse is refused here too. Tags are added
to what each item already carries rather than replacing it; repositories
replace what each item targeted before. An item already at the value is left
alone. The tags and repositories of an item already routed or archived are what
that decision was made with, so they are not changed.

Picking a set is separate from choosing the item the detail shows: one is what
the reader is looking at, the other what the next act will reach.

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
`Content Kind` and source link are read from the captured text on intake, and
from the item's attachments when it has any.

## Routing

```meta
type: feature
status: draft
depends-on: [.devbook/domain/inbox/features.md#triage-workflow]
related: [.devbook/domain/tasks/features.md#task-creation, .devbook/domain/devbook/features.md#knowledge-capture]
```

Routing is the terminal outcome of triage and happens exactly once.

### Route to Tasks

```meta
type: sub-feature
status: draft
related: [.devbook/domain/tasks/features.md#task-creation, .devbook/domain/inbox/domain.md#itemtriaged, .devbook/domain/inbox/domain.md#attachment-folder]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToBacklogTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.AttachmentIntakeTests.Routing_hands_the_items_folder_to_the_task_as_its_attachment]
```

Create one draft task per repository assigned to the item — or a single
untargeted task when none is — each carrying the item's id as its provenance.
An item with attachments gives each task its attachment folder as the task's
attachment. The item records the tasks it became and is routed exactly once; a
failure on the Tasks side leaves it unrouted.

### Create plan from an item

```meta
type: sub-feature
status: draft
depends-on: [.devbook/domain/inbox/features.md#route-to-tasks]
related: [.devbook/domain/tasks/features.md#import, .devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.CreatePlanTests]
```

Ask an AI drafter for an import plan about the item and bring it into the
backlog through the ordinary plan import
(`.devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md`). The plan's entries
land as Draft, whatever the drafter wrote, because nobody has read them yet;
they share a plan tag unique to the item, so two items with one title never
clear each other's plan; and the plan may name only the repositories the item
was assigned — a plan that names another is refused whole. The item is routed
to the entries the import created, the same outcome as Route to Tasks reached
through a different door. When no drafter is configured the action stays
visible, disabled, with its reason.

### Route to Devbook

```meta
type: sub-feature
status: draft
related: [.devbook/domain/devbook/features.md#knowledge-capture]
```

Create a Knowledge Note from the item. Modelled, not built.

### Defer

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/flow.md#inbox-item-lifecycle]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeferAndResurfaceTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.Deferring_with_a_date_moves_the_row_to_the_deferred_slice_and_counts_it_there, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeInboxWiringTests.Every_opening_of_the_inbox_pane_runs_the_resurface_sweep]
```

Put an item aside with an optional **Review on** date from its detail. The item
leaves the Inbox and its list and appears under the side menu's fixed
**Deferred** row, which counts every deferred item wherever it is filed and
lists them soonest review date first, undated last. A deferred item keeps its
list, and it can still be archived or routed. Deferring it again changes the
date, and **Return to inbox** brings it back at once.

It comes back as unprocessed on its own when the review date is reached. The
desktop pane sweeps for due items each time it opens, not on a background
schedule, and a toast says how many came back. An item deferred with no date
waits until a person returns it.

### Archive

```meta
type: sub-feature
status: draft
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_routed_item_cannot_be_archived]
```

Dismiss items that are not actionable while keeping them accessible. An item
that has been routed cannot be archived — routing is the terminal outcome — and
archiving an item that arrived through sync tells the phone to stop offering it.

### Delete

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/requirements.md#delete, .devbook/domain/inbox/features.md#archive, .devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeleteItemTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests]
```

Remove an item for good — a mis-capture, a duplicate, or an archive being
cleared out. It is the opposite of Archive: archive keeps an item findable,
delete keeps nothing, so the detail asks before it acts and says which of the
two it is. Delete is offered on every item, decided or not, because clearing
the archive is exactly what it is for.

An item that arrived through sync still owes the phone its acknowledgement, the
same one archiving sends, so the phone stops offering the capture rather than
sending it back. That acknowledgement is all that outlives the item, and only
until the outbox has pushed it. The item's downloaded files go with it — unless
it was routed, because routing handed that folder to the task.

## Queue health

```meta
type: feature
status: draft
related: [.devbook/domain/monitoring/features.md#inbox-and-queue-health]
```

Track unprocessed count and oldest item age, surface items unprocessed for too
long, and raise configurable alerts when the queue exceeds a threshold.

### Queue health strip

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/requirements.md#queue-health, .devbook/domain/monitoring/features.md#inbox-and-queue-health]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests]
```

The Inbox half of queue health: one line over the queue that says how many
items wait unprocessed, how long ago the oldest was captured, and — only when
there are any — a chip counting those unprocessed for more than fourteen days.
It reads the whole queue, whichever list is open, because how the queue is doing
is not a question about one list. The dashboard half, and the configurable
alerts, are Monitoring's.
