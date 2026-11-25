# Invariants

```meta
type: invariants
```

Rules of the types on [`domain.md`](domain.md). The tests are in `Backlog.Modules.Sync.UnitTests` and, for the rules held at the service's endpoints, `Backlog.Modules.Sync.Api.UnitTests`.

## Device

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#device]
```

### Invariant: A presented secret is compared in constant time

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_wrong_credential_gets_no_token, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_credential_belonging_to_another_device_gets_no_token, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorCodecTests.A_tampered_signature_is_malformed]
```

A registration credential is checked against its stored hash, and a cursor against its signature, by a comparison whose time does not depend on how much of the secret was right (`device.credential_invalid`, `sync.cursor_malformed`).
An ordinary equality check stops at the first byte that differs, so a caller timing enough attempts could walk a secret out byte by byte.
The tests prove a wrong secret is refused. The timing itself is held by the call to `CryptographicOperations.FixedTimeEquals` and is not measured.

Enforced at: `Sha256CredentialHasher.Matches()`, `HmacSyncCursorCodec`

### Invariant: A device has a name

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_device_has_to_be_called_something
```

A device is registered or paired only under a name that is not blank, trimmed to at most 100 characters (`device.name_required`).

Enforced at: `DeviceName.Validate()`

### Invariant: A device holds a key or a credential, never both

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_credential_device_binds_its_key_once_and_its_credential_stops_working, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_key_bound_device_has_no_credential_to_offer, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_credential_device_cannot_sign_in_by_assertion, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_migration_that_loses_the_race_to_bind_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_migration_with_a_key_that_is_not_one_keeps_the_credential]
```

Binding a key drops the device's credential hash in the same write, and happens once: a second binding, and the credential, are refused from then on (`device.credential_invalid`).
A device on a credential is taken on the credential alone, and an assertion beside it is refused rather than ignored.

Enforced at: `IssueDeviceTokenCommandHandler`, `IDeviceRegistry.BindKey()`

### Invariant: A device key is a P-256 public key

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_key_on_another_curve_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_public_key_that_is_not_one_is_refused_before_the_enrolment_code_is_spent, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.Pairing_with_a_public_key_that_is_not_one_leaves_the_code_unspent]
```

A key offered at registration, pairing or a migration is a base64url SubjectPublicKeyInfo of a P-256 key and nothing else, and is stored re-encoded, so two spellings of one key cannot both be on file (`device.public_key_invalid`).
It is checked before an enrolment or pairing code is spent, so a bad key leaves the code working.

Enforced at: `DevicePublicKey.Validate()`

### Invariant: An assertion is ES256, from its device, for this service

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_signed_by_another_key_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_for_another_audience_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_issued_by_another_device_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_about_another_subject_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_naming_another_algorithm_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_unsigned_assertion_naming_alg_none_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_with_a_critical_header_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_living_longer_than_five_minutes_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_expired_assertion_is_refused_past_the_clock_skew, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.An_assertion_issued_in_the_future_is_refused]
```

An assertion is taken only when its key signed it with ES256, its header carries no `crit`, it names the device as both `iss` and `sub` and `backlog-sync/devices/token` as `aud`, and it lives at most five minutes, within the clock skew either side.
The signature is checked before any claim is read, so nothing an unsigned body says decides anything. Every failure reads the same (`device.credential_invalid`).

Enforced at: `EcdsaDeviceAssertionVerifier.Verify()`, on the shape `SyncAssertion` fixes

### Invariant: An assertion is taken once

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.The_same_assertion_works_once, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_replay_inside_the_clock_skew_after_expiry_is_still_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_forged_assertion_does_not_spend_the_id_it_reuses, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_migration_spends_its_assertion, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.A_replay_store_that_cannot_answer_mints_no_token]
```

A verified assertion's id is spent before a token is minted, and kept until the assertion could no longer be taken, so the same assertion is refused however soon it comes back.
Only an assertion that passed every other check spends its id, so a forgery cannot burn an id a real one will use. A replay store that cannot answer mints no token.

Enforced at: `IAssertionReplayCache.TryAdd()`, the `assertionReplays` container in a deployment

## Pairing Code

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#pairing-code]
```

### Invariant: A pairing code is burned before its device is added

```meta
type: invariant
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_pairs_one_device_and_no_more
```

A redemption adds a device only after it has burned the code, so a code pairs one device and every later redemption is refused (`pairing.code_used`).
Two devices racing on one code both pass the checks before the burn, and only the one that wins it may have a device.

Enforced at: `RedeemPairingCodeCommandHandler`, `IPairingCodeStore.TryBurn()`

## Connector Client

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#connector-client]
```

### Invariant: A client generation outlives nothing issued under an earlier one

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_code_issued_before_a_new_secret_is_an_invalid_grant, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.Revoking_the_secret_cuts_off_the_secret_and_every_refresh_token, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorRefreshRaceTests.A_refresh_token_of_an_owner_no_longer_configured_is_an_invalid_grant]
```

A new secret and a revoke each move the client's generation on, and a code, refresh token or access token whose generation is not the current one is refused (`invalid_grant`, or an invalid token on `/mcp`).
Every grant carries its generation, so the revoke needs no query over what it kills.

Enforced at: `ExchangeAuthorizationCodeCommandHandler`, `RefreshConnectorTokenCommandHandler`, `RemoteMcpAuthentication`

## Connector Authorization

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#connector-authorization]
```

### Invariant: A code works once, and its reuse revokes what it issued

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_code_works_once_and_its_reuse_revokes_what_it_issued, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_wrong_verifier_is_an_invalid_grant, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_redirect_other_than_the_one_authorized_is_an_invalid_grant, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorRefreshRaceTests.A_code_reuse_that_revokes_before_the_first_exchange_stores_its_token_still_kills_it]
```

