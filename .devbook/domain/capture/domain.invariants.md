# Invariants

```meta
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Capture

```meta
type: invariants
related: [.devbook/domain/capture/domain.md#capture]
```

### Invariant: Attachments named are already stored

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.AttachmentStoreHandlerTests.A_capture_naming_an_attachment_not_uploaded_is_refused_with_its_id
```

A capture names only attachments already stored for its owner, each with the size and SHA-256 the capture claims; otherwise the whole capture is refused, naming the ids.

Enforced at: `CaptureInboxItemCommandHandler`

### Invariant: Metadata travels, bytes do not

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.AttachmentSyncEndpointTests.A_capture_naming_an_uploaded_attachment_carries_its_metadata_to_the_pull_and_the_list
```

A capture carries its attachments' metadata and never their bytes.

Enforced at: `CaptureInboxItemCommandHandler` (writes `TaskPayload.Attachments`)

### Invariant: Stored bytes match their digest

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.AttachmentStoreHandlerTests.Bytes_that_miss_their_declared_digest_are_refused_and_not_committed
```

An attachment's bytes are stored only when they hash to the digest the upload declared and fit the per-file cap; a refused upload leaves nothing stored.

Enforced at: `StoreAttachmentCommandHandler`

### Invariant: Acknowledging releases the held attachments

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.AttachmentStoreHandlerTests.An_acknowledgement_succeeds_and_logs_when_the_release_fails
```

Acknowledging a capture releases the attachments the held capture named, never ones its [tombstone](../tasks/domain.md#tombstone) names. A failed release never fails the acknowledgement.

Enforced at: `CaptureAttachmentRelease`, from `AcknowledgeInboxItemCommandHandler` and `PushTasksCommandHandler`

### Invariant: An edit changes only a waiting capture's title and body

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.EditCaptureHandlerTests.An_edit_replaces_the_title_and_body_and_keeps_everything_else_the_capture_said, unit:dotnet:Backlog.Modules.Sync.UnitTests.EditCaptureHandlerTests.An_edit_is_stamped_after_the_held_version_when_the_service_clock_is_not_later, unit:dotnet:Backlog.Modules.Sync.UnitTests.EditCaptureHandlerTests.An_edit_of_anything_but_a_waiting_capture_is_not_found_and_writes_nothing]
```

An edit replaces the title and body of a live capture of its owner, stamped on the service's clock or one tick after the held stamp, and keeps its id, capture instant, source, tags, person and attachments. Anything else is not found (`inbox.item_not_found`).

Enforced at: `EditCaptureCommandHandler`

### Invariant: A capture's tombstone is final

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.A_capture_tombstone_wins_over_a_live_capture_whatever_the_stamps, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.Any_other_tombstone_still_yields_to_a_later_live_copy, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.The_desktop_tombstone_stamped_before_a_phone_edit_still_tombstones_the_capture]
```

Between a capture tombstone and a live capture, the tombstone wins in both directions whatever their stamps. A decision racing a phone edit therefore still ends tombstoned. Tasks and notes keep last-write-wins, because both are restored on purpose.

Enforced at: `TaskChangePrecedence.Supersedes()`
