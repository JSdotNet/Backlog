# Invariants

```meta
type: invariants
status: draft
related: [.devbook/domain/inbox/domain.md]
```

> What each aggregate on `domain.md` enforces, one chapter per aggregate. Each
> invariant is one claim, where it is enforced, and the unit test that proves it.

## Inbox Item

```meta
type: invariants
status: draft
related: [.devbook/domain/inbox/domain.md#inbox-item, .devbook/domain/inbox/domain.md#attachment]
tests: [unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxAttachmentTests]
```

> The rules the Inbox Item holds its [attachments](domain.md#attachment) to.
> The aggregate chapter says what the boundary is and why it exists. This
> chapter says what it will not let a caller break. The item's older rules are
> still in the table on `domain.md` and have not moved here yet.

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
