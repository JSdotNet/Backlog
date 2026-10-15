# Invariants

```meta
status: draft
type: invariants
```

What each aggregate and domain service on [`domain.md`](domain.md) enforces, one chapter each: each rule, where it is enforced, and the unit test that proves it. The tests are in `Backlog.Modules.Sync.UnitTests` and, for the rules the service holds at its endpoints, `Backlog.Modules.Sync.Api.UnitTests`.

## Device

```meta
status: draft
type: invariants
related: [.devbook/domain/sync/domain.md#device]
```

> The rules a registered device and the secrets that stand for it are held to.

### Invariant: A presented secret is compared in constant time

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_wrong_credential_gets_no_token, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_credential_belonging_to_another_device_gets_no_token, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorCodecTests.A_tampered_signature_is_malformed]
```

A registration credential is checked against its stored hash, and a cursor against its signature, by a comparison whose time does not depend on how much of the secret was right (`device.credential_invalid`, `sync.cursor_malformed`).
An ordinary equality check stops at the first byte that differs, so a caller who can time enough attempts could walk a secret out one byte at a time.
The tests prove a wrong secret is refused. The timing itself is held by the call to `CryptographicOperations.FixedTimeEquals` and is not measured.

Enforced at: `Sha256CredentialHasher.Matches()`, `HmacSyncCursorCodec`

### Invariant: A device has a name

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_device_has_to_be_called_something
```

A device is registered or paired only under a name that is not blank, trimmed to at most 100 characters (`device.name_required`).

Enforced at: `DeviceName.Validate()`

## Pairing Code

```meta
status: draft
type: invariants
related: [.devbook/domain/sync/domain.md#pairing-code]
```

> The rules a pairing code is held to on its way from issued to redeemed.

### Invariant: A pairing code is burned before its device is added

```meta
status: draft
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_pairs_one_device_and_no_more
```

A redemption adds a device only after it has burned the code, so a code pairs one device and every later redemption is refused (`pairing.code_used`).
Two devices racing on one code both pass the checks before the burn, and only the one that wins it may have a device.

Enforced at: `RedeemPairingCodeCommandHandler`, `IPairingCodeStore.TryBurn()`

## Change Precedence

```meta
status: draft
type: invariants
related: [.devbook/domain/sync/domain.md#change-precedence]
```

> The rules on what a push may write to a replica.

### Invariant: The later stamp wins, and a tombstone wins a tie

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.Precedence_is_the_stamp_then_the_tombstone, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.An_older_live_copy_pushed_after_a_tombstone_does_not_replace_it, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.A_tombstone_of_the_very_version_held_is_taken_on_a_tied_stamp, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryAnnotationReplicaPrecedenceTests.Precedence_is_the_stamp_then_the_tombstone, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryAnnotationReplicaPrecedenceTests.An_older_live_copy_pushed_after_a_tombstone_does_not_replace_it]
```

A pushed copy replaces the stored one only when its device stamp is later, or when the stamps tie and the pushed copy is a tombstone of a live one, so a push never moves a document backwards.
An identical pair is the same version twice and writes nothing. The rule is on the stamp and not on arrival order, because an echo carries the stamp of the version it echoes.

Enforced at: `TaskChangePrecedence.Supersedes()`, `AnnotationChangePrecedence.Supersedes()`

### Invariant: A push over the cap is refused whole

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.TaskSyncEndpointTests.A_push_of_more_tasks_than_the_cap_is_refused, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SessionSyncEndpointTests.A_push_of_more_records_than_the_cap_is_refused, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.AnnotationSyncEndpointTests.A_push_of_more_annotations_than_the_cap_is_refused_whole]
```

A push carrying more documents than its feed's cap stores none of them (`sync.push_batch_too_large`, `sync.session_batch_too_large`, `sync.annotation_batch_too_large`).
Storing the first part and answering success would leave the device believing the rest was stored too, and its watermark would move past it.

Enforced at: `TaskSyncEndpoints`, `SessionSyncEndpoints`, `AnnotationSyncEndpoints` (the caps are in `SyncRequestLimits`)

## Shared Value Objects

```meta
status: draft
type: invariants
related: [.devbook/domain/sync/domain.md#shared-value-objects]
```

> The rules the shared values enforce on their own.

### Invariant: A cursor carries its owner

```meta
status: draft
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorCodecTests.A_cursor_minted_for_another_owner_is_refused_by_its_own_code, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorCodecTests.A_payload_rewritten_to_another_owner_is_malformed]
```

A replica cursor names the owner it was minted for, and a cursor naming another owner than the caller's is refused (`sync.cursor_not_yours`).

Enforced at: `SyncCursor`, `TaskReplicaCursor`, `SessionReplicaCursor`, `AnnotationReplicaCursor`, `HmacSyncCursorCodec`
