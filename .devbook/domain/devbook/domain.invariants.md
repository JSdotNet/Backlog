# Invariants

```meta
status: draft
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

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

## Subject State

```meta
status: draft
type: invariants
related: [.devbook/domain/devbook/domain.md#subject-state]
```

> The rules a Subject State is held to as it is set, stored and replicated. The record `DevbookSubjectState` has no behaviour of its own: `DevbookSubjectRules` decides every operation, and the stores and the replica merge keep the rest, so `Enforced at:` names them.

### Invariant: A refused operation writes nothing

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookSubjectStateStoreTests.A_refused_operation_leaves_the_file_as_it_was_and_says_nothing, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookSubjectStateStoreTests.A_refusal_on_a_subject_with_no_state_creates_no_file, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DecisionToolsTests.Accepting_with_no_approval_is_refused_not_approved_and_writes_nothing]
```

An operation the rules refuse leaves the subject's state as it was, and the store neither writes nor announces anything.
A refusal carries no state, and a store writes only a state that comes back.

Enforced at: `DevbookSubjectRules`, whose `DevbookSubjectOutcome.Refuse()` carries no state; `DevbookSubjectStateStore.Operate()` writes only what comes back

### Invariant: A state is one of its kind's row

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.A_state_outside_the_subjects_row_is_refused, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.A_proposal_is_never_set_to_approved_or_accepted, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.A_decision_record_moves_through_its_own_row]
```

A subject is set only to a state in its kind's row, and a proposal reaches `approved` and `accepted` only by being approved and accepted (`bad-state`).

Enforced at: `DevbookSubjectRules.SetState()`

### Invariant: Approve and accept take a proposal alone

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Approve_and_accept_take_a_proposal_alone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.A_fingerprint_that_is_no_fingerprint_is_refused]
```

Only a proposal is approved or accepted, and only at a value that is a fingerprint, `sha256:` and eight lowercase hex characters (`bad-state`).

Enforced at: `DevbookSubjectRules.Approve()` and `Accept()`, through `IsFingerprint()`

### Invariant: An acceptance stands on an approval at the same fingerprint

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Accepting_stands_on_an_approval_at_the_same_fingerprint, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Accepting_without_an_approval_is_refused_not_approved, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Accepting_at_another_fingerprint_is_refused_as_a_mismatch]
```

A proposal is accepted only at the fingerprint it was approved at (`not-approved`, `fingerprint-mismatch`).
A change that moved since its approval has to be approved again, at what it now says, before it can be accepted.

Enforced at: `DevbookSubjectRules.Accept()`

### Invariant: Leaving a decision step withdraws the decisions above it

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Setting_a_proposal_back_to_draft_withdraws_approval_and_acceptance, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectRulesTests.Approving_again_withdraws_an_acceptance_at_the_old_fingerprint]
```

Setting a proposal back to `draft` withdraws its approval and its acceptance, and approving it again withdraws its acceptance.
An acceptance stands on the approval it was given after, so it cannot outlive that approval.

Enforced at: `DevbookSubjectRules.SetState()` and `Approve()`

### Invariant: Each change stamps the state later than the copy it changed

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookSubjectStateStoreTests.A_state_set_on_a_copy_from_a_faster_clock_still_lands_after_it
```

Setting a state, approving or accepting stamps the subject's state later than the copy it changed, even when that copy arrived from a device whose clock runs ahead.
Replication tells which version is newer by the stamp, so a change stamped earlier would lose to the version it replaced.

Enforced at: `DevbookSubjectStateStore.Operate()`, through `Stamp()`

### Invariant: The later version of a subject's state wins whole

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookSubjectStateStoreTests.A_replicated_state_no_later_than_the_one_held_is_not_written, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DevbookSubjectReplicaMergeTests.A_later_state_from_elsewhere_replaces_the_local_one_whole, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DevbookSubjectReplicaMergeTests.An_older_state_never_replaces_a_later_local_one, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DevbookSubjectReplicaMergeTests.The_replica_stamp_decides_first_then_the_device_stamp_then_the_device_id]
```

