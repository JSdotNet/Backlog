# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Knowledge Note

```meta
status: draft
type: invariants
related: [.devbook/domain/devbook/domain.md#knowledge-note]
```

> The rules a Knowledge Note holds its area and its links to, the areas its Brain
> declares, and the planning fields any devbook chapter carries.

### Invariant: A note sits in one area

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.A_note_s_folder_is_its_area_and_no_devbook_convention_applies, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.Para_repos_and_tasks_are_node_attributes]
```

A note sits in exactly one area, its [PARA Category](domain.md#para-category): the top folder it sits in, or the area its `para` field names instead.
A chapter inside a note is a node of its own, and may name an area of its own.

Enforced at: `DevbookGraphBuilder.ApplyBrainFields()` (one `para` per node)

### Invariant: Links change through the note

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

A note's links to tasks and to other notes change only through the note.

Enforced at: open

### Invariant: The change time moves forward

```meta
status: draft
type: invariant
```

```annotation
kind: question
author: "flow-spec"
date: 2026-10-13
body: |
  Not built: no type in src/ enforces this yet. Which type and transition will?
```

A note's `updated_at` moves forward on every change.

Enforced at: open

### Invariant: A roadmap contribution draws no graph edge

```meta
status: draft
type: invariant
```

A [Roadmap Contribution](domain.md#roadmap-contribution) is kept on the chapter's node as a name and never becomes an edge in the knowledge graph.
It names a roadmap item rather than addressing a chapter, so there is no node for it to point at.

Enforced at: `DevbookGraphBuilder.ApplyMeta()`, which keeps `roadmap` as a node attribute and leaves it out of `ReferenceFields`

### Invariant: An area is one plain folder, declared once

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_folder_that_is_not_one_plain_segment_is_skipped, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_folder_declared_twice_counts_once]
```

Every area `CONVENTIONS.md` declares is a distinct folder of one plain segment: a path, `..`, a dot-folder, or a second spelling of a folder already declared is skipped.
An area is one top folder of the Brain, so a declaration that leaves the Brain or names a folder twice cannot be one.

Enforced at: `BrainConventions.Declared()`, through `IsAreaFolder()` and a case-insensitive set of the folders already kept

### Invariant: Declared areas keep the order written

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_table_declares_the_areas_in_its_order, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_table_s_order_column_wins_over_the_row_order]
```

The declared areas keep the order `CONVENTIONS.md` writes them in, unless a table's `Order` column numbers them, in which case the numbered rows come first by number and the rest follow in the order written.

Enforced at: `BrainConventions.Declared()`

### Invariant: Discovered areas follow the declared ones

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Discovered_areas_follow_the_declared_ones_by_folder_name, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_top_folder_is_an_area_once_a_note_sits_anywhere_below_it, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Only_a_plain_undeclared_unignored_folder_is_probed, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.With_nothing_declared_the_para_set_leads_the_discovered_areas, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_declared_area_matches_its_folder_whatever_the_case]
```

Every top folder of the Brain that is one plain segment, holds at least one `.md` note below it outside a dot-folder, is not ignored and is not declared is an area, listed after the declared areas by folder name, ordinal and case-insensitive.
A note filed into a new top folder is never hidden for want of a line in `CONVENTIONS.md` (local ADR 0026).

Enforced at: `BrainConventions`, in `Backlog.Modules.Devbook.Abstractions`

### Invariant: An ignored folder is never an area

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignored_folder_is_never_an_area_even_when_declared, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.Ignoring_every_declared_area_does_not_bring_para_back, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignore_list_names_its_folders_in_backticks_or_bare, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignore_table_reads_its_folder_column]
```

A folder `CONVENTIONS.md` lists under `## Ignore`, matched case-insensitively, is never an area, even when `## Areas` declares it.
Ignore wins over a declaration, so one line keeps a folder of templates or attachments off the tabs.

Enforced at: `BrainConventions`, in `Backlog.Modules.Devbook.Abstractions`

