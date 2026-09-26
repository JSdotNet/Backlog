# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Inbox Item

```meta
type: invariants
status: draft
related: [.devbook/domain/inbox/domain.md#inbox-item, .devbook/domain/inbox/domain.md#attachment]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests]
```

> The rules the Inbox Item holds its state and its [attachments](domain.md#attachment) to.

### Invariant: Source link and capture instant never change

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.The_capture_instant_and_the_source_url_have_no_setter
```

The original source link and `captured_at` are preserved unchanged for the life of the item.

Enforced at: constructor (neither has a setter)

### Invariant: Status follows the lifecycle

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.An_item_that_is_still_open_can_be_archived
```

Status only advances through the defined lifecycle (`unprocessed` → `triaged` → routed / deferred / archived).

Enforced at: `Defer()`, `Resurface()`, `Archive()`, `RouteToBacklog()`

### Invariant: One routing target

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Routing_happens_exactly_once
```

A routing decision records exactly one `Routing Target`; a second routing is refused.

Enforced at: `RouteToBacklog()`

### Invariant: A routed item is never archived

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_routed_item_cannot_be_archived
```

A routed item is never archived: routing is the terminal outcome of triage.

Enforced at: `Archive()`

### Invariant: An archived item is never routed

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.An_archived_item_cannot_be_routed
```

An archived item is never routed.

Enforced at: `RouteToBacklog()`

### Invariant: A deferred item resurfaces

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeferAndResurfaceTests.The_sweep_returns_every_deferred_item_whose_date_is_reached_and_touches_nothing_else
```

A deferred item resurfaces as `unprocessed` when its `deferred_until` date is reached; an undated deferral is never due, and a date still ahead leaves it deferred.

Enforced at: `ResurfaceIfDue()`, applied by the `ResurfaceDueItems` sweep the pane runs each time it opens

### Invariant: Deferring again changes the review date

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.DeferAndResurfaceTests.A_deferred_item_can_be_deferred_again_to_a_new_date
```

Deferring an item that is already deferred changes its review date; a routed or archived item cannot be deferred.

Enforced at: `Defer()`

### Invariant: Every item has a content kind

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.An_unknown_kind_slug_survives_as_its_own_word
```

Every item has a `Content Kind`; `text` is the kind of an item nobody has looked at, and a kind this build does not know keeps its own word.

Enforced at: constructor, `SetKind()`

### Invariant: A source person is an @name tag

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.ReceiveCaptureTests.A_capture_with_tags_and_a_person_files_both
```

A `Source` person, when present, is a stored `@name` tag — the sigil is the whole of the difference from a general tag.

Enforced at: constructor; `ReceiveCapture` adds the `@` however the capture sent it

### Invariant: A person is never a tag

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_person_is_refused_as_a_tag
```

A person is never a tag: a `@name` among the tags is refused.

Enforced at: `SetTags()`

### Invariant: Tags are bare and distinct

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Tags_are_stored_bare_and_deduplicated_by_name
```

Tags are stored bare (no `#`) and de-duplicated by canonical name.

Enforced at: `SetTags()`

### Invariant: Repositories are distinct

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Repositories_are_distinct_without_regard_to_case
```

Assigned repositories are distinct without regard to case.

Enforced at: `SetRepoIds()`

### Invariant: Filing is not triage

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.Filing_is_allowed_in_every_state_including_archived
```

Filing is not triage: `list_id` may change in any state, archived included.

Enforced at: `MoveToList()`

### Invariant: A synced item keeps the capture's id

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxItemTests.A_replica_capture_keeps_the_captures_id
```

An item that arrived through sync carries the capture's own id.

Enforced at: constructor (`FromCapture`)

### Invariant: Capture tags go through SetTags

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.ReceiveCaptureTests.A_capture_with_tags_and_a_person_files_both
```

A capture's tags reach the item only through `SetTags()`, so they are stored bare and de-duplicated like any other.

Enforced at: `ReceiveCapture`

### Invariant: A person among capture tags is refused

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.ReceiveCaptureTests.A_person_among_the_tags_is_refused_as_a_tag_and_the_capture_still_lands
```

A `@name` among a capture's tags is refused as a tag and does not become the person; intake still takes the rest of the capture rather than failing.

Enforced at: `ReceiveCapture`

### Invariant: The capture's person travels as its one @name tag

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskReplicaMergeCaptureTests.A_captures_person_tag_arrives_as_its_person_and_its_body_as_its_body
```

On the replica, the capture's person travels as the one `@name` tag on its document; the sync service refuses any other tag that reads as a person.

Enforced at: `InboxEndpoints.OutOfBounds`, `TaskReplicaMerge.ToCapture`

### Invariant: A resent capture is the same capture

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.InboxCaptureEndpointTests.A_retry_with_the_same_id_answers_the_stored_capture_and_writes_nothing
```

A capture sent again with the same client id is the same capture: the service answers the stored one and writes no second document, and never brings an acknowledged one back.

Enforced at: `CaptureInboxItemCommandHandler`

### Invariant: An item holds each file once

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.A_file_is_recorded_once_by_id
```

An item's attachments are distinct by id. Recording files only adds the ones
the item does not have yet. A replayed page, or the same capture offered twice,
therefore records nothing twice.

Enforced at: `RecordAttachments()`

### Invariant: Recording a file again changes nothing about it

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.Recording_again_does_not_reset_a_downloaded_file
```

Recording a file that is already on the item leaves it exactly as it was: its
metadata, and whether it is downloaded. A replay cannot reset a downloaded file
to waiting.

Enforced at: `RecordAttachments()`

### Invariant: What the capture said about a file never changes

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.A_file_is_recorded_once_by_id
```

An attachment's name, content type, size and sha256 stay as they were first
recorded. Every change to the local copy keeps them as they are. They record
what was sent, like the capture instant.

Enforced at: `InboxAttachment` (no setters; `Downloaded()` and `Failed()` copy the capture's members unchanged), `RecordAttachments()`

### Invariant: A file is on this machine or it is not, never both

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.A_file_is_downloaded_or_failed_never_both
```

A downloaded file has a local path and a download time and no error. A file
that is not downloaded has neither of them and may have the reason its last
fetch failed. A local path and an error are never kept together.

Enforced at: `MarkAttachmentDownloaded()`, `MarkAttachmentFailed()`

### Invariant: A file no fetch could honour is refused

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.A_file_no_fetch_could_honour_is_refused
```

A file named with an empty id, a negative size, or a sha256 that is not 64 hex
characters is refused. Without these the desktop could not ask for the file or
check the bytes it got back.

Enforced at: `InboxAttachment.Named()`

### Invariant: A file with no name or type is still a file

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.A_file_with_no_name_or_type_is_still_a_file
```

A file with a blank name takes its id as its name, and a file with a blank
content type takes `application/octet-stream`.

Enforced at: `InboxAttachment.Named()`

### Invariant: Each file has one safe name in its item's folder

```meta
type: invariant
status: draft
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests.File_names_are_safe_and_the_second_file_under_a_name_gets_its_id
```

Each file has one stable name in the item's
[attachment folder](domain.md#attachment-folder). Characters a file system
refuses are replaced. A later file that would share an earlier file's name gets
`-` and the first eight characters of its id before the extension. The first
file keeps the name, so a retry writes to the same place the first attempt did.

Enforced at: `AttachmentFileName()`

## Inbox List

```meta
status: draft
type: invariants
related: [.devbook/domain/inbox/domain.md#inbox-list]
```

### Invariant: A list is named

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Renaming_trims_and_refuses_a_blank
```

A list has a non-empty name, stored trimmed.

Enforced at: constructor, `Rename()`

### Invariant: Deleting a list unfiles its items

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Deleting_a_list_returns_its_items_to_the_inbox
```

Deleting a list returns every item it held to the unfiled inbox.

Enforced at: `DeleteList` command

### Invariant: Items are filed only in lists that exist

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Filing_an_item_in_a_list_that_does_not_exist_is_refused
```

An item is only filed in a list that exists.

Enforced at: `MoveToList` command

## Inbox Group

```meta
status: draft
type: invariants
related: [.devbook/domain/inbox/domain.md#inbox-group]
```

### Invariant: A group is named

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.A_group_is_named_uniquely_and_renamed_under_the_same_rule
```

A group has a non-empty name, stored trimmed.

Enforced at: constructor, `Rename()`

### Invariant: Removing a group never removes a list

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Inbox.UnitTests.OrganizerTests.Ungrouping_moves_the_lists_to_the_top_level_and_removes_the_group
```

Removing a group never removes a list: its lists are moved to the top level first.

Enforced at: `UngroupLists` command