When two versions of one subject's state meet, the one stamped later replaces the other as a whole document, and a tie keeps the one held.
No version is ever deleted, so no tombstone takes part. The sync service holds the same rule for a push ([Change Precedence](../sync/domain.invariants.md#change-precedence)).

Enforced at: `DevbookSubjectRules.Supersedes()`, which every store's `Apply()` asks under its own lock; `DevbookSubjectReplicaMerge.Wins()` reduces a pulled page to one version per subject by the replica's stamp, then the device's stamp, then the device id

### Invariant: A subject has one id on every device

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectTests.The_id_is_the_same_on_every_device_for_the_same_subject, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectTests.Another_repository_or_pull_request_is_another_subject, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSubjectTests.An_alias_names_one_repository_whatever_its_case, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DevbookSubjectReplicaMergeTests.A_record_filed_under_another_id_than_its_subjects_is_not_taken]
```

A subject's state has the same id on every device, derived from the repository's alias, whatever its case, and the subject's name, and a pulled state filed under any other id is not taken.
Two devices that set a state on one subject before either has synced would otherwise keep two states with nothing to say which is current.

Enforced at: `DevbookSubject.IdFor()`, through `AliasKey()`; `DevbookSubjectReplicaMerge.ToState()` refuses a record whose id is not its subject's

### Invariant: A state the sync service would refuse stays on this device

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DevbookSubjectSyncSessionTests.A_state_the_service_would_refuse_is_left_out_and_named_without_holding_back_the_rest, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevbookSubjectRecordLimitsTests.A_change_the_service_would_not_store_is_refused_with_a_reason]
```

A state filed under no repository, with a kind or state the contract does not have, with a decision on anything but a proposal, or with a field past its bound, is left out of the push and named in a warning.
It is offered again only once its state is set again. The sync service refuses a push whole, so sending it would hold back every state beside it on every cycle.

Enforced at: `DevbookSubjectSyncSession.PushAsync()`, through `DevbookSubjectRecordLimits.WhyRefused()`, the rule the sync endpoint runs

### Invariant: A subject's state is never written into a repository

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookSubjectStateStoreTests.Each_repository_keeps_its_own_file, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookReadOnlyBranchTests.A_debt_records_state_is_set_on_a_branch_and_no_file_changes]
```

A subject's state is never written into a repository's devbook folders or into the devbook database.
That is what lets a reader set a debt record's state on a branch snapshot nobody can write to, and what keeps a state when the database is rebuilt.

Enforced at: `DevbookSubjectStateStore`, which writes only under its own storage folder (`FolderName`), and `DevbookSubject`, which holds a repository path rather than a path on disk

## Shared Value Objects

```meta
status: draft
type: invariants
related: [.devbook/domain/devbook/domain.md#shared-value-objects]
```

> The rules of the planning fields any devbook chapter carries, a Brain note
> included.

### Invariant: A roadmap contribution draws no graph edge

```meta
status: draft
type: invariant
```

A [Roadmap Contribution](domain.md#roadmap-contribution) is kept on the chapter's node as a name and never becomes an edge in the knowledge graph.
It names a roadmap item rather than addressing a chapter, so there is no node for it to point at.

Enforced at: `DevbookGraphBuilder.ApplyMeta()`, which keeps `roadmap` as a node attribute and leaves it out of `ReferenceFields`

### Invariant: An unreadable effort is no estimate

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.KnowledgeGraphTests.AMissingOrUnreadableEffortIsNull_NotZero
```

A chapter's `effort` that is not a whole number of zero or more, a negative one included, reads as no estimate rather than as zero.
A chapter is a file nothing checks on save, so the reader is where a value it cannot use stops.

Enforced at: `DevbookGraphBuilder.ApplyMeta()`, which reads only an all-digit value; on the Roadmap side, `KnowledgeGraph.Effort()`, which reads only a value of zero or more