### Invariant: An unreadable effort is no estimate

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.KnowledgeGraphTests.AMissingOrUnreadableEffortIsNull_NotZero
```

A chapter's `effort` that is not a whole number of zero or more, a negative one included, reads as no estimate rather than as zero.
A chapter is a file nothing checks on save, so the reader is where a value it cannot use stops.

Enforced at: `DevbookGraphBuilder.ApplyMeta()`, which reads only an all-digit value; on the Roadmap side, `KnowledgeGraph.Effort()`, which reads only a value of zero or more

## Remark

```meta
status: draft
type: invariants
related: [.devbook/domain/devbook/domain.md#remark]
```

> The rules a Remark is held to as it is changed, stored and replicated. The record `DevbookAnnotation` has no behaviour of its own: the annotation store and the replica merge keep these rules, so `Enforced at:` names them.

### Invariant: Each change stamps the remark later than the copy it changed

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.Editing_and_resolving_stamp_the_remark_for_the_next_push, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.An_edit_of_a_remark_that_arrived_from_a_faster_clock_still_lands_after_it, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.A_deletion_of_a_remark_that_arrived_from_a_faster_clock_still_lands_after_it]
```

Editing, resolving or deleting a remark stamps it later than the copy it changed, even when that copy arrived from a device whose clock runs ahead.
Replication tells which version is newer by the stamp, so a change stamped earlier would lose to the version it replaced.

Enforced at: `DevbookAnnotationStore.Edit()`, `SetResolved()` and `Delete()`, through `Stamp()`

### Invariant: The newer version of a remark wins whole

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationReplicaMergeTests.A_newer_version_from_elsewhere_replaces_the_local_one, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationReplicaMergeTests.An_older_version_never_replaces_a_newer_local_one_even_one_already_pushed, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationReplicaMergeTests.The_replica_stamp_decides_first_then_the_device_stamp_then_the_device_id]
```

When two versions of one remark meet, the newer one replaces the other as a whole document, and an older version never replaces a newer one.
The replica's stamp decides first, then the device's stamp, then the device id.

Enforced at: `AnnotationReplicaMerge.Wins()` and `Apply()`

### Invariant: Deleting a written remark leaves a tombstone

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.Deleting_a_written_remark_leaves_a_tombstone_for_the_push, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationReplicaMergeTests.A_tombstone_from_elsewhere_deletes_the_local_remark]
```

Deleting a remark that has a body leaves a tombstone in its place rather than removing it.
The tombstone is what lets the deletion replicate too.

Enforced at: `DevbookAnnotationStore.Delete()`, which sets `DeletedAt`; `AnnotationReplicaMerge.Apply()` deletes the local remark when a tombstone wins

### Invariant: A draft stays on this device

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.A_fresh_remark_is_a_draft_on_the_block_it_was_asked_for, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.A_push_selects_what_changed_after_the_watermark_and_never_a_draft, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.Deleting_a_draft_removes_it_outright]
```

A live remark with an empty body is a draft: it is kept on this device, never replicated, and deleting it removes it outright with no tombstone.
A draft is a remark open in the read view that the reader has not written yet, and a draft never pushed leaves nothing to replicate.

Enforced at: `DevbookAnnotation.IsDraft`; `DevbookAnnotationStore.ListChangedSince()` leaves drafts out of a push, and `Delete()` removes a draft

### Invariant: A remark the sync service would refuse stays on this device

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationSyncSessionTests.A_remark_the_service_would_refuse_does_not_hold_back_the_rest, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationSyncSessionTests.A_remark_longer_than_the_service_stores_does_not_hold_back_the_rest, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationSyncSessionTests.A_remark_left_out_is_counted_and_named_in_a_warning_that_leaves_out_its_body]
```

A remark filed under no repository, with a blank chapter path or a negative block index, or with a field past its bound, is left out of the push, named in a warning without its body, and offered again only after an edit.
The sync service refuses a push whole, so sending it would hold back every remark beside it on every cycle.

Enforced at: `AnnotationSyncSession.PushAsync()`, through `AnnotationRecordLimits.WhyRefused()`, the rule the sync endpoint runs

### Invariant: A deletion replicates without the remark's body

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.AnnotationSyncSessionTests.A_deleted_remark_is_sent_as_a_tombstone_however_long_it_was]
```

A tombstone goes to the sync service with an empty body, however long the remark's body was.
A deletion says nothing about what the remark said, and a body past the bound would otherwise keep the deletion off every other device.

Enforced at: `AnnotationReplicaMerge.ToChange()`

### Invariant: A remark is never written into a repository

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookAnnotationStoreTests.Each_repository_gets_its_own_file_under_the_storage_folder
```

A remark is never written into a repository's devbook folders.
That is what lets a reader remark on a branch snapshot nobody can write to.

Enforced at: `DevbookAnnotationStore`, which writes only under its own storage folder (`FolderName`), and `DevbookAnnotation`, which holds a repository alias and a chapter path rather than a path on disk
