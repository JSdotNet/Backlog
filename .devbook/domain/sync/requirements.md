# Requirements

```meta
type: requirements
```

> The service is the `Backlog.Modules.Sync` module, hosted by
> `Backlog.Modules.Sync.Api`; the client is `Backlog.Infrastructure.Sync`.

## Pairing a device

```meta
type: requirements
related: [.devbook/domain/sync/features.md#pairing-a-device]
```

### Requirement: An unpinned deployed service enrols no device beside another owner's

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#ownerid, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#1-the-owner-is-one-github-account-matched-by-its-numeric-user-id]
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.OwnerPinTests.A_pinned_owner_id_is_the_owner_ahead_of_the_derived_one, unit:dotnet:Backlog.Modules.Sync.UnitTests.OwnerPinTests.An_unpinned_deployed_owner_with_devices_under_another_owner_refuses_enrolment_and_changes_nothing, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.OwnerEnrolmentEndpointTests.A_pinned_owner_id_is_the_owner_a_signed_in_enrolment_joins, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.OwnerEnrolmentEndpointTests.Recovery_under_a_pinned_owner_id_keeps_that_owner_and_revokes_the_rest, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.OwnerEnrolmentEndpointTests.An_unpinned_owner_with_devices_under_another_owner_refuses_enrolment, unit:dotnet:Backlog.Modules.Sync.Api.UnitTests.OwnerEnrolmentRegistrationTests.A_deployed_service_without_a_pinned_owner_warns_at_startup]
```

A deployed sync service with no pinned [OwnerId](domain.md#ownerid) SHALL refuse a signed-in enrolment or recovery while devices of another owner are on record (`enrolment.owner_unpinned`). It refuses before the enrolment code is spent, adds and revokes no device, and warns at startup that no owner is pinned.

**Why:** without a pin, the derived owner is a new, empty one, and joining it would orphan the owner whose devices the service already holds.

#### Scenario: A service deployed without its owner pin

- **Given** a deployed sync service with no `Owner:OwnerId` that holds devices of another owner
- **When** the owner signs in with GitHub to enrol a device
- **Then** the enrolment is refused, its code still works, and the device list is unchanged

#### Scenario: A pinned owner recovers

- **Given** a deployed sync service whose `Owner:OwnerId` names the owner
- **When** the owner signs in with GitHub to recover
- **Then** the recovered device joins that owner, and the owner's other devices are revoked

### Requirement: A pairing code works for ten minutes

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_stops_working_after_ten_minutes, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_still_works_a_minute_before_it_expires]
```

The sync service SHALL refuse a [Pairing Code](domain.md#pairing-code) redeemed ten minutes or more after it was issued.

#### Scenario: A code entered too late

- **Given** a pairing code the first device showed eleven minutes ago
- **When** the person enters it on a second device
- **Then** the second device is not paired

### Requirement: A pairing code pairs one device

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_pairs_one_device_and_no_more, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DevicePairingEndpointTests.The_same_code_cannot_pair_two_devices]
```

The sync service SHALL accept a pairing code once and refuse every later redemption of it.

#### Scenario: A code entered twice

- **Given** a pairing code a second device has already redeemed
- **When** a third device enters the same code
- **Then** the third device is not paired

### Requirement: A refused device is revoked

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.The_device_list_names_the_caller_and_the_device_whose_code_paired_each, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.A_revoked_device_can_no_longer_mint_a_token_and_leaves_the_list, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.A_device_cannot_revoke_itself_or_another_owners_device, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.Only_the_device_whose_code_paired_another_may_revoke_it, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DeviceStepUpTests.A_confirmed_refusal_revokes_the_device, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DeviceStepUpTests.A_refusal_the_person_did_not_confirm_is_never_sent, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DeviceStepUpTests.A_refusal_nobody_confirms_in_time_expires_and_is_never_sent, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.A_refusal_not_confirmed_keeps_the_device_and_asks_to_refuse_it_again, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.PairingSubjectsTests.The_not_refused_alert_is_worded_by_why_Windows_Hello_did_not_confirm, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsDevicesTests.Revoke_is_offered_only_on_the_rows_this_device_paired, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsDevicesTests.The_second_Revoke_revokes_the_device_once_and_its_row_goes, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsDevicesTests.A_revocation_Windows_Hello_did_not_confirm_leaves_the_row_saying_why]
```

The sync service SHALL list an owner's devices with the device whose code paired each, and SHALL let only the device whose code paired another revoke it, after which it mints no token. The desktop SHALL send a revocation only once Windows Hello confirms it.

#### Scenario: The person refuses a device

- **Given** a phone that redeemed the code the desktop showed
- **When** the person answers Refuse to "Pair *phone*?" on the desktop, and confirms it with Windows Hello
- **Then** the phone is no longer in the owner's device list, and it is refused at the token exchange