A code is traded only with the verifier behind its S256 challenge and the redirect it was issued for, and only once (`invalid_grant`).
A second use revokes the refresh-token family its first use started, so a stolen code that loses the race still ends the session it raced.

Enforced at: `ExchangeAuthorizationCodeCommandHandler`

## Connector Refresh Token

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#connector-refresh-token]
```

### Invariant: A spent refresh token presented again revokes its family

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.Replaying_a_rotated_refresh_token_revokes_the_family, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorRefreshRaceTests.A_replay_that_revokes_before_the_rotation_leaves_nothing_issued, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorRefreshRaceTests.A_replay_that_revokes_right_after_the_rotation_kills_the_replacement, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorRefreshRaceTests.A_caller_that_loses_the_rotation_mints_nothing_and_stamps_nothing]
```

A refresh token that was rotated or revoked is a replay when presented, and revokes every token of its family, the replacement included (`invalid_grant`).
Of two calls racing on one token, only the one that wins the rotation mints anything.

Enforced at: `RefreshConnectorTokenCommandHandler`, `ConnectorRefreshToken.IsSpent`

## Change Precedence

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#change-precedence]
```

### Invariant: The later stamp wins, and a tombstone wins a tie

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.Precedence_is_the_stamp_then_the_tombstone, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.An_older_live_copy_pushed_after_a_tombstone_does_not_replace_it, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryTaskReplicaPrecedenceTests.A_tombstone_of_the_very_version_held_is_taken_on_a_tied_stamp, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryAnnotationReplicaPrecedenceTests.Precedence_is_the_stamp_then_the_tombstone, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryAnnotationReplicaPrecedenceTests.An_older_live_copy_pushed_after_a_tombstone_does_not_replace_it]
```

A pushed copy replaces the stored one only when its device stamp is later, or the stamps tie and it is a live copy's tombstone. A push therefore never moves a document backwards.
An identical pair is the same version twice and writes nothing. The rule is on the stamp and not on arrival order, because an echo carries the stamp of the version it echoes.

Enforced at: `TaskChangePrecedence.Supersedes()`, `AnnotationChangePrecedence.Supersedes()`

### Invariant: A devbook subject's later stamp wins alone

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryDevbookSubjectReplicaTests.Precedence_is_the_stamp_alone, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryDevbookSubjectReplicaTests.An_older_state_pushed_after_a_later_one_does_not_replace_it, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryDevbookSubjectReplicaTests.The_same_version_twice_is_refused_and_does_not_move_in_the_feed, unit:dotnet:Backlog.Modules.Sync.UnitTests.InMemoryDevbookSubjectReplicaTests.A_later_state_replaces_the_stored_one_whole_and_is_counted]
```

A pushed copy of a [devbook subject's state](../devbook/domain.md#subject-state) replaces the stored one only when its device stamp is later, and a tie writes nothing.
A subject's state is set and never deleted, so no tombstone takes part. A tie is the same version twice or two devices nothing can order.

Enforced at: `DevbookSubjectChangePrecedence.Supersedes()`

### Invariant: A push over the cap is refused whole

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.TaskSyncEndpointTests.A_push_of_more_tasks_than_the_cap_is_refused, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SessionSyncEndpointTests.A_push_of_more_records_than_the_cap_is_refused, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.AnnotationSyncEndpointTests.A_push_of_more_annotations_than_the_cap_is_refused_whole, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.DevbookSubjectSyncEndpointTests.A_push_of_more_changes_than_the_cap_is_refused_whole]
```

A push carrying more documents than its feed's cap stores none of them (`sync.push_batch_too_large`, `sync.session_batch_too_large`, `sync.annotation_batch_too_large`, `sync.devbook_subject_batch_too_large`).
Storing the first part and answering success would leave the device believing the rest was stored too, and its watermark would move past it.

Enforced at: `TaskSyncEndpoints`, `SessionSyncEndpoints`, `AnnotationSyncEndpoints`, `DevbookSubjectSyncEndpoints` (the caps are in `SyncRequestLimits`)

### Invariant: A devbook subject state the service would not store refuses its whole push

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.DevbookSubjectSyncEndpointTests.A_state_outside_its_kinds_row_is_refused_with_the_whole_batch, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevbookSubjectRecordLimitsTests.A_change_the_service_would_not_store_is_refused_with_a_reason, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevbookSubjectRecordLimitsTests.A_refusal_names_the_field_and_never_echoes_the_value]
```

A push stores nothing when one subject state has no repository alias, a kind or state `devbook.decision@1` lacks, a decision on anything but a proposal, or a field past its bound (`sync.devbook_subject_invalid`).
The refusal names the field and never echoes its value. The desktop runs the same rule before it pushes, so such a state stays on its device rather than holding back every push.

Enforced at: `DevbookSubjectSyncEndpoints`, through `DevbookSubjectRecordLimits.WhyRefused()`

## Shared Value Objects

```meta
type: invariants
related: [.devbook/domain/sync/domain.md#shared-value-objects]
```

### Invariant: A cursor carries its owner

```meta
type: invariant
tests: [unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorCodecTests.A_cursor_minted_for_another_owner_is_refused_by_its_own_code, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorCodecTests.A_payload_rewritten_to_another_owner_is_malformed, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncCursorOwnerEndpointTests.A_cursor_minted_for_another_owner_is_refused]
```

A replica cursor names the owner it was minted for, and a cursor naming another owner than the caller's is refused (`sync.cursor_not_yours`).

Enforced at: `SyncCursor`, `TaskReplicaCursor`, `SessionReplicaCursor`, `AnnotationReplicaCursor`, `DevbookSubjectReplicaCursor`, `HmacSyncCursorCodec`
