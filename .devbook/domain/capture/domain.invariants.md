# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Capture

```meta
status: draft
type: invariants
related: [.devbook/domain/capture/domain.md#capture]
```

### Invariant: Attachments named are already stored

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.AttachmentStoreHandlerTests.A_capture_naming_an_attachment_not_uploaded_is_refused_with_its_id
```

A capture names only attachments already stored for its owner, each with the size and SHA-256 the capture claims; otherwise the whole capture is refused, naming the ids.

Enforced at: `CaptureInboxItemCommandHandler`

### Invariant: Metadata travels, bytes do not

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.AttachmentSyncEndpointTests.A_capture_naming_an_uploaded_attachment_carries_its_metadata_to_the_pull_and_the_list
```

A capture carries its attachments' metadata and never their bytes.

Enforced at: `CaptureInboxItemCommandHandler` (writes `TaskPayload.Attachments`)

### Invariant: Stored bytes match their digest

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.AttachmentStoreHandlerTests.Bytes_that_miss_their_declared_digest_are_refused_and_not_committed
```

An attachment's bytes are stored only when they hash to the digest the upload declared and fit the per-file cap; a refused upload leaves nothing stored.

Enforced at: `StoreAttachmentCommandHandler`

### Invariant: Acknowledging releases the held attachments

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.AttachmentStoreHandlerTests.An_acknowledgement_succeeds_and_logs_when_the_release_fails
```

Acknowledging a capture releases the attachments the held capture named — never ones the tombstone names — and a failed release never fails the acknowledgement.

Enforced at: `CaptureAttachmentRelease`, from `AcknowledgeInboxItemCommandHandler` and `PushTasksCommandHandler`
