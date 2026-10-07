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
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.AttachmentIntakeTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests.A_plans_task_entries_carry_the_items_attachment_folder, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_captured_picture_is_a_thumbnail_and_a_captured_file_is_a_row_with_open, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_file_that_failed_to_download_shows_why_and_retry_brings_it_down, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests.A_file_not_fetched_yet_says_it_is_waiting_and_a_picture_not_on_disk_is_a_row]
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
task it becomes carries the folder as its attachment. Create plan hands it on
the same way: every task the drafted plan makes carries the folder, and the
drafter itself never sees it.

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

Five acts are offered across the selection: **Archive**, **Move to list**,
**Tags** — add tags, or take one tag off — **Repositories**, and **Move to
backlog**, which routes the selection as one [batch](#route-a-batch-to-tasks).
Each of the first four is the same decision the item's own detail makes,
applied to each picked item in turn, so an item the single-item act would
refuse is refused here too. Tags are added
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
related: [.devbook/domain/inbox/requirements.md#triage-from-the-keyboard, .devbook/design/accessibility.md#keyboard-navigation]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxKeyboardTriageTests]
```

A triage session is a run of small decisions, and it is kept short by taking
them from the keyboard. With the Inbox pane in use, **j** and **k** move to the
next and previous row, **a** archives, **d** offers review dates to defer to —
tomorrow, next week, next month, or without a date — **l** opens Move to list,
**r** moves the item to the backlog, **t** goes to its tags, and **x** picks it
for an act on several at once. The Tasks pane binds no letters, so the one key
the two panes share is **Escape**, which in both leaves the mode the reader is
in. A key offers only the acts the item's detail offers: a routed or archived
item is not archived again from the keyboard.

A key pressed while typing is typing. A letter in a field, anywhere inside a
dialog, with Ctrl, Alt or Meta held, or while the focus is in another pane is
left to what it was pressed in. That is decided in the browser at the moment
the key goes down, because a decision made on the server arrives one key late.

**Triage mode** shows one item at a time, full width, with where it stands in
the rows — "12 of 40" — and the keys under the count. Each decision (archive,
defer, move to a list, move to the backlog) moves on to the next row; past the
last it goes back to the first item still waiting, which is where a reader who
skipped some with **j** left them, and when none is left it says so. Tags and
repositories are not decisions and leave the reader where they are. Escape, or
**Back to the list**, returns to the rows on the item the session stopped at.

The keys are listed in the **Shortcuts** dialog in the pane's header, which **?**
also opens, and the buttons they stand for name them in their titles.

## Classification and enrichment

```meta
type: feature
status: draft
related: [.devbook/domain/inbox/requirements.md#classification-and-enrichment, .devbook/domain/inbox/domain.md#classification]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
setting: .devbook/domain/inbox/context.md#inbox-routing-rules
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.SuggestionTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests]
```

Read what an item is and propose where it goes, so triage starts from a
proposal rather than from nothing. Nothing here is applied without the reader.

On intake, the item's `Content Kind` and source link are read from the
captured text, or from its attachments when it has any.

While an item is open, the detail shows a **Suggested** row above the pickers.
It holds up to three kinds of proposal:

- **Tags.** A `#word` written in the title or the body. Also any tag the
  backlog already files entries under that the text mentions as a whole word.
  The backlog's spelling is used, so the same tag is not spelled two ways. At
  most five tag chips are shown.
- **Repositories.** Every repository whose
  [routing rule](context.md#inbox-routing-rules) the item matches. A tag rule
  reads the tags the item carries, not the ones only suggested. So taking a
  suggested tag can bring its rule's repository onto the row.
- **One destination.**
  - *Archive* for a newsletter: the item offers to unsubscribe.
  - *Move to backlog* for an item that already has a repository, or for a note
    of the reader's own (text, code, a voice memo).
  - *Keep as knowledge* for collected material: an article, a link, a video, a
    picture, a file or a mail.

Each suggestion is a chip with its reason on it. The reader takes a chip with
its number key (1–9, from the list or the detail but never while typing in a
field), with Enter, or with a click. Taking it does what the chip says:

- A tag chip adds the tag.
- A repository chip assigns the repository.
- *Move to backlog* routes the item.
- *Archive* archives it.

The × on a chip turns it down. A turned-down suggestion is recorded on the
item and is never offered for that item again, whatever would propose it
next. A decided item — routed or archived — is offered nothing.

Still modelled and not built:

- *Keep as knowledge* is proposed but cannot be taken, because [Route to
  Devbook](#route-to-devbook) is not built. The chip says so, has no number,
  and can still be turned down.
- Enriching an item with links to related tasks or knowledge notes.
- Suggestions from anything other than these rules: no model reads the item.

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

### Route a batch to Tasks

```meta
type: sub-feature
status: draft
depends-on: [.devbook/domain/inbox/features.md#route-to-tasks, .devbook/domain/inbox/features.md#act-on-several-at-once]
related: [.devbook/domain/inbox/domain.md#batch, .devbook/domain/inbox/requirements.md#route-a-batch-to-tasks, .devbook/domain/tasks/features.md#import, .devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteBatchToBacklogTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests]
```

Route several items to Tasks in one go, as one plan import, so a set of
captures that belong together lands in the backlog as one plan. A batch is
formed in two ways:

- **Move to backlog** in the selection bar routes the picked items. Their plan
  tag is `+inbox-batch-` and eight hex digits.
- **Move list to backlog** in the list's toolbar routes every open item of the
  list the reader has open, after a confirmation that gives the count and the
  tag, `+{list-name}-…`. The list stays; routed items simply leave it. Its
  deferred items stay with it, and the confirmation says so when there are any.

Each item becomes what Route to Tasks would make of it: a draft task per
repository, or one untargeted task, carrying the item's id as its provenance
and its files as the task's attachment. All the entries share the batch's plan
tag. Each item records only the tasks it became.

Some items are named and left out before Tasks is asked, and the rest still go:

- an item already routed or archived;
- an item whose notes carry a top-level `#` heading or an unclosed code fence,
  which would break the document apart (routed on its own, it goes through);
- an item assigned to a repository the workspace no longer knows. A batch never
  registers a repository, just as a single route never does.

Tasks' import takes the rest as one document, whole or not at all. If Tasks
refuses it, nothing is routed, the message says so, and every item stays in the
queue. Only one route runs at a time: a batch waits for a single route to
finish, and a single route waits for a batch.

An item whose entries were made but which could not then be marked routed is
named along with those entries, so routing it again, which would duplicate
them, is not the only way to find them.

Which item waits on which is settled before the batch goes, in
[Before you route](#before-you-route).

### Before you route

```meta
type: sub-feature
status: draft
depends-on: [.devbook/domain/inbox/features.md#route-a-batch-to-tasks]
related: [.devbook/domain/inbox/domain.md#dependency-tier, .devbook/domain/inbox/domain.md#batch, .devbook/domain/tasks/features.md#import]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.ProposeBatchTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.DependencyProposalTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxBatchOrderTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxRouteDraftTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBatchDependencyImportTests]
```

Routing two or more items opens a panel that shows what the batch would do
before it does it: the order the items go in, the plan tag, the task count, the
repositories per item (or one choice for all), the items that will not go and
why, and the dependencies the Inbox can see. A single item routes on the press,
without the panel.

Each dependency is a switch, on by default, that puts one item after another
item of the batch or after an open task already in the backlog. The
[stated](domain.md#dependency-tier) ones are always proposed: the item's own
text names the other thing — another item's source link or title, or an open
task's issue, pull request or source link — and the switch quotes that text. A person
turns off any that are wrong. A loop is named and holds Confirm until a switch
breaks it. Confirm routes the batch once, with the switches left on written as
`after:` tokens; Cancel changes nothing.

### Order a batch with the drafter

```meta
type: sub-feature
status: draft
depends-on: [.devbook/domain/inbox/features.md#before-you-route, .devbook/domain/inbox/features.md#create-plan-from-an-item]
related: [.devbook/domain/inbox/domain.md#dependency-tier, .devbook/domain/inbox/requirements.md#order-a-batch-with-the-drafter, .devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InferBatchOrderTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxRouteDraftTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.InboxPaneTests, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBacklogTargetTests]
```

**Ask the AI to order** in the panel asks the plan drafter which item of the
batch waits on which. It is opt-in per batch: the drafter is asked only on the
press, one model call per press, because every ask costs one. When no drafter
is configured the button stays visible, disabled, titled with the drafter's
reason, as Create plan does.

The drafter is the one Create plan uses, handed the whole batch instead of one
item. It answers in the import grammar
(`.devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md`): one entry
per item, whose `id:` is the item's id and whose `after:` tokens are its
reading of the order. The answer is only read, never imported. It is refused
whole, and no dependency offered, when it names a repository none of the
batch's items goes to — the rule a drafted plan is held to — or an entry or an
`after:` that is not one of the batch's items.

What it reads becomes [inferred](domain.md#dependency-tier) dependencies,
between two items of the batch only, added to the panel as switches marked
AI-inferred and on by default. One the panel already shows between the same two
items is not added again. A person turns off any that are wrong; a loop still
holds Confirm, and Confirm waits while the ask is out. A refused or failed ask
says why in the panel and adds nothing. Confirm routes the batch through
[Route a batch to Tasks](#route-a-batch-to-tasks), as it would without the ask.

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

## Notes

```meta
type: feature
status: draft
related: [.devbook/domain/inbox/domain.md#note, .devbook/domain/inbox/domain.md#content-kind, .devbook/arc42/06-runtime-view.md#mobile-note-sync, .devbook/domain/capture/features.md#talk-note]
```

A note is an Inbox Item the person keeps and edits, rather than a thought
waiting to be sorted. Its `Content Kind` is `note`. Every note reaches the
phone, whichever device made it, and an edit on either side reaches the other.
The phone and the desktop each keep the edit with the later `updated_at`, the
same rule the task feed uses. The sync both ways, the desktop half and the
phone's Notes tab are built.

### Notes on the phone

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/domain.md#note, .devbook/arc42/06-runtime-view.md#mobile-note-sync]
```

The phone's Notes tab lists every note, newest change first. Each row shows the
title, when the note last changed — the time for today, "Yesterday", the
weekday within the week, then the date — and the opening of its body as two
lines of plain text. A search field narrows the list on the phone itself: a
note stays when every word typed is in its title or its body. With no notes the
tab says how to start one; with no match it repeats the words searched for.
**New note** opens an empty editor, and a tap on a note opens it in the same
editor.

The editor holds a title, a Markdown body that dictation appends to, and
attached photos and files — the talk note's tools
(`.devbook/domain/capture/features.md#talk-note`). It has no save button. A
second after typing stops the note is saved; leaving a field or the editor saves
at once, and so does picking a file. A new note is created on its first save
with something in it, so an editor opened and left empty makes nothing. The
line at the top says **Saving...**, then **Saved**, or **Waiting to sync**
while the outbox still holds the note. A note made or changed on the phone is
kept on the phone first and sent through the outbox, so it works with no
signal. A note the desktop archives or deletes leaves the phone's list on the
next pull, and an editor still open on it says its changes are no longer kept.
Pinning, a linked task, a formatting toolbar and turning a line into an Inbox
item are not part of the phone's Notes tab.

### Notes on the desktop

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/domain.md#note, .devbook/domain/inbox/features.md#filter-by-content-kind]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
```

On the desktop a note is an item in the queue like any other. It shows its kind
on its row, with a mark of its own, and the kind chips filter it like any kind.
The reader may edit its title and body in place in the detail column with
**Edit note**, and the edit reaches the phone on the next sync. An archived note
offers no edit, because it has left the phone. The reader may also triage it:
routing, deferring and archiving work as for any item. Routing a note leaves it on
the phone. Archiving or deleting a note removes it from the phone.

### Triage stays on the desktop

```meta
type: sub-feature
status: draft
related: [.devbook/domain/inbox/domain.md#triage, .devbook/domain/capture/features.md#offline-first-sync]
```

The phone never triages. It does not route, defer, accept or archive any Inbox
item, notes included, and it offers no button that would. The phone's Inbox tab
shows only the captures the phone made itself, each with its sync state, until
the desktop takes them in. Deciding what an item becomes is the desktop's job,
because the desktop is where the backlog and the repositories are.