#### Scenario: Windows Hello is not confirmed

- **Given** a phone that redeemed the code the desktop showed
- **When** the person answers Refuse, then cancels the Windows Hello prompt or leaves it unanswered for 2 minutes
- **Then** no revocation is sent, the phone stays paired, and a Warning alert asks the person to refuse it again from Settings → Devices

#### Scenario: The person revokes a device this desktop paired

- **Given** the Devices settings page lists a phone this desktop paired
- **When** the person chooses Revoke, confirms it in the row, and confirms it with Windows Hello
- **Then** the phone is revoked once, and its row leaves the list

### Requirement: The registration credential is shown once

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#registration-credential, .devbook/domain/sync/domain.md#device-key]
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.Only_the_hash_of_the_credential_is_stored, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.Registering_with_a_public_key_stores_the_key_and_mints_no_credential, unit:dotnet:Backlog.Modules.Sync.UnitTests.DeviceKeyTests.Pairing_with_a_public_key_stores_the_key_and_mints_no_credential, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DeviceKeyClientTests.Registering_with_a_key_sends_the_public_key_and_stores_no_secret, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DeviceKeyClientTests.Pairing_with_a_key_sends_the_public_key_and_stores_no_secret]
```

The sync service SHALL hand a device that registers or pairs without a [Device key](domain.md#device-key) its [Registration credential](domain.md#registration-credential) only in the answer to that registration or pairing, and SHALL keep only a hash of it. A device that registers or pairs with its key SHALL get no credential.

#### Scenario: A device without a key registers

- **Given** a device with no key store registering with the sync service
- **When** the service answers
- **Then** the answer carries the credential, and the service's store holds only its hash

#### Scenario: A device with a key pairs

- **Given** a phone whose key is in Android Keystore
- **When** it redeems a pairing code and sends its public key
- **Then** the service stores the key, the answer carries no credential, and the phone stores no secret

### Requirement: A key-bound device is taken only on an assertion its key signed

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#device, .devbook/domain/sync/domain.md#device-assertion, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#2-a-device-proves-itself-with-a-non-exportable-key]
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyEndpointTests.A_key_bound_device_signs_for_a_thirty_minute_token_that_syncs, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyEndpointTests.A_key_bound_device_has_no_credential_path, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyEndpointTests.A_replayed_assertion_is_unauthorized, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyEndpointTests.An_assertion_signed_by_another_key_is_unauthorized, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyRoundTripTests.A_device_registered_with_its_key_signs_for_a_token_that_syncs_and_keeps_no_secret, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyRoundTripTests.A_bound_device_sending_its_secret_alone_is_refused]
```

The sync service SHALL mint a [Device token](domain.md#device-token) for a device bound to a [Device key](domain.md#device-key) only in exchange for a [Device assertion](domain.md#device-assertion) that key signed, and SHALL refuse a credential, a replayed assertion and an assertion another key signed (`device.credential_invalid`).

**Why:** the private key cannot leave the machine, so a copied app folder on another machine cannot sync.

#### Scenario: A copied app folder

- **Given** the app's folder copied from a key-bound desktop to another machine
- **When** the copy asks for a device token
- **Then** it has no key to sign with, and no credential the service would take, so it gets no token

#### Scenario: A replayed assertion

- **Given** an assertion the device already traded for a token
- **When** it is presented again
- **Then** the exchange is refused as unauthorized

### Requirement: A credential device moves onto its key once

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#device, .devbook/domain/sync/domain.md#registration-credential]
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyEndpointTests.A_credential_device_binds_its_key_once_and_then_signs, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyEndpointTests.A_migration_without_an_assertion_is_unauthorized_and_binds_nothing, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyRoundTripTests.A_credential_device_moves_onto_its_key_and_its_old_secret_is_refused, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceKeyRoundTripTests.A_migration_resent_after_the_bind_succeeds_and_forgets_the_secret, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.DeviceKeyClientTests.A_device_on_a_credential_moves_onto_its_key_once_and_forgets_the_secret]
```

A device on a [Registration credential](domain.md#registration-credential) that has a [Device key](domain.md#device-key) SHALL move onto it at its next token fetch, by sending its credential, its public key and an assertion that key signed; the sync service SHALL bind the key only with all three, and SHALL refuse the credential from then on. The device SHALL delete its credential once a token comes back.

#### Scenario: A desktop paired before keys

- **Given** a desktop paired on a registration credential, now on a release with a device key
- **When** it next fetches a device token
- **Then** the service binds its key and drops the credential hash, and the desktop deletes the credential
- **And** the old credential is refused from then on

#### Scenario: A migration without proof of the key

- **Given** a device on a registration credential
- **When** it sends its credential and a public key without an assertion that key signed
- **Then** the exchange is refused, and the device keeps its credential

### Requirement: A device token expires

```meta
type: requirement
tests: integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncEndpointAuthenticationTests.An_expired_token_is_refused
```

The sync service SHALL refuse a [Device token](domain.md#device-token) after its lifetime, which is thirty minutes unless the deployment sets it between 15 and 60.

#### Scenario: A token past its lifetime

- **Given** a device token that expired five minutes ago
- **When** the device calls the sync service with it
- **Then** the call is refused as unauthorized

## Syncing between devices

```meta
type: requirements
related: [.devbook/domain/sync/features.md#syncing-between-devices]
```

### Requirement: Every sync call is scoped to the token's owner

```meta
type: requirement
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.TaskSyncEndpointTests.A_separate_owner_pulls_nothing, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.TaskSyncEndpointTests.A_cursor_minted_for_another_owner_is_refused, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DevicePairingEndpointTests.A_device_only_ever_sees_its_own_owners_captures]
```

The sync service SHALL answer a call only from the partition of the [Owner](domain.md#owner) its device token names.

#### Scenario: Two unpaired backlogs

- **Given** two devices registered as separate owners, one of which pushed tasks
- **When** the other device pulls
- **Then** it receives none of the first owner's tasks

### Requirement: A device sends nothing to a newer sync service

```meta
type: requirement
related: [.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SyncCompatibilityHandlerTests.A_newer_service_is_sent_nothing_and_the_answer_asks_for_an_update, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SyncCompatibilityRegistrationTests.A_sync_client_sends_nothing_to_a_newer_service, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SyncCompatibilityRegistrationTests.The_token_request_is_held_too, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskSyncWorkerTests.A_service_newer_than_the_app_is_sent_nothing_and_the_progress_stays, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncWorkerTests.A_service_newer_than_the_app_is_sent_nothing_and_the_progress_stays]
```

A device SHALL send no sync request, its token request included, while the sync service reports a version higher than the device's app version.

#### Scenario: The service was released past the app

- **Given** a desktop at app version 0.1.4 with task changes waiting to push
- **And** a sync service whose root reports version 0.2.0
- **When** the desktop runs a sync cycle
- **Then** it sends no request but the version question
- **And** it says "Update Backlog to keep syncing."
- **And** its push watermark and pull cursor stay where they were

#### Scenario: The service is rolled back

- **Given** a device that stopped syncing for a service at version 0.2.0
- **When** the service is rolled back to 0.1.0 and ten minutes pass
- **Then** the device syncs again without a restart

### Requirement: A phone holds its outbox for a newer sync service

```meta
type: requirement
related: [.devbook/domain/sync/features.md#carrying-captures-to-the-inbox, .devbook/arc42/06-runtime-view.md#mobile-capture-and-sync]
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.DeviceOutboxTests.Entries_for_a_service_newer_than_the_app_wait_rather_than_being_refused, unit:dotnet:Backlog.Mobile.UI.UnitTests.DeviceOutboxTests.Waiting_entries_go_once_the_service_is_no_newer_than_the_app]
```

The phone SHALL hold every outbox entry in its place, with its attempts, while the sync service reports a version higher than the phone's app version.

#### Scenario: A capture made while the service is newer

- **Given** a phone whose sync service reports a version higher than its app's
- **When** the person makes a capture
- **Then** the capture waits in the outbox, not set aside as refused
- **And** it is sent once the service reports a version no higher than the app's

## Capturing from claude.ai

```meta
type: requirements
related: [.devbook/domain/sync/features.md#capturing-from-claudeai]
```

### Requirement: Only the owner authorizes the connector

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#connector-authorization, .devbook/domain/sync/domain.md#owner]
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.A_GitHub_user_other_than_the_owner_goes_back_to_claude_denied_without_a_code, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.Deny_goes_back_to_claude_as_access_denied, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.An_unknown_client_is_refused_in_place, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorAuthorizationTests.A_service_with_no_owner_authorizes_nobody, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorAuthorizationTests.The_consent_prompt_names_the_client_and_each_scope]
```

The authorization server SHALL issue an authorization code only to the `claude-connector` client, and only after the [Owner](domain.md#owner)'s GitHub account signs in and allows the scopes on the consent page. Any other GitHub account, and a Deny, SHALL go back to claude.ai as `access_denied` with no code.

#### Scenario: Someone else signs in

- **Given** claude.ai starting the connector's authorization
- **When** a GitHub account other than the owner's signs in
- **Then** claude.ai gets `access_denied` and no code

#### Scenario: The owner allows it

- **Given** the owner signed in with GitHub
- **When** the owner allows `inbox:capture` on the consent page
- **Then** claude.ai gets a code it can trade, with its verifier, for a connector token

### Requirement: A connector token reaches only /mcp

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#connector-token, .devbook/domain/sync/domain.md#device-token, .devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#8-a-remote-agent-reaches-backlog-through-oauth-21-on-the-sync-service]
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.The_access_token_is_for_the_mcp_resource_for_fifteen_minutes_and_not_cached, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.The_sync_API_refuses_a_connector_token, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_device_token_is_not_a_connector_token, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_connector_token_is_not_a_device_token, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_token_for_another_audience_is_refused_as_an_invalid_token, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_token_without_inbox_capture_is_refused_as_insufficient_scope, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_call_without_a_token_is_challenged_with_the_metadata_address]
```

The `/mcp` resource server SHALL take only a [Connector token](domain.md#connector-token) issued for its own audience, and the sync API SHALL refuse one. A call without a token SHALL be challenged with the address of the protected-resource metadata, and a token without `inbox:capture` SHALL be refused as `insufficient_scope`.

#### Scenario: A device token on /mcp

- **Given** a paired desktop's device token
- **When** it is presented to `/mcp`
- **Then** the call is refused as an invalid token

#### Scenario: A connector token on the sync API

- **Given** a connector token claude.ai holds
- **When** it is presented to a sync route
- **Then** the call is refused as unauthorized

### Requirement: A refresh token works once

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#connector-refresh-token]
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_refresh_rotates_and_the_presented_token_stops_working, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_refresh_token_older_than_thirty_days_is_an_invalid_grant, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_refresh_asking_for_more_scope_is_an_invalid_scope, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.A_refresh_rotates_and_replaying_the_old_token_revokes_the_family]
```

The authorization server SHALL answer a refresh with a new [Connector Refresh Token](domain.md#connector-refresh-token) beside the access token, SHALL refuse the presented one from then on, and SHALL refuse a refresh token older than thirty days or one asking for more scope than it was granted.

#### Scenario: claude.ai refreshes

- **Given** a refresh token claude.ai holds
- **When** it trades it for a new access token
- **Then** the answer carries a new refresh token, and the old one is refused

### Requirement: The connector secret is shown once, to the owner

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#connector-client]
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.The_secret_is_256_bits_shown_once_and_only_its_hash_is_kept, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.Only_the_configured_owner_manages_the_secret, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.A_new_secret_replaces_the_old_one, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.The_desktop_sees_the_secret_once_and_then_only_whether_it_exists]
```

The sync service SHALL let only a device of the configured [Owner](domain.md#owner) create or revoke the [Connector Client](domain.md#connector-client)'s secret (`connector.not_owner`), SHALL hand the secret back only in the answer that created it, and SHALL keep only its hash.

#### Scenario: The owner makes a secret

- **Given** the owner's desktop on the MCP server page's Remote connector card
- **When** the person creates a secret
- **Then** the card shows it once, and afterwards only that a secret exists, when it was made and when it was last used

### Requirement: Revoking the connector secret cuts claude.ai off at once

```meta
type: requirement
related: [.devbook/domain/sync/domain.md#connector-client, .devbook/domain/sync/domain.invariants.md#invariant-a-client-generation-outlives-nothing-issued-under-an-earlier-one]
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.ConnectorAuthorizationEndpointTests.Revoke_cuts_off_the_secret_every_refresh_token_and_new_authorizations, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_token_from_before_the_secret_was_revoked_is_refused, unit:dotnet:Backlog.Modules.Sync.UnitTests.ConnectorTokenTests.Revoking_leaves_no_secret_and_a_new_authorize_unauthorized]
```

When the owner revokes or replaces the connector secret, the sync service SHALL refuse every access token, refresh token and authorization code issued under the earlier secret, and a revoked client SHALL get no new authorization until a new secret is made.

#### Scenario: The owner revokes the connector

- **Given** claude.ai holding a live access token and a refresh token
- **When** the owner revokes the connector secret from the desktop
- **Then** `/mcp` refuses the access token at once, and the refresh token buys nothing

### Requirement: A capture from claude.ai lands on the replica

```meta
type: requirement
related: [.devbook/domain/sync/features.md#carrying-captures-to-the-inbox, .devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md]
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_capture_lands_on_the_replica_where_the_owners_desktop_reads_it, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.RemoteMcpEndpointTests.A_capture_the_bounds_refuse_is_a_tool_error_and_writes_nothing]
```

The `capture_to_inbox` tool on `/mcp` SHALL file a capture as a `capture`-kind document on the token's owner's replica, under the channel `claude`, through the same handler as `POST /api/sync/inbox`; a capture that handler refuses SHALL come back as a tool error and write nothing.

#### Scenario: A capture from the Claude app

- **Given** claude.ai connected with `inbox:capture`
- **When** the person asks Claude on their phone to capture a note
- **Then** the note is on the owner's replica as a capture from `claude`, and the desktop files it in the Inbox at its next sync
